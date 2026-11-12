using System.Net;
using System.Text;
using System.Text.Json;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// The Gemini monthly-report assistant. Google's free tier may use what it is sent, so
// these tests check what actually leaves the server: counts only, never a name.
public class AssistantTests
{
    // Stands in for Gemini: records each request and answers with a canned draft (or an error).
    private sealed class FakeGemini : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            var body = Status == HttpStatusCode.OK
                ? """{ "candidates": [ { "content": { "parts": [ { "text": "## Summary\n- 2 visits." } ] } } ] }"""
                : """{ "error": { "code": 429, "message": "quota" } }""";
            return new HttpResponseMessage(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Gemini_gets_the_months_counts_and_never_a_name()
    {
        var gemini = new FakeGemini();
        using var factory = new TrackerFactory(gemini: gemini);
        var admin = await factory.SignedInAsync();
        await factory.Heartbeat(1);
        await admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1, "Zenaida", "Quiapo"));
        await factory.Anonymous().Register("Xandro", "Villanueva");

        var status = await admin.GetJson<AssistantStatusDto>("/api/reports/assistant");
        Assert.True(status.Configured);

        var result = await admin.PostJson<AssistantDraftDto>("/api/reports/assistant", new { from = "2026-11-01", to = "2026-11-30" });
        Assert.Equal("## Summary\n- 2 visits.", result.Draft);
        Assert.Equal("gemini-2.5-flash", result.Model);

        var (request, body) = Assert.Single(gemini.Requests);
        Assert.EndsWith("/v1beta/models/gemini-2.5-flash:generateContent", request.RequestUri!.AbsoluteUri);
        Assert.Equal("test-gemini-key", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("key=", request.RequestUri.Query); // the key is not in the URL (URLs end up in logs)

        // What staff are shown is exactly what was sent, and it holds counts only.
        var prompt = JsonDocument.Parse(body).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.EndsWith(result.SentToGemini, prompt);
        foreach (var name in new[] { "Zenaida", "Quiapo", "Xandro", "Villanueva" })
            Assert.DoesNotContain(name, body, StringComparison.OrdinalIgnoreCase);

        var facts = JsonDocument.Parse(result.SentToGemini).RootElement;
        Assert.Equal(2, facts.GetProperty("totals").GetProperty("visits").GetInt32());
        Assert.Equal(1, facts.GetProperty("visitorsBySector").GetProperty("Senior Citizen").GetInt32());
        Assert.Equal(2, facts.GetProperty("visitsByHourOfDay").GetProperty("09:00").GetInt32()); // both at 01:00 UTC = 9 AM in Baguio

        var audit = await admin.GetJson<List<AuditEntryDto>>("/api/audit?action=report.assistant");
        Assert.Contains("counts only", Assert.Single(audit).Details);
    }

    [Fact]
    public async Task Without_an_api_key_it_says_how_to_set_it_up()
    {
        using var factory = new TrackerFactory();
        var admin = await factory.SignedInAsync();

        Assert.False((await admin.GetJson<AssistantStatusDto>("/api/reports/assistant")).Configured);
        var response = await admin.Post("/api/reports/assistant", new { from = "2026-11-01", to = "2026-11-30" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Gemini API key", await response.Message());
    }

    [Fact]
    public async Task Only_admins_can_use_it()
    {
        using var factory = new TrackerFactory(gemini: new FakeGemini());
        var staff = await factory.SignedInAsync("desk", StaffRole.Staff);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.Post("/api/reports/assistant", new { from = "2026-11-01", to = "2026-11-30" })).StatusCode);
    }

    [Fact]
    public async Task Hitting_the_free_limit_gives_a_clear_message()
    {
        using var factory = new TrackerFactory(gemini: new FakeGemini { Status = HttpStatusCode.TooManyRequests });
        var admin = await factory.SignedInAsync();

        var response = await admin.Post("/api/reports/assistant", new { from = "2026-11-01", to = "2026-11-30" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("free limit", await response.Message());
        Assert.Empty(await admin.GetJson<List<AuditEntryDto>>("/api/audit?action=report.assistant"));
    }
}
