using System.Net;
using System.Net.Http.Json;
using TrackerApi.Models;

namespace TrackerApi.Tests;

public class AuthTests : IDisposable
{
    private readonly TrackerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // ---- Who may call what -----------------------------------------------------

    [Fact]
    public async Task Staff_endpoints_need_a_sign_in_but_phone_endpoints_do_not()
    {
        var phone = _factory.Anonymous();

        var dashboard = await phone.GetAsync("/api/computers");
        Assert.Equal(HttpStatusCode.Unauthorized, dashboard.StatusCode);
        Assert.Equal("Please sign in.", await dashboard.Message());

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/passes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.Post("/api/passes/check-in", new { code = "123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/sessions/active")).StatusCode);

        // The phone's own routes stay open.
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/public/computers")).StatusCode);
        await _factory.Kiosk().Heartbeat(1);
        var pass = await phone.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync($"/api/passes/{pass.Token}")).StatusCode);
    }

    [Fact]
    public async Task Kiosk_key_opens_only_the_kiosk_routes()
    {
        var kiosk = _factory.Kiosk();
        Assert.Equal(HttpStatusCode.OK, (await kiosk.GetAsync("/api/computers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await kiosk.GetAsync("/api/sessions/active")).StatusCode);
        await kiosk.Heartbeat(1);

        // Not for the kiosk: staff-only data.
        Assert.False((await kiosk.GetAsync("/api/passes")).IsSuccessStatusCode);
        Assert.False((await kiosk.GetAsync("/api/audit")).IsSuccessStatusCode);

        var wrongKey = _factory.Kiosk(key: "guess");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongKey.GetAsync("/api/computers")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_manage_staff_and_read_the_audit_log()
    {
        var staff = await _factory.SignedInAsync("maria", StaffRole.Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/computers")).StatusCode);

        var admin = await _factory.SignedInAsync("boss", StaffRole.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit")).StatusCode);
    }

    // ---- Passwords -------------------------------------------------------------

    [Fact]
    public async Task Sign_in_with_password_then_sign_out()
    {
        await _factory.CreateStaffAsync("maria");
        var browser = _factory.Anonymous();

        var wrong = await browser.Post("/api/auth/login", new { username = "maria", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Wrong username or password.", await wrong.Message());

        // Usernames are not case-sensitive.
        var me = await browser.PostJson<StaffDto>("/api/auth/login", new { username = "Maria", password = TrackerFactory.Password });
        Assert.Equal("maria", me.Username);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);

        (await browser.Post("/api/auth/logout")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_until_an_admin_unlocks_it()
    {
        var maria = await _factory.CreateStaffAsync("maria");
        var browser = _factory.Anonymous();

        for (var i = 0; i < 5; i++)
            await browser.Post("/api/auth/login", new { username = "maria", password = "wrong" });

        var locked = await browser.Post("/api/auth/login", new { username = "maria", password = TrackerFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("Too many wrong passwords", await locked.Message());

        var admin = await _factory.SignedInAsync();
        (await admin.Post($"/api/staff/{maria.Id}/unlock")).EnsureSuccessStatusCode();

        var ok = await browser.Post("/api/auth/login", new { username = "maria", password = TrackerFactory.Password });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Lockout_ends_by_itself_after_15_minutes()
    {
        await _factory.CreateStaffAsync("maria");
        var browser = _factory.Anonymous();
        for (var i = 0; i < 5; i++)
            await browser.Post("/api/auth/login", new { username = "maria", password = "wrong" });

        _factory.Clock.Advance(TimeSpan.FromMinutes(15));
        var ok = await browser.Post("/api/auth/login", new { username = "maria", password = TrackerFactory.Password });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Password_change_needs_the_current_password()
    {
        var maria = await _factory.SignedInAsync("maria", StaffRole.Staff);

        var bad = await maria.Post("/api/auth/password", new { currentPassword = "wrong", newPassword = "a-new-password" });
        Assert.Equal("Your current password is wrong.", await bad.Message());

        var tooShort = await maria.Post("/api/auth/password", new { currentPassword = TrackerFactory.Password, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        (await maria.Post("/api/auth/password",
            new { currentPassword = TrackerFactory.Password, newPassword = "a-new-password" })).EnsureSuccessStatusCode();
        var login = await _factory.Anonymous().Post("/api/auth/login", new { username = "maria", password = "a-new-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // ---- Admin rules -------------------------------------------------------------

    [Fact]
    public async Task Deactivated_staff_are_signed_out_straight_away()
    {
        var admin = await _factory.SignedInAsync();
        var maria = await _factory.SignedInAsync("maria", StaffRole.Staff);
        var mariaId = (await maria.GetJson<StaffDto>("/api/auth/me")).Id;

        (await admin.PutAsJsonAsync($"/api/staff/{mariaId}", new { isActive = false })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await maria.GetAsync("/api/computers")).StatusCode);
    }

    [Fact]
    public async Task Promotion_takes_effect_on_the_next_sign_in()
    {
        var admin = await _factory.SignedInAsync();
        var maria = await _factory.SignedInAsync("maria", StaffRole.Staff);
        var mariaId = (await maria.GetJson<StaffDto>("/api/auth/me")).Id;

        (await admin.PutAsJsonAsync($"/api/staff/{mariaId}", new { role = "Admin" }, TrackerFactory.Json))
            .EnsureSuccessStatusCode();

        // Her old cookie says "Staff", so it is no longer accepted.
        Assert.Equal(HttpStatusCode.Unauthorized, (await maria.GetAsync("/api/computers")).StatusCode);
    }

    [Fact]
    public async Task The_last_admin_cannot_be_removed()
    {
        var admin = await _factory.SignedInAsync();
        var me = await admin.GetJson<StaffDto>("/api/auth/me");

        var response = await admin.PutAsJsonAsync($"/api/staff/{me.Id}", new { isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("There must always be at least one active admin.", await response.Message());
    }

    [Fact]
    public async Task Admin_creates_staff_and_usernames_are_unique()
    {
        var admin = await _factory.SignedInAsync();
        var created = await admin.PostJson<StaffDto>("/api/staff",
            new { username = "juan", fullName = "Juan Cruz", role = "Staff", password = "long-enough-pw" });
        Assert.Equal("juan", created.Username);
        var login = await _factory.Anonymous().Post("/api/auth/login", new { username = "juan", password = "long-enough-pw" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Every account needs a password.
        var noPassword = await admin.Post("/api/staff", new { username = "ana", fullName = "Ana Reyes", role = "Staff" });
        Assert.Equal(HttpStatusCode.BadRequest, noPassword.StatusCode);

        var again = await admin.Post("/api/staff",
            new { username = "JUAN", fullName = "Other", role = "Staff", password = "long-enough-pw" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    // ---- Audit log ---------------------------------------------------------------

    [Fact]
    public async Task Audit_log_records_who_did_what()
    {
        var admin = await _factory.SignedInAsync();
        var kiosk = _factory.Kiosk(computerId: 1);
        await kiosk.Heartbeat(1);

        var pass = await _factory.Anonymous().PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));
        var session = await admin.PostJson<SessionDto>("/api/passes/check-in", new { code = pass.Pass.Code });
        await kiosk.PostJson<SessionDto>($"/api/sessions/{session.Id}/end", new { reason = "Idle" });
        await _factory.Anonymous().Post("/api/auth/login", new { username = "admin", password = "wrong" });

        var log = await admin.GetJson<List<AuditEntryDto>>("/api/audit");

        var checkIn = log.Single(a => a.Action == "pass.check_in");
        Assert.Equal(ActorType.Staff, checkIn.ActorType);
        Assert.Equal("admin Full Name", checkIn.ActorName);
        Assert.Equal(session.Id.ToString(), checkIn.TargetId);

        var end = log.Single(a => a.Action == "session.end");
        Assert.Equal(ActorType.Kiosk, end.ActorType);
        Assert.Equal("Kiosk app (computer 1)", end.ActorName);
        Assert.Contains("Idle", end.Details);

        Assert.Single(log, a => a.Action == "auth.login_failed");
        Assert.Single(log, a => a.Action == "auth.login");
    }
}
