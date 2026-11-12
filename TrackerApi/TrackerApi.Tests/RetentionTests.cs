using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// DTC keeps visit records for 1 year; after that the whole record is deleted.
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
    public async Task Visits_older_than_a_year_are_deleted_with_everything_that_belongs_to_them()
    {
        // Maria: a walk-in with a PC session. Pia: picked a PC on her phone (a pass), never scanned.
        await _factory.Heartbeat(1, 2);
        var maria = await _admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1, "Maria", "Santos"));
        await _admin.PostJson<SessionDto>($"/api/sessions/{maria.Session!.Id}/end");
        await _factory.Anonymous().RegisterAndPick(2, "Pia", "Lopez");

        _factory.Clock.Advance(TimeSpan.FromDays(200));
        await _factory.Anonymous().Register("Jose", "Rizal"); // only 166 days old at the end

        _factory.Clock.Advance(TimeSpan.FromDays(166)); // Maria and Pia: 366 days old
        await RunCleanupAsync();
        await RunCleanupAsync(); // a second run finds nothing more
        _admin = await _factory.SignedInAsync("admin2");

        var visits = await _admin.GetJson<List<StaffVisitDto>>("/api/visits?limit=10");
        Assert.Equal("Jose", Assert.Single(visits).FirstName);
        Assert.Empty(await _admin.GetJson<List<SessionDto>>("/api/sessions?limit=10"));
        Assert.Empty(await _admin.GetJson<List<StaffPassDto>>("/api/passes"));

        var day = DateOnly.FromDateTime(maria.Visit.CreatedAt.AddHours(8));
        var report = await _admin.GetJson<ReportDto>($"/api/reports/summary?from={day:yyyy-MM-dd}");
        Assert.Equal((0, 0), (report.Visits, report.Sessions));

        var audit = Assert.Single(await _admin.GetJson<List<AuditEntryDto>>("/api/audit?action=retention."));
        Assert.Equal("retention.visits_deleted", audit.Action);
        Assert.Contains("2 visits and 1 PC sessions", audit.Details);
    }

    [Fact]
    public async Task A_visit_under_a_year_old_is_kept()
    {
        await _factory.Anonymous().Register("Ana", "Reyes");
        _factory.Clock.Advance(TimeSpan.FromDays(364));
        await RunCleanupAsync();
        _admin = await _factory.SignedInAsync("admin2");

        Assert.Equal("Ana", Assert.Single(await _admin.GetJson<List<StaffVisitDto>>("/api/visits")).FirstName);
    }
}
