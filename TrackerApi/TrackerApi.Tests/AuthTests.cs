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
        await _factory.Heartbeat(1);
        var pass = await phone.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync($"/api/passes/{pass.Token}")).StatusCode);
    }

    [Fact]
    public async Task Kiosk_key_only_opens_the_kiosk_routes_for_its_own_pc()
    {
        var pc1 = await _factory.KioskAsync(1);
        var state = await pc1.PostJson<KioskStateDto>("/api/kiosk/heartbeat");
        Assert.Equal(1, state.Computer.Id);
        Assert.Null(state.Session);

        // None of the staff data: other PCs, sessions, passes, the audit log.
        Assert.Equal(HttpStatusCode.Unauthorized, (await pc1.GetAsync("/api/computers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pc1.GetAsync("/api/sessions/active")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pc1.GetAsync("/api/passes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pc1.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task A_kiosk_cannot_see_or_end_another_pcs_session()
    {
        var pc1 = await _factory.KioskAsync(1);
        var pc2 = await _factory.KioskAsync(2);
        await _factory.Heartbeat(2);
        await pc2.PostJson<SessionDto>("/api/kiosk/sessions", new { userName = "Maria", purpose = "Printing" });

        // PC-01 sees only itself, so Maria's name never reaches it...
        var state = await pc1.PostJson<KioskStateDto>("/api/kiosk/heartbeat");
        Assert.Null(state.Session);
        Assert.Null(state.Computer.CurrentUser);

        // ...and "end session" on PC-01 only ever means PC-01's session.
        var end = await pc1.Post("/api/kiosk/session/end");
        Assert.Equal(HttpStatusCode.Conflict, end.StatusCode);
        Assert.NotNull((await pc2.PostJson<KioskStateDto>("/api/kiosk/heartbeat")).Session);
    }

    [Fact]
    public async Task A_key_copied_from_one_pc_does_not_work_as_another_pc()
    {
        var code = await _factory.NewPairingCodeAsync(1);
        var paired = await _factory.Anonymous().PostJson<KioskPairedDto>("/api/kiosk/pair", new { code });

        var pretending = _factory.KioskWithKey(2, paired.KioskKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pretending.Post("/api/kiosk/heartbeat")).StatusCode);

        var guessing = _factory.KioskWithKey(1, "guess");
        var response = await guessing.Post("/api/kiosk/heartbeat");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("not paired", await response.Message());
    }

    [Fact]
    public async Task Pairing_codes_work_once_and_expire_after_15_minutes()
    {
        var code = await _factory.NewPairingCodeAsync(1);
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", code);

        // Typed in lower case without the dash still works.
        var first = await _factory.Anonymous().Post("/api/kiosk/pair", new { code = code.Replace("-", "").ToLower() });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var again = await _factory.Anonymous().Post("/api/kiosk/pair", new { code });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        var late = await _factory.NewPairingCodeAsync(2);
        _factory.Clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _factory.Anonymous().Post("/api/kiosk/pair", new { code = late })).StatusCode);
    }

    [Fact]
    public async Task Pairing_again_or_unpairing_cancels_the_old_key()
    {
        var oldKiosk = await _factory.KioskAsync(1);
        var code = await _factory.NewPairingCodeAsync(1);
        var paired = await _factory.Anonymous().PostJson<KioskPairedDto>("/api/kiosk/pair", new { code });

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldKiosk.Post("/api/kiosk/heartbeat")).StatusCode);

        var newKiosk = _factory.KioskWithKey(1, paired.KioskKey);
        Assert.Equal(HttpStatusCode.OK, (await newKiosk.Post("/api/kiosk/heartbeat")).StatusCode);

        var admin = await _factory.SignedInAsync();
        (await admin.Post("/api/computers/1/unpair")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await newKiosk.Post("/api/kiosk/heartbeat")).StatusCode);
    }

    [Fact]
    public async Task Only_admins_create_pairing_codes()
    {
        var staff = await _factory.SignedInAsync("maria", StaffRole.Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Post("/api/computers/1/pairing-code")).StatusCode);

        var admin = await _factory.SignedInAsync("boss", StaffRole.Admin);
        var code = await admin.PostJson<PairingCodeDto>("/api/computers/1/pairing-code");
        Assert.Equal("PC-01", code.ComputerName);
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
        var kiosk = await _factory.KioskAsync(1);
        await _factory.Heartbeat(1);

        var pass = await _factory.Anonymous().PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(1));
        var session = await admin.PostJson<SessionDto>("/api/passes/check-in", new { code = pass.Pass.Code });
        await kiosk.PostJson<SessionDto>("/api/kiosk/session/end", new { reason = "Idle" });
        await _factory.Anonymous().Post("/api/auth/login", new { username = "admin", password = "wrong" });

        var log = await admin.GetJson<List<AuditEntryDto>>("/api/audit");

        var checkIn = log.Single(a => a.Action == "pass.check_in");
        Assert.Equal(ActorType.Staff, checkIn.ActorType);
        Assert.Equal("admin Full Name", checkIn.ActorName);
        Assert.Equal(session.Id.ToString(), checkIn.TargetId);

        var end = log.Single(a => a.Action == "session.end");
        Assert.Equal(ActorType.Kiosk, end.ActorType);
        Assert.Equal("Kiosk PC-01", end.ActorName);
        Assert.Contains("Idle", end.Details);

        Assert.Single(log, a => a.Action == "auth.login_failed");
        Assert.Single(log, a => a.Action == "auth.login");
        Assert.Single(log, a => a.Action == "kiosk.paired");
    }
}
