using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// Each test gets its own API + database (IClassFixture is NOT used on purpose).
// _staff is a staff member signed in to the dashboard; _phone is a visitor's
// phone (no sign-in); _factory.Heartbeat(...) is the kiosk app on those PCs.
public class FlowTests : IAsyncLifetime
{
    private readonly TrackerFactory _factory = new();
    private HttpClient _staff = null!;
    private HttpClient _phone = null!;

    public async Task InitializeAsync()
    {
        _staff = await _factory.SignedInAsync();
        _phone = _factory.Anonymous();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private void Advance(TimeSpan time) => _factory.Clock.Advance(time);

    // ---- PC status ---------------------------------------------------------

    [Fact]
    public async Task Pcs_are_offline_until_they_send_a_heartbeat_and_go_offline_again_when_it_stops()
    {
        Assert.Equal(ComputerStatus.Offline, await _phone.StatusOf(1));

        await _factory.Heartbeat(1);
        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(1));

        Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(ComputerStatus.Offline, await _phone.StatusOf(1));
    }

    [Fact]
    public async Task Open_problem_report_means_maintenance_and_resolving_it_frees_the_pc()
    {
        await _factory.Heartbeat(3);
        Assert.Equal(ComputerStatus.Maintenance, await _phone.StatusOf(3)); // seeded report

        var open = await _staff.GetJson<List<ProblemReportDto>>("/api/problems?status=Open&computerId=3");
        var response = await _staff.PutAsJsonAsync($"/api/problems/{open.Single().Id}/resolve", new { notes = "New cable" });
        response.EnsureSuccessStatusCode();

        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(3));
    }

    [Fact]
    public async Task Public_list_never_contains_visitor_names()
    {
        await _factory.Heartbeat(1);
        var (_, code) = await _phone.RegisterAndPick(1, "Secret", "Person");
        await _staff.PostJson<SessionDto>("/api/passes/check-in", new { code });

        var json = await _phone.GetStringAsync("/api/public/computers");
        Assert.DoesNotContain("Secret", json);
    }

    // ---- Registering (the DTC login form) ------------------------------------

    [Fact]
    public async Task A_visit_can_be_logged_without_a_computer()
    {
        var created = await _phone.Register("Ana", "Reyes");
        Assert.Null(created.Visit.Pass);
        Assert.Null(created.Visit.Session);

        var log = await _staff.GetJson<List<StaffVisitDto>>("/api/visits");
        var visit = log.Single();
        Assert.Equal("Reyes", visit.LastName);
        Assert.Equal(AgeGroup.Age18To30, visit.AgeGroup);
        Assert.Equal(Sex.Male, visit.Sex);
        Assert.Equal(new[] { Sector.Academe, Sector.Youth }, visit.Sectors);
        Assert.Equal(new[] { ServiceAvailed.Connectivity, ServiceAvailed.IctEquipment }, visit.Services);
        Assert.Equal(VisitSource.Phone, visit.Source);
    }

    [Fact]
    public async Task Every_required_field_of_the_form_is_checked()
    {
        async Task<string> Problem(object form) => await (await _phone.Post("/api/visits", form)).Message();

        Assert.Equal("Please choose your age group.", await Problem(new
            { firstName = "A", lastName = "B", sex = "Male", sectors = new[] { "Youth" }, services = new[] { "Others" } }));
        Assert.Equal("Please choose your sex.", await Problem(new
            { firstName = "A", lastName = "B", ageGroup = "Age18To30", sectors = new[] { "Youth" }, services = new[] { "Others" } }));
        Assert.Equal("Please select at least one sector.", await Problem(new
            { firstName = "A", lastName = "B", ageGroup = "Age18To30", sex = "Male", sectors = Array.Empty<string>(), services = new[] { "Others" } }));
        Assert.Equal("Please select at least one purpose or service.", await Problem(new
            { firstName = "A", lastName = "B", ageGroup = "Age18To30", sex = "Male", sectors = new[] { "Youth" } }));
        Assert.Equal("Please enter your first name.", await Problem(new
            { lastName = "B", ageGroup = "Age18To30", sex = "Male", sectors = new[] { "Youth" }, services = new[] { "Others" } }));
    }

    // ---- The main flow -------------------------------------------------------

    [Fact]
    public async Task Register_pick_pc_staff_check_in_then_end_from_phone()
    {
        await _factory.Heartbeat(1);

        // Phone: register, then pick PC-01.
        var visit = await _phone.Register();
        var picked = await _phone.Pick(visit.Token, 1);
        var pass = picked.Pass!;
        Assert.Equal(PassStatus.Pending, pass.Status);
        Assert.Matches("^[0-9]{6}$", pass.Code);
        Assert.Equal(_factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(10), pass.ExpiresAt);
        Assert.Equal(ComputerStatus.Reserved, await _phone.StatusOf(1));

        // Staff: scan the QR (the visit's token) to see who it is, then check in.
        var lookup = await _staff.PostJson<StaffPassDto>("/api/passes/lookup", new { token = visit.Token });
        Assert.Equal("Dela Cruz", lookup.Visit.LastName);
        Assert.Equal(new[] { Sector.Academe, Sector.Youth }, lookup.Visit.Sectors);

        Advance(TimeSpan.FromMinutes(2));
        await _factory.Heartbeat(1);
        var session = await _staff.PostJson<SessionDto>("/api/passes/check-in", new { code = pass.Code });
        Assert.Equal("Juan Dela Cruz", session.UserName);
        Assert.Equal("Connectivity, Use of ICT equipment", session.Purpose);
        Assert.Equal(session.StartedAt.AddHours(3), session.ScheduledEndAt);
        Assert.Equal(ComputerStatus.InUse, await _phone.StatusOf(1));

        // Phone: sees the session, then ends it.
        var mine = await _phone.GetJson<VisitDto>($"/api/visits/{visit.Token}");
        Assert.Equal(PassStatus.Used, mine.Pass!.Status);
        Assert.Null(mine.Session!.EndedAt);

        var ended = await _phone.PostJson<VisitDto>($"/api/visits/{visit.Token}/session/end");
        Assert.Equal(SessionEndReason.Visitor, ended.Session!.EndReason);
        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(1));
    }

    [Fact]
    public async Task A_pass_cannot_be_used_twice()
    {
        await _factory.Heartbeat(1);
        var (token, _) = await _phone.RegisterAndPick(1);
        await _staff.PostJson<SessionDto>("/api/passes/check-in", new { token });

        var again = await _staff.Post("/api/passes/check-in", new { token });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This pass has already been used.", await again.Message());
    }

    [Fact]
    public async Task Second_visitor_cannot_take_a_reserved_pc()
    {
        await _factory.Heartbeat(1);
        await _phone.RegisterAndPick(1);

        var other = await _factory.Anonymous().Register("Maria", "Santos");
        var response = await _phone.Post($"/api/visits/{other.Token}/pass", new { computerId = 1 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("That computer was just taken. Please choose another.", await response.Message());
    }

    [Fact]
    public async Task Picking_another_pc_gives_the_first_one_back()
    {
        await _factory.Heartbeat(1, 2);
        var visit = await _phone.Register();
        await _phone.Pick(visit.Token, 1);
        var second = await _phone.Pick(visit.Token, 2);

        Assert.Equal(2, second.Pass!.ComputerId);
        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(1));
        Assert.Equal(ComputerStatus.Reserved, await _phone.StatusOf(2));
    }

    [Fact]
    public async Task Cancelled_pass_frees_the_pc()
    {
        await _factory.Heartbeat(1);
        var (token, _) = await _phone.RegisterAndPick(1);

        var cancelled = await _phone.PostJson<VisitDto>($"/api/visits/{token}/pass/cancel");
        Assert.Equal(PassStatus.Cancelled, cancelled.Pass!.Status);
        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(1));
    }

    [Fact]
    public async Task Unscanned_pass_expires_and_the_pc_can_be_picked_again()
    {
        await _factory.Heartbeat(1);
        var (token, _) = await _phone.RegisterAndPick(1);

        Advance(TimeSpan.FromMinutes(10));
        await _factory.Heartbeat(1);

        var mine = await _phone.GetJson<VisitDto>($"/api/visits/{token}");
        Assert.Equal(PassStatus.Expired, mine.Pass!.Status);

        var checkIn = await _staff.Post("/api/passes/check-in", new { token });
        Assert.Equal(HttpStatusCode.Conflict, checkIn.StatusCode);

        // The cleanup job has not run, but the PC is free again anyway.
        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(1));
        await _factory.Anonymous().RegisterAndPick(1, "Maria", "Santos");
    }

    [Fact]
    public async Task Check_in_is_refused_when_the_pc_went_offline()
    {
        await _factory.Heartbeat(1);
        var (_, code) = await _phone.RegisterAndPick(1);

        Advance(TimeSpan.FromMinutes(1)); // no heartbeat for a minute

        var checkIn = await _staff.Post("/api/passes/check-in", new { code });
        Assert.Equal(HttpStatusCode.Conflict, checkIn.StatusCode);
        Assert.Contains("offline", await checkIn.Message());
    }

    [Fact]
    public async Task One_visitor_uses_one_pc_at_a_time()
    {
        await _factory.Heartbeat(1, 2);
        var (token, code) = await _phone.RegisterAndPick(1);
        await _staff.PostJson<SessionDto>("/api/passes/check-in", new { code });

        var second = await _phone.Post($"/api/visits/{token}/pass", new { computerId = 2 });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("You are already using a computer.", await second.Message());
    }

    [Fact]
    public async Task An_old_registration_cannot_pick_a_pc()
    {
        var visit = await _phone.Register();
        Advance(TimeSpan.FromHours(12));
        await _factory.Heartbeat(1);

        var response = await _phone.Post($"/api/visits/{visit.Token}/pass", new { computerId = 1 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("register again", await response.Message());
    }

    // ---- Walk-ins at the desk ---------------------------------------------------

    [Fact]
    public async Task Walk_in_with_a_pc_starts_a_session_right_away()
    {
        await _factory.Heartbeat(2);
        var result = await _staff.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(2, "Lola", "Reyes"));

        Assert.Equal(VisitSource.Desk, result.Visit.Source);
        Assert.Equal("Lola Reyes", result.Session!.UserName);
        Assert.Equal(ComputerStatus.InUse, await _phone.StatusOf(2));
    }

    [Fact]
    public async Task Walk_in_without_a_pc_only_logs_the_visit()
    {
        var result = await _staff.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(null, "Ben", "Cruz"));
        Assert.Null(result.Session);
        Assert.Single(await _staff.GetJson<List<StaffVisitDto>>("/api/visits"));
    }

    // ---- Time limit and extensions ------------------------------------------------

    private async Task<SessionDto> WalkInSession(int pc)
    {
        await _factory.Heartbeat(pc);
        return (await _staff.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(pc))).Session!;
    }

    [Fact]
    public async Task Session_can_be_extended_only_near_the_end_and_at_most_twice()
    {
        var session = await WalkInSession(1);
        Assert.False(session.CanExtend);

        var early = await _staff.Post($"/api/sessions/{session.Id}/extend");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        Advance(TimeSpan.FromMinutes(171)); // 9 minutes left
        session = await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/extend");
        Assert.Equal(1, session.ExtensionCount);
        Assert.Equal(session.StartedAt.AddHours(4), session.ScheduledEndAt);

        Advance(TimeSpan.FromMinutes(61)); // 8 minutes left again
        session = await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/extend");
        Assert.Equal(2, session.ExtensionCount);

        Advance(TimeSpan.FromMinutes(61));
        var third = await _staff.Post($"/api/sessions/{session.Id}/extend");
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("A session can only be extended 2 times.", await third.Message());
    }

    [Fact]
    public async Task No_extension_while_someone_is_on_the_waitlist()
    {
        // Only PC-01 is online, and it is in use, so nothing is available.
        var session = await WalkInSession(1);
        Advance(TimeSpan.FromMinutes(175));
        await _factory.Heartbeat(1);

        var ana = await _phone.Register("Ana", "Reyes");
        var waiting = await _phone.PostJson<VisitDto>($"/api/visits/{ana.Token}/waitlist");
        Assert.Equal(1, waiting.Waitlist!.Position);

        var extend = await _staff.Post($"/api/sessions/{session.Id}/extend");
        Assert.Equal(HttpStatusCode.Conflict, extend.StatusCode);
        Assert.Equal("Someone is waiting for a computer.", await extend.Message());

        // Once Ana leaves the line, extending works.
        var left = await _phone.DeleteAsync($"/api/visits/{ana.Token}/waitlist");
        left.EnsureSuccessStatusCode();
        await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/extend");
    }

    [Fact]
    public async Task Cannot_join_the_waitlist_while_a_pc_is_free()
    {
        await _factory.Heartbeat(1);
        var visit = await _phone.Register();
        var response = await _phone.Post($"/api/visits/{visit.Token}/waitlist");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Picking_a_pc_takes_the_visitor_off_the_waitlist()
    {
        var session = await WalkInSession(1);
        var ana = await _phone.Register("Ana", "Reyes");
        await _phone.PostJson<VisitDto>($"/api/visits/{ana.Token}/waitlist");
        Assert.Single(await _staff.GetJson<List<WaitlistEntryDto>>("/api/waitlist"));

        await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/end");
        var picked = await _phone.Pick(ana.Token, 1);

        Assert.Null(picked.Waitlist);
        Assert.Empty(await _staff.GetJson<List<WaitlistEntryDto>>("/api/waitlist"));
    }

    [Fact]
    public async Task A_freed_pc_is_held_for_whoever_waited_first()
    {
        var session = await WalkInSession(1);
        var ana = await _phone.Register("Ana", "Reyes");
        await _phone.PostJson<VisitDto>($"/api/visits/{ana.Token}/waitlist");
        var ben = await _factory.Anonymous().Register("Ben", "Cruz");

        await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/end");
        await _factory.Heartbeat(1);

        // Ben came later and is not waiting: PC-01 is held for Ana.
        var benTries = await _phone.Post($"/api/visits/{ben.Token}/pass", new { computerId = 1 });
        Assert.Equal(HttpStatusCode.Conflict, benTries.StatusCode);
        Assert.Contains("held for someone on the waitlist", await benTries.Message());

        // Ana's phone says a computer is ready for her, for 5 minutes.
        var anaView = await _phone.GetJson<VisitDto>($"/api/visits/{ana.Token}");
        Assert.Equal(_factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(5), anaView.Waitlist!.ComputerReadyUntil);

        var picked = await _phone.Pick(ana.Token, 1);
        Assert.Equal(PassStatus.Pending, picked.Pass!.Status);
    }

    [Fact]
    public async Task Missing_your_turn_passes_the_pc_to_the_next_person()
    {
        var session = await WalkInSession(1);
        var ana = await _phone.Register("Ana", "Reyes");
        var ben = await _phone.Register("Ben", "Cruz");
        await _phone.PostJson<VisitDto>($"/api/visits/{ana.Token}/waitlist");
        await _phone.PostJson<VisitDto>($"/api/visits/{ben.Token}/waitlist");

        await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/end");
        await _factory.Heartbeat(1);

        // Ben is second: not his turn yet.
        var benEarly = await _phone.Post($"/api/visits/{ben.Token}/pass", new { computerId = 1 });
        Assert.Equal(HttpStatusCode.Conflict, benEarly.StatusCode);
        Assert.Equal(2, (await _phone.GetJson<VisitDto>($"/api/visits/{ben.Token}")).Waitlist!.Position);

        // Ana does not pick within 5 minutes.
        Advance(TimeSpan.FromMinutes(5));
        await _factory.Heartbeat(1);

        var anaView = await _phone.GetJson<VisitDto>($"/api/visits/{ana.Token}");
        Assert.Equal(WaitlistStatus.Missed, anaView.Waitlist!.Status);

        var benView = await _phone.GetJson<VisitDto>($"/api/visits/{ben.Token}");
        Assert.Equal(1, benView.Waitlist!.Position);
        Assert.NotNull(benView.Waitlist.ComputerReadyUntil);
        await _phone.Pick(ben.Token, 1);
    }

    [Fact]
    public async Task Getting_a_pc_counts_as_using_ict_equipment()
    {
        await _factory.Heartbeat(1);
        var visit = await _phone.PostJson<CreatedVisitDto>("/api/visits", new
        {
            firstName = "Ana", lastName = "Reyes", ageGroup = "Age31To59", sex = "Female",
            sectors = new[] { "Msme" }, services = new[] { "Connectivity" } // forgot to tick ICT equipment
        });
        var picked = await _phone.Pick(visit.Token, 1);
        await _staff.PostJson<SessionDto>("/api/passes/check-in", new { code = picked.Pass!.Code });

        var logged = (await _staff.GetJson<List<StaffVisitDto>>("/api/visits")).Single();
        Assert.Equal(new[] { ServiceAvailed.Connectivity, ServiceAvailed.IctEquipment }, logged.Services);
    }

    [Fact]
    public async Task Cleanup_ends_sessions_at_the_time_limit_and_expires_passes()
    {
        var session = await WalkInSession(1);
        await _factory.Heartbeat(2);
        await _phone.RegisterAndPick(2, "Maria", "Santos");

        Advance(TimeSpan.FromHours(3));
        using (var scope = _factory.Services.CreateScope())
            await CleanupService.RunOnceAsync(scope.ServiceProvider);

        var ended = await _staff.GetJson<SessionDto>($"/api/sessions/{session.Id}");
        Assert.Equal(SessionEndReason.TimeLimit, ended.EndReason);

        Assert.Single(await _staff.GetJson<List<StaffPassDto>>("/api/passes?status=Expired"));
    }

    // ---- The kiosk's staff-only form and the dashboard ----------------------------

    [Fact]
    public async Task Staff_register_a_visitor_on_the_pc_itself()
    {
        var kiosk = await _factory.KioskAsync(1);
        await _factory.Heartbeat(1);

        var session = await kiosk.PostJson<SessionDto>("/api/kiosk/sessions", ApiExtensions.Registration("Maria", "Santos"));
        Assert.Equal("Maria Santos", session.UserName);
        Assert.Equal(session.StartedAt.AddHours(3), session.ScheduledEndAt);

        var visit = (await _staff.GetJson<List<StaffVisitDto>>("/api/visits")).Single();
        Assert.Equal(VisitSource.Kiosk, visit.Source);

        // The dashboard's End session sends no body at all.
        (await _staff.PostAsync($"/api/sessions/{session.Id}/end", null)).EnsureSuccessStatusCode();
        Assert.Equal(SessionEndReason.Staff, (await _staff.GetJson<SessionDto>($"/api/sessions/{session.Id}")).EndReason);

        // "Report broken" then "Mark as fixed".
        (await _staff.PutAsJsonAsync("/api/computers/1/status", new { status = "Maintenance" }, TrackerFactory.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(ComputerStatus.Maintenance, await _phone.StatusOf(1));
        (await _staff.PutAsJsonAsync("/api/computers/1/status", new { status = "Available" }, TrackerFactory.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(ComputerStatus.Available, await _phone.StatusOf(1));

        Assert.Single(await _staff.GetJson<List<SessionDto>>("/api/computers/1/sessions"));
    }

    [Fact]
    public async Task Times_read_back_from_the_database_are_marked_utc()
    {
        await _phone.Register();
        var json = await _staff.GetStringAsync("/api/visits"); // freshly loaded from SQLite
        Assert.Matches("\"createdAt\":\"[^\"]+Z\"", json);
    }

    // ---- Rules the database enforces on its own --------------------------------

    private static Visit NewVisit(string token) => new()
    {
        TokenHash = Secrets.Hash(token), FirstName = "A", LastName = "B",
        CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    [Fact]
    public async Task Database_refuses_a_second_active_session_on_the_same_pc()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
        var now = DateTime.UtcNow;

        db.Sessions.Add(new UsageSession { ComputerId = 1, Visit = NewVisit("a"), StartedAt = now, ScheduledEndAt = now });
        db.Sessions.Add(new UsageSession { ComputerId = 1, Visit = NewVisit("b"), StartedAt = now, ScheduledEndAt = now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_refuses_two_active_sessions_for_one_visitor()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();
        var now = DateTime.UtcNow;
        var visit = NewVisit("c");

        db.Sessions.Add(new UsageSession { ComputerId = 1, Visit = visit, StartedAt = now, ScheduledEndAt = now });
        db.Sessions.Add(new UsageSession { ComputerId = 2, Visit = visit, StartedAt = now, ScheduledEndAt = now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_refuses_a_session_on_a_pc_that_does_not_exist()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackerDbContext>();

        db.Sessions.Add(new UsageSession { ComputerId = 999, Visit = NewVisit("d") });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
