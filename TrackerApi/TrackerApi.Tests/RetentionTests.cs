using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// DTC keeps visitors' names for 1 year; after that only the anonymous answers stay.
public class RetentionTests : IAsyncLifetime
{
    private readonly TrackerFactory _factory = new();
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await _factory.SignedInAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task RunCleanupAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await CleanupService.RunOnceAsync(scope.ServiceProvider);
    }

    [Fact]
    public async Task Names_are_removed_after_a_year_but_the_visit_still_counts()
    {
        await _factory.Heartbeat(1);
        var old = await _admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1, "Maria", "Santos"));
        await _admin.PostJson<SessionDto>($"/api/sessions/{old.Session!.Id}/end");

        _factory.Clock.Advance(TimeSpan.FromDays(200));
        await _factory.Anonymous().Register("Jose", "Rizal"); // only 165 days old at the end

        _factory.Clock.Advance(TimeSpan.FromDays(166)); // Maria's visit is now 366 days old
        await RunCleanupAsync();
        await RunCleanupAsync(); // a second run changes nothing
        _admin = await _factory.SignedInAsync("admin2");

        var visits = await _admin.GetJson<List<StaffVisitDto>>("/api/visits?limit=10");
        var maria = visits.Single(v => v.Id == old.Visit.Id);
        Assert.Equal(("", ""), (maria.FirstName, maria.LastName));
        Assert.Equal(AgeGroup.Age60AndAbove, maria.AgeGroup); // the anonymous answers stay
        Assert.Contains(visits, v => v.FirstName == "Jose");

        var session = await _admin.GetJson<SessionDto>($"/api/sessions/{old.Session.Id}");
        Assert.Equal(Visit.RemovedName, session.UserName);

        var day = DateOnly.FromDateTime(old.Visit.CreatedAt.AddHours(8));
        var report = await _admin.GetJson<ReportDto>($"/api/reports/summary?from={day:yyyy-MM-dd}");
        Assert.Equal(1, report.Visits);
        Assert.Equal(1, report.Sessions);

        var audit = await _admin.GetJson<List<AuditEntryDto>>("/api/audit?action=retention.");
        Assert.Contains("1 visits", Assert.Single(audit).Details);
    }

    [Fact]
    public async Task A_visit_under_a_year_old_keeps_its_name()
    {
        await _factory.Anonymous().Register("Ana", "Reyes");
        _factory.Clock.Advance(TimeSpan.FromDays(364));
        await RunCleanupAsync();
        _admin = await _factory.SignedInAsync("admin2");

        Assert.Equal("Ana", (await _admin.GetJson<List<StaffVisitDto>>("/api/visits")).Single().FirstName);
    }
}
