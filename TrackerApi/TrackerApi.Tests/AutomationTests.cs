using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// An automation tool (n8n, Activepieces) starts and reads reports with X-Automation-Token.
// It is not an admin: report runs and the exports without names only.
public class AutomationTests
{
    private const string Token = "automation-token-for-tests-0123456789abcdef";

    private static TrackerFactory Factory(string token = Token) => new(
        settings: new() { ["Automation:Token"] = token });

    private static HttpClient WithToken(TrackerFactory factory, string token = Token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Automation-Token", token);
        return client;
    }

    [Fact]
    public async Task The_token_can_make_a_report_and_download_it_without_names()
    {
        using var factory = Factory();
        var bot = WithToken(factory);

        var started = await bot.PostJson<ReportRunDto>("/api/reports/runs", new { kind = "Day", date = "2026-11-07" });
        await factory.Services.GetRequiredService<ReportRunQueue>().DrainAsync();
        Assert.Equal(RunStatus.Done, (await bot.GetJson<ReportRunDto>($"/api/reports/runs/{started.Id}")).Status);

        Assert.Equal(HttpStatusCode.OK, (await bot.GetAsync("/api/reports/export?from=2026-11-07&format=xlsx")).StatusCode);

        // The audit log says it was the automation, not a person.
        var admin = await factory.SignedInAsync();
        var audit = Assert.Single(await admin.GetJson<List<AuditEntryDto>>("/api/audit?action=report.assistant"));
        Assert.Equal("Automation (API token)", audit.ActorName);
    }

    [Fact]
    public async Task The_token_cannot_download_names_or_reach_anything_else()
    {
        using var factory = Factory();
        var bot = WithToken(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await bot.GetAsync("/api/reports/export?format=xlsx&names=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bot.GetAsync("/api/reports/export?format=csv&table=visits")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/api/visits")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bot.DeleteAsync("/api/reports/runs/1")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_token_or_no_token_set_up_opens_nothing()
    {
        using var factory = Factory();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await WithToken(factory, Token + "x").Post("/api/reports/runs", new { kind = "Day" })).StatusCode);

        using var off = new TrackerFactory(); // Automation:Token not set
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await WithToken(off, Token).Post("/api/reports/runs", new { kind = "Day" })).StatusCode);

        using var tooShort = Factory("short");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await WithToken(tooShort, "short").Post("/api/reports/runs", new { kind = "Day" })).StatusCode);
    }
}
