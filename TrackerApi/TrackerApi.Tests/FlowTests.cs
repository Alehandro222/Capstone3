using System.Net.Http.Json;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// Each test gets its own API + database (IClassFixture is NOT used on purpose).
public class FlowTests : IDisposable
{
    private readonly TrackerFactory _factory = new();
    private readonly HttpClient _api;

    public FlowTests()
    {
        _api = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    private void Advance(TimeSpan time) => _factory.Clock.Advance(time);

    // ---- PC status ---------------------------------------------------------

    [Fact]
    public async Task Pcs_are_offline_until_they_send_a_heartbeat_and_go_offline_again_when_it_stops()
    {
        Assert.Equal(ComputerStatus.Offline, await _api.StatusOf(1));

        await _api.Heartbeat(1);
        Assert.Equal(ComputerStatus.Available, await _api.StatusOf(1));

        Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(ComputerStatus.Offline, await _api.StatusOf(1));
    }

    [Fact]
    public async Task Open_problem_report_means_maintenance_and_resolving_it_frees_the_pc()
    {
        await _api.Heartbeat(3);
        Assert.Equal(ComputerStatus.Maintenance, await _api.StatusOf(3)); // seeded report

        var open = await _api.GetJson<List<ProblemReportDto>>("/api/problems?status=Open&computerId=3");
        var response = await _api.PutAsJsonAsync($"/api/problems/{open.Single().Id}/resolve", new { notes = "New cable" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(ComputerStatus.Available, await _api.StatusOf(3));
    }

    [Fact]
    public async Task Public_list_never_contains_visitor_names()
    {
        await _api.Heartbeat(1);
        var created = await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1, "Secret", "Person"));
        await _api.PostJson<SessionDto>("/api/passes/check-in", new { code = created.Pass.Code });

        var json = await _api.GetStringAsync("/api/public/computers");
        Assert.DoesNotContain("Secret", json);
    }

    // ---- The main flow -----------------------------------------------------

    [Fact]
    public async Task Pick_pc_get_pass_staff_check_in_then_end_from_phone()
    {
        await _api.Heartbeat(1);

        // Phone: pick PC-01 and get a pass.
        var created = await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));
        Assert.Equal(PassStatus.Pending, created.Pass.Status);
        Assert.Matches("^[0-9]{6}$", created.Pass.Code);
        Assert.Equal(_factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(10), created.Pass.ExpiresAt);
        Assert.Equal(ComputerStatus.Reserved, await _api.StatusOf(1));

        // Staff: look the pass up by its QR token, then check in with the 6-digit code.
        var lookup = await _api.PostJson<StaffPassDto>("/api/passes/lookup", new { token = created.Token });
        Assert.Equal("Dela Cruz", lookup.LastName);
        Assert.Equal(new[] { Sector.Academe, Sector.Youth }, lookup.Sectors);
        Assert.Equal(Sex.Male, lookup.Sex);

        Advance(TimeSpan.FromMinutes(2));
        await _api.Heartbeat(1);
        var session = await _api.PostJson<SessionDto>("/api/passes/check-in", new { code = created.Pass.Code });
        Assert.Equal("Juan Dela Cruz", session.UserName);
        Assert.Equal("Schoolwork", session.Purpose);
        Assert.Equal(session.StartedAt.AddHours(3), session.ScheduledEndAt);
        Assert.Equal(ComputerStatus.InUse, await _api.StatusOf(1));

        // Phone: sees the session, then ends it.
        var pass = await _api.GetJson<VisitorPassDto>($"/api/passes/{created.Token}");
        Assert.Equal(PassStatus.Used, pass.Status);
        Assert.NotNull(pass.Session);

