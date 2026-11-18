using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// The report runs (day / week / month reports made in the background, with Gemini writing the
// text). Google's free tier may use what it is sent, so these tests check what actually leaves
// the server (counts only, never a name), and that no number in a finished report is one the
// data does not have.
public class AssistantTests
{
    // Stands in for Gemini: records each request and answers with the next canned draft (or an error).
    private sealed class FakeGemini : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        public Queue<string> Drafts { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            var text = Drafts.Count > 0 ? Drafts.Dequeue() : "## Summary\n- 2 visits.";
            var body = Status == HttpStatusCode.OK
                ? JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } } } } })
                : """{ "error": { "code": 429, "message": "quota" } }""";
            return new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    // Starts a run, waits for the background job to finish, and returns the finished run.
    private static async Task<ReportRunDto> RunAsync(TrackerFactory factory, HttpClient admin, string kind, string date)
    {
        var started = await admin.PostJson<ReportRunDto>("/api/reports/runs", new { kind, date });
        Assert.Equal(RunStatus.Queued, started.Status);
        await factory.Services.GetRequiredService<ReportRunQueue>().DrainAsync();
        return await admin.GetJson<ReportRunDto>($"/api/reports/runs/{started.Id}");
    }

    // Two visits today (Nov 7, 9 AM in Baguio), both with real names the report must never carry.
    private static async Task AddTwoVisitsAsync(TrackerFactory factory, HttpClient admin)
    {
        await factory.Heartbeat(1);
        await admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1, "Zenaida", "Quiapo"));
        await factory.Anonymous().Register("Xandro", "Villanueva");
    }

    private static string PromptOf(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;

    [Fact]
    public async Task Gemini_gets_the_periods_counts_and_never_a_name()
    {
        var gemini = new FakeGemini();
        using var factory = new TrackerFactory(gemini: gemini);
        var admin = await factory.SignedInAsync();
        await AddTwoVisitsAsync(factory, admin);

        Assert.True((await admin.GetJson<AssistantStatusDto>("/api/reports/assistant")).Configured);

        var run = await RunAsync(factory, admin, "Month", "2026-11-07");
        Assert.Equal(RunStatus.Done, run.Status);
        Assert.Equal(new DateOnly(2026, 11, 1), run.From);
        Assert.Equal(new DateOnly(2026, 11, 7), run.To); // the month is not over: counted up to today
        Assert.True(run.Document!.Partial);
        Assert.Equal("## Summary\n- 2 visits.", run.Document.Narrative);
        Assert.True(run.Document.NarrativeChecked);
        Assert.Equal(2, run.Document.Current.Visits);

        var (request, body) = Assert.Single(gemini.Requests);
        Assert.EndsWith("/v1beta/models/gemini-2.5-flash:generateContent", request.RequestUri!.AbsoluteUri);
        Assert.Equal("test-gemini-key", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("key=", request.RequestUri.Query); // the key is not in the URL (URLs end up in logs)

        // What staff are shown is exactly what was sent, and it holds counts only.
        Assert.EndsWith(run.SentToGemini!, PromptOf(body));
        var saved = JsonSerializer.Serialize(run, TrackerFactory.Json);
        foreach (var name in new[] { "Zenaida", "Quiapo", "Xandro", "Villanueva" })
        {
            Assert.DoesNotContain(name, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(name, saved, StringComparison.OrdinalIgnoreCase); // nor does the saved report
        }

        var facts = JsonDocument.Parse(run.SentToGemini!).RootElement;
        Assert.Equal(2, facts.GetProperty("totals").GetProperty("visits").GetInt32());
        Assert.Equal(1, facts.GetProperty("visitorsBySector").GetProperty("Senior Citizen").GetInt32());
        Assert.Equal(2, facts.GetProperty("visitsByHourOfDay").GetProperty("09:00").GetInt32()); // both at 01:00 UTC = 9 AM in Baguio
        Assert.Equal("09:00", facts.GetProperty("busiestHourOfDay").GetProperty("hour").GetString());
        Assert.Equal(100, facts.GetProperty("percentOfVisits").GetProperty("service").GetProperty("Use of ICT Equipment").GetDouble());
        Assert.Equal(0, facts.GetProperty("comparedWithPreviousPeriod").GetProperty("rows")[0].GetProperty("before").GetInt32());

        var audit = await admin.GetJson<List<AuditEntryDto>>("/api/audit?action=report.assistant");
        Assert.Contains("counts only", Assert.Single(audit).Details);
    }

    [Fact]
    public async Task A_number_that_is_not_in_the_data_is_sent_back_and_then_left_out()
    {
        var gemini = new FakeGemini();
        gemini.Drafts.Enqueue("## Summary\n- 99 visits were logged.");
        using var factory = new TrackerFactory(gemini: gemini);
        var admin = await factory.SignedInAsync();
        await AddTwoVisitsAsync(factory, admin);

        // First draft invents "5"; the second is right. The second request names the bad number.
        var run = await RunAsync(factory, admin, "Week", "2026-11-07");
        Assert.Equal(2, gemini.Requests.Count);
        Assert.StartsWith("Your previous draft used these numbers, which are not in the data: 5.", PromptOf(gemini.Requests[1].Body));
        Assert.Equal("## Summary\n- 2 visits.", run.Document!.Narrative);
        Assert.True(run.Document.NarrativeChecked);
        Assert.Null(run.Note);

        // Both drafts invent a number: the report keeps its numbers and tables, but not the text.
        gemini.Drafts.Enqueue("- 99 visits.");
        gemini.Drafts.Enqueue("- 98 visits.");
        var bad = await RunAsync(factory, admin, "Week", "2026-11-07");
        Assert.Equal(RunStatus.Done, bad.Status);
        Assert.Equal("", bad.Document!.Narrative);
        Assert.False(bad.Document.NarrativeChecked);
        Assert.Contains("98", bad.Note);
        Assert.Equal(2, bad.Document.Current.Visits); // the figures themselves come from the data
        Assert.Equal(4, gemini.Requests.Count); // two tries for each report, never more
    }

    [Fact]
    public async Task A_day_report_is_written_by_code_and_sends_nothing_to_gemini()
    {
        var gemini = new FakeGemini();
        using var factory = new TrackerFactory(gemini: gemini);
        var admin = await factory.SignedInAsync();
        await AddTwoVisitsAsync(factory, admin);

        var run = await RunAsync(factory, admin, "Day", "2026-11-07");
        Assert.Empty(gemini.Requests);
        Assert.Null(run.SentToGemini);
        Assert.Contains("2 visits were logged for November 7, 2026", run.Document!.Narrative);
        Assert.True(run.Document.NarrativeChecked);
    }

    [Fact]
    public async Task Without_an_api_key_the_report_still_has_its_numbers()
    {
        using var factory = new TrackerFactory();
        var admin = await factory.SignedInAsync();

        Assert.False((await admin.GetJson<AssistantStatusDto>("/api/reports/assistant")).Configured);
        var run = await RunAsync(factory, admin, "Month", "2026-11-07");
        Assert.Equal(RunStatus.Done, run.Status);
        Assert.Contains("Gemini is not set up", run.Note);
        Assert.Equal("", run.Document!.Narrative);
    }

    [Fact]
    public async Task Hitting_the_free_limit_gives_a_clear_note_not_a_broken_report()
    {
        using var factory = new TrackerFactory(gemini: new FakeGemini { Status = HttpStatusCode.TooManyRequests });
        var admin = await factory.SignedInAsync();

        var run = await RunAsync(factory, admin, "Month", "2026-11-07");
        Assert.Equal(RunStatus.Done, run.Status);
        Assert.Contains("free limit", run.Note);
        Assert.NotNull(run.Document);
    }

    [Fact]
    public async Task Only_admins_can_use_it()
    {
        using var factory = new TrackerFactory(gemini: new FakeGemini());
        var staff = await factory.SignedInAsync("desk", StaffRole.Staff);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.Post("/api/reports/runs", new { kind = "Month", date = "2026-11-07" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/reports/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/reports/runs/1")).StatusCode);
    }

    [Fact]
    public async Task Periods_that_have_not_started_or_are_past_the_retention_are_refused()
    {
        using var factory = new TrackerFactory(gemini: new FakeGemini());
        var admin = await factory.SignedInAsync();

        var future = await admin.Post("/api/reports/runs", new { kind = "Month", date = "2026-12-05" });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        var old = await admin.Post("/api/reports/runs", new { kind = "Month", date = "2025-01-05" });
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Contains("no longer complete", await old.Message());
    }

    [Fact]
    public async Task One_report_at_a_time_and_a_run_lost_in_a_restart_is_marked_failed()
    {
        using var factory = new TrackerFactory(gemini: new FakeGemini());
        var admin = await factory.SignedInAsync();

        int stuckId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
            var stuck = new ReportRun
            {
                Kind = ReportKind.Month, From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 7),
                Status = RunStatus.Running, CreatedAt = factory.Clock.GetUtcNow().UtcDateTime
            };
            db.ReportRuns.Add(stuck);
            await db.SaveChangesAsync();
            stuckId = stuck.Id;
        }

        var busy = await admin.Post("/api/reports/runs", new { kind = "Day", date = "2026-11-07" });
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);

        // What the worker does when the server starts again.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ReportRunner>().FailInterruptedAsync();
        var run = await admin.GetJson<ReportRunDto>($"/api/reports/runs/{stuckId}");
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("restarted", run.Note);

        // Now a new one can start; finished reports are listed and can be deleted.
        var done = await RunAsync(factory, admin, "Day", "2026-11-07");
        Assert.Equal(2, (await admin.GetJson<List<ReportRunListItemDto>>("/api/reports/runs")).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/reports/runs/{done.Id}")).StatusCode);
        Assert.Single(await admin.GetJson<List<ReportRunListItemDto>>("/api/reports/runs"));
    }
}
