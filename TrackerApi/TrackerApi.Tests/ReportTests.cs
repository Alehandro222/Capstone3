using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// Reports count days and hours in Philippine time (UTC+8).
// The fake clock starts at 2026-11-07 01:00 UTC = 09:00 in Baguio.
public class ReportTests : IAsyncLifetime
{
    private readonly TrackerFactory _factory = new();
    private HttpClient _admin = null!;
    private HttpClient _phone = null!;

    public async Task InitializeAsync()
    {
        _admin = await _factory.SignedInAsync();
        _phone = _factory.Anonymous();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private void Advance(TimeSpan time) => _factory.Clock.Advance(time);

    // Nov 7, 09:00 PH: Ana gets PC-01 at the desk for 90 minutes.
    // Nov 7, 23:30 PH: Ben registers on his phone.   Nov 8, 00:30 PH: Cy registers (next day!).
    private async Task SeedAsync()
    {
        await _factory.Heartbeat(1);
        var walkIn = await _admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1, "Ana", "Reyes"));
        Advance(TimeSpan.FromMinutes(90));
        await _admin.PostJson<SessionDto>($"/api/sessions/{walkIn.Session!.Id}/end");

        Advance(TimeSpan.FromHours(13)); // 15:30 UTC = 23:30 PH
        await _phone.Register("Ben", "Cruz");
        Advance(TimeSpan.FromHours(1));  // 16:30 UTC = 00:30 PH, Nov 8
        await _phone.Register("Cy", "Lim");