        var ended = await _api.PostJson<VisitorPassDto>($"/api/passes/{created.Token}/end");
        Assert.Equal(SessionEndReason.Visitor, ended.Session!.EndReason);
        Assert.Equal(ComputerStatus.Available, await _api.StatusOf(1));
    }

    [Fact]
    public async Task A_pass_cannot_be_used_twice()
    {
        await _api.Heartbeat(1);
        var created = await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));
        await _api.PostJson<SessionDto>("/api/passes/check-in", new { token = created.Token });

        var again = await _api.Post("/api/passes/check-in", new { token = created.Token });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This pass has already been used.", await again.Message());
    }

    [Fact]
    public async Task Second_visitor_cannot_take_a_reserved_pc()
    {
        await _api.Heartbeat(1);
        await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));

        var second = await _api.Post("/api/passes", ApiExtensions.Visitor(1, "Maria", "Santos"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("That computer was just taken. Please choose another.", await second.Message());
    }

    [Fact]
    public async Task Cancelled_pass_frees_the_pc()
    {
        await _api.Heartbeat(1);
        var created = await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));

        var cancelled = await _api.PostJson<VisitorPassDto>($"/api/passes/{created.Token}/cancel");
        Assert.Equal(PassStatus.Cancelled, cancelled.Status);
        Assert.Equal(ComputerStatus.Available, await _api.StatusOf(1));
    }

    [Fact]
    public async Task Unscanned_pass_expires_and_the_pc_can_be_picked_again()
    {
        await _api.Heartbeat(1);
        var created = await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));

        Advance(TimeSpan.FromMinutes(10));
        await _api.Heartbeat(1);

        var pass = await _api.GetJson<VisitorPassDto>($"/api/passes/{created.Token}");
        Assert.Equal(PassStatus.Expired, pass.Status);

        var checkIn = await _api.Post("/api/passes/check-in", new { token = created.Token });
        Assert.Equal(HttpStatusCode.Conflict, checkIn.StatusCode);

        // The cleanup job has not run, but the PC is free again anyway.
        Assert.Equal(ComputerStatus.Available, await _api.StatusOf(1));
        await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1, "Maria", "Santos"));
    }

    [Fact]
    public async Task Check_in_is_refused_when_the_pc_went_offline()
    {
        await _api.Heartbeat(1);
        var created = await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));

        Advance(TimeSpan.FromMinutes(1)); // no heartbeat for a minute

        var checkIn = await _api.Post("/api/passes/check-in", new { code = created.Pass.Code });
        Assert.Equal(HttpStatusCode.Conflict, checkIn.StatusCode);
        Assert.Contains("offline", await checkIn.Message());
    }

    [Fact]
    public async Task Registration_form_is_validated()
    {
        await _api.Heartbeat(1);
        var response = await _api.Post("/api/passes", new
        {
            computerId = 1, firstName = "Juan", lastName = "Dela Cruz", age = 20, purpose = "Schoolwork",
            sectors = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Please select at least one sector.", await response.Message());

        response = await _api.Post("/api/passes", new
        {
            computerId = 1, firstName = "Juan", lastName = "Dela Cruz", age = 0, purpose = "Schoolwork",
            sectors = new[] { "Youth" }
        });
        Assert.Equal("Please enter a valid age.", await response.Message());
    }

    [Fact]
    public async Task Walk_in_starts_a_session_right_away()
    {
        await _api.Heartbeat(2);
        var session = await _api.PostJson<SessionDto>("/api/passes/walk-in", ApiExtensions.Visitor(2, "Lola", "Reyes"));
        Assert.Equal("Lola Reyes", session.UserName);
        Assert.Equal(ComputerStatus.InUse, await _api.StatusOf(2));

        var passes = await _api.GetJson<List<StaffPassDto>>("/api/passes?status=Used");
        Assert.True(passes.Single().IsWalkIn);
    }

    // ---- Time limit and extensions ------------------------------------------

    [Fact]
    public async Task Session_can_be_extended_only_near_the_end_and_at_most_twice()
    {
        await _api.Heartbeat(1);
        var session = await _api.PostJson<SessionDto>("/api/passes/walk-in", ApiExtensions.Visitor(1));
        Assert.False(session.CanExtend);

        var early = await _api.Post($"/api/sessions/{session.Id}/extend");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Advance(TimeSpan.FromMinutes(171)); // 9 minutes left
        session = await _api.PostJson<SessionDto>($"/api/sessions/{session.Id}/extend");
        Assert.Equal(1, session.ExtensionCount);
        Assert.Equal(session.StartedAt.AddHours(4), session.ScheduledEndAt);

        Advance(TimeSpan.FromMinutes(61)); // 8 minutes left again
        session = await _api.PostJson<SessionDto>($"/api/sessions/{session.Id}/extend");
        Assert.Equal(2, session.ExtensionCount);

        Advance(TimeSpan.FromMinutes(61));
        var third = await _api.Post($"/api/sessions/{session.Id}/extend");
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("A session can only be extended 2 times.", await third.Message());
    }

    [Fact]
    public async Task No_extension_while_someone_is_on_the_waitlist()
    {
        // Only PC-01 is online, and it is in use, so nothing is available.
        await _api.Heartbeat(1);
        var session = await _api.PostJson<SessionDto>("/api/passes/walk-in", ApiExtensions.Visitor(1));

        Advance(TimeSpan.FromMinutes(175));
        await _api.Heartbeat(1);

        var joined = await _api.PostJson<WaitlistJoinedDto>("/api/waitlist", new { firstName = "Ana" });
        Assert.Equal(1, joined.Entry.Position);

        var extend = await _api.Post($"/api/sessions/{session.Id}/extend");
        Assert.Equal(HttpStatusCode.Conflict, extend.StatusCode);
        Assert.Equal("Someone is waiting for a computer.", await extend.Message());

        // Once Ana leaves the line, extending works.
        (await _api.DeleteAsync($"/api/waitlist/{joined.Token}")).EnsureSuccessStatusCode();
        await _api.PostJson<SessionDto>($"/api/sessions/{session.Id}/extend");
    }

    [Fact]
    public async Task Cannot_join_the_waitlist_while_a_pc_is_free()
    {
        await _api.Heartbeat(1);
        var response = await _api.Post("/api/waitlist", new { firstName = "Ana" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cleanup_ends_sessions_at_the_time_limit_and_expires_passes()
    {
        await _api.Heartbeat(1, 2);
        var session = await _api.PostJson<SessionDto>("/api/passes/walk-in", ApiExtensions.Visitor(1));
        await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(2, "Maria", "Santos"));

        Advance(TimeSpan.FromHours(3));
        using (var scope = _factory.Services.CreateScope())
            await CleanupService.RunOnceAsync(scope.ServiceProvider);

        var ended = await _api.GetJson<SessionDto>($"/api/sessions/{session.Id}");
        Assert.Equal(SessionEndReason.TimeLimit, ended.EndReason);

        var passes = await _api.GetJson<List<StaffPassDto>>("/api/passes?status=Expired");
        Assert.Single(passes);
    }

    // ---- Old kiosk login and the dashboard ------------------------------------

    [Fact]
    public async Task Kiosk_login_form_and_dashboard_buttons_still_work()
    {
        await _api.Heartbeat(1);
        var session = await _api.PostJson<SessionDto>("/api/sessions/start",
            new { computerId = 1, userName = "Maria", purpose = "Printing" });
        Assert.Equal(session.StartedAt.AddHours(3), session.ScheduledEndAt);

        // The dashboard sends no body at all; the kiosk sends {}.
        var end = await _api.PostAsync($"/api/sessions/{session.Id}/end", null);
        end.EnsureSuccessStatusCode();

        // "Report broken" then "Mark as fixed".
        (await _api.PutAsJsonAsync("/api/computers/1/status", new { status = "Maintenance" }, TrackerFactory.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(ComputerStatus.Maintenance, await _api.StatusOf(1));
        (await _api.PutAsJsonAsync("/api/computers/1/status", new { status = "Available" }, TrackerFactory.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(ComputerStatus.Available, await _api.StatusOf(1));

        var history = await _api.GetJson<List<SessionDto>>("/api/computers/1/sessions");
        Assert.Single(history);
    }

    [Fact]
    public async Task Times_read_back_from_the_database_are_marked_utc()
    {
        await _api.Heartbeat(1);
        await _api.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));

        var json = await _api.GetStringAsync("/api/passes?status=Pending"); // freshly loaded from SQLite
        Assert.Matches("\"createdAt\":\"[^\"]+Z\"", json);
    }

    // ---- Rules the database enforces on its own --------------------------------

    [Fact]
    public async Task Database_refuses_a_second_active_session_on_the_same_pc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
        var now = DateTime.UtcNow;

        db.Sessions.Add(new UsageSession { ComputerId = 1, UserName = "A", Purpose = "x", StartedAt = now, ScheduledEndAt = now });
        db.Sessions.Add(new UsageSession { ComputerId = 1, UserName = "B", Purpose = "x", StartedAt = now, ScheduledEndAt = now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_refuses_a_session_on_a_pc_that_does_not_exist()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        db.Sessions.Add(new UsageSession { ComputerId = 999, UserName = "A", Purpose = "x" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