        _admin = await _factory.SignedInAsync("admin2"); // the first sign-in has expired by now
    }

    [Fact]
    public async Task Summary_counts_visits_and_pc_use_by_philippine_day_and_hour()
    {
        await SeedAsync();

        var nov7 = await _admin.GetJson<ReportDto>("/api/reports/summary?from=2026-11-07&to=2026-11-07");
        Assert.Equal("Asia/Manila", nov7.TimeZone);
        Assert.Equal(2, nov7.Visits); // Cy registered after midnight in Baguio, so on Nov 8
        Assert.Equal(1, nov7.VisitsWithComputer);
        Assert.Equal(1, nov7.Sessions);
        Assert.Equal(1.5, nov7.SessionHours);
        Assert.Equal(90, nov7.AverageSessionMinutes);
        Assert.Equal(1, nov7.VisitsByHour.Single(h => h.Key == "9").Count);
        Assert.Equal(1, nov7.VisitsByHour.Single(h => h.Key == "23").Count);
        Assert.Equal(1, nov7.SessionsByHour.Single(h => h.Key == "9").Count);
        Assert.Equal(24, nov7.VisitsByHour.Count);

        // Every answer is listed, also those nobody picked.
        Assert.Equal(4, nov7.ByAgeGroup.Count);
        Assert.Equal(1, nov7.ByAgeGroup.Single(a => a.Key == "Age60AndAbove").Count); // Ana (walk-in form)
        Assert.Equal(1, nov7.ByAgeGroup.Single(a => a.Key == "Age18To30").Count);     // Ben
        Assert.Equal(0, nov7.ByAgeGroup.Single(a => a.Key == "Age17AndUnder").Count);
        Assert.Equal(10, nov7.BySector.Count);
        Assert.Equal(1, nov7.BySector.Single(s => s.Key == "SeniorCitizen").Count);
        Assert.Equal(1, nov7.BySector.Single(s => s.Key == "Youth").Count);
        Assert.Equal(2, nov7.ByService.Single(s => s.Key == "IctEquipment").Count);
        Assert.Equal(1, nov7.BySource.Single(s => s.Key == "Desk").Count);
        Assert.Equal(1, nov7.BySource.Single(s => s.Key == "Phone").Count);
        Assert.Equal(1, nov7.SessionEndReasons.Single(s => s.Key == "Staff").Count);

        var pc1 = nov7.ByComputer.Single(c => c.ComputerId == 1);
        Assert.Equal((1, 1.5), (pc1.Sessions, pc1.Hours));
        Assert.Equal(12, nov7.ByComputer.Count);

        var both = await _admin.GetJson<ReportDto>("/api/reports/summary?from=2026-11-07&to=2026-11-08");
        Assert.Equal(3, both.Visits);
        Assert.Equal(new[] { 2, 1 }, both.ByDay.Select(d => d.Visits));
        Assert.Equal(new DateOnly(2026, 11, 8), both.ByDay[1].Day);
    }

    [Fact]
    public async Task A_session_that_ran_out_while_the_server_was_off_counts_only_until_its_limit()
    {
        await _factory.Heartbeat(1, 2);
        var stopped = await _admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1, "Eva", "Tan"));
        await _admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(2, "Fe", "Uy")); // still running

        Advance(TimeSpan.FromHours(10)); // the server was off; the cleanup job runs when it comes back
        using (var scope = _factory.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<TrackerApi.Services.SessionService>();
            await sessions.EndOverdueAsync();
        }
        _admin = await _factory.SignedInAsync("admin2");

        var ended = await _admin.GetJson<SessionDto>($"/api/sessions/{stopped.Session!.Id}");
        Assert.Equal(SessionEndReason.TimeLimit, ended.EndReason);
        Assert.Equal(ended.ScheduledEndAt, ended.EndedAt); // not 10 hours later

        var report = await _admin.GetJson<ReportDto>("/api/reports/summary?from=2026-11-07");
        Assert.Equal(6, report.SessionHours); // 3 h + 3 h, not 10 h + 10 h
    }

    [Fact]
    public async Task Summary_without_dates_is_today_in_the_philippines()
    {
        Advance(TimeSpan.FromHours(16)); // 17:00 UTC Nov 7 = 01:00 PH Nov 8
        await _phone.Register("Dee", "Ong");
        _admin = await _factory.SignedInAsync("admin2");

        var today = await _admin.GetJson<ReportDto>("/api/reports/summary");
        Assert.Equal(new DateOnly(2026, 11, 8), today.From);
        Assert.Equal(1, today.Visits);
    }

    [Fact]
    public async Task Bad_ranges_are_refused()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.GetAsync("/api/reports/summary?from=2026-11-08&to=2026-11-07")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.GetAsync("/api/reports/summary?from=2025-01-01&to=2026-11-07")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _phone.GetAsync("/api/reports/summary")).StatusCode);
    }

    [Fact]
    public async Task Staff_download_counts_but_only_admins_download_names()
    {
        await SeedAsync();
        var staff = await _factory.SignedInAsync("desk", StaffRole.Staff);

        var counts = await staff.GetAsync("/api/reports/export?from=2026-11-07&to=2026-11-08&format=xlsx");
        counts.EnsureSuccessStatusCode();
        using (var book = new XLWorkbook(await counts.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(new[] { "Summary", "By day" }, book.Worksheets.Select(w => w.Name));
            Assert.Equal("Visits", book.Worksheet("Summary").Cell(5, 2).GetString());
            Assert.Equal(3, book.Worksheet("Summary").Cell(5, 3).GetValue<int>());
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.GetAsync("/api/reports/export?format=xlsx&names=true")).StatusCode);
        var refused = await staff.GetAsync("/api/reports/export?format=csv&table=visits");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("Only admins", await refused.Message());

        var full = await _admin.GetAsync("/api/reports/export?from=2026-11-07&to=2026-11-08&format=xlsx&names=true");
        full.EnsureSuccessStatusCode();
        using (var book = new XLWorkbook(await full.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(new[] { "Summary", "By day", "Visits", "PC sessions" }, book.Worksheets.Select(w => w.Name));
            Assert.Equal("Ana", book.Worksheet("Visits").Cell(2, 2).GetString());
            Assert.Equal(new DateTime(2026, 11, 7, 9, 0, 0), book.Worksheet("Visits").Cell(2, 1).GetDateTime()); // PH time
            Assert.Equal("PC-01", book.Worksheet("PC sessions").Cell(2, 1).GetString());
            Assert.Equal(90, book.Worksheet("PC sessions").Cell(2, 5).GetValue<int>());
        }

        var audit = await _admin.GetJson<List<AuditEntryDto>>("/api/audit?action=report.export");
        Assert.Equal(2, audit.Count); // the refused downloads are not recorded as downloads
        Assert.Contains(audit, a => a.Details!.Contains("with names"));
    }

    [Fact]
    public async Task Csv_has_a_bom_for_excel_and_cannot_smuggle_formulas()
    {
        await _phone.Register("=HYPERLINK(\"http://x\")", "Santos, Jr.");

        var csv = await _admin.GetAsync("/api/reports/export?format=csv&table=visits");
        csv.EnsureSuccessStatusCode();
        Assert.Equal("misa-visits_2026-11-07.csv", csv.Content.Headers.ContentDisposition!.FileNameStar ?? csv.Content.Headers.ContentDisposition.FileName);
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);

        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\n')).ToArray();
        Assert.StartsWith("Date and time,First name,Last name", lines[0]);
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"", lines[1]); // quoted, and starts with ' so it stays text
        Assert.Contains("\"Santos, Jr.\"", lines[1]);
        Assert.StartsWith("2026-11-07 09:00,", lines[1]);
    }
}
