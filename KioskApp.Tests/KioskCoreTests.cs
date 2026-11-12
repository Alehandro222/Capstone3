using System.Net;
using System.Text;
using System.Text.Json;
using KioskApp.Core;

namespace KioskApp.Tests;

// Tests for the kiosk's logic (Core/), with the tracker faked: no WPF windows, no network.
public class KioskCoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kiosk-tests-" + Guid.NewGuid().ToString("N"));

    public KioskCoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- Staff PIN -----------------------------------------------------------

    [Fact]
    public void Pin_is_stored_as_a_salted_hash()
    {
        var a = PinHasher.Hash("482193");
        var b = PinHasher.Hash("482193");

        Assert.DoesNotContain("482193", a);
        Assert.NotEqual(a, b); // different salt each time
        Assert.True(PinHasher.Verify("482193", a));
        Assert.False(PinHasher.Verify("4822", a));
        Assert.False(PinHasher.Verify("482193", "482193"));   // a plain value is never accepted as a hash
        Assert.False(PinHasher.Verify("482193", "pbkdf2-sha256$x$y$z"));
        Assert.False(PinHasher.Verify("482193", ""));
    }

    [Fact]
    public void A_plain_pin_in_the_settings_file_is_replaced_by_its_hash()
    {
        File.WriteAllText(Path.Combine(_dir, "kiosk.settings.json"),
            """{ "apiUrl": "http://server:5080/", "kioskMode": true, "staffPin": "482193", "refreshSeconds": 7 }""");

        var settings = KioskSettings.Load(_dir, Path.Combine(_dir, "data"));

        Assert.True(settings.HasStaffPin);
        Assert.True(PinHasher.Verify("482193", settings.StaffPinHash));
        var file = File.ReadAllText(Path.Combine(_dir, "kiosk.settings.json"));
        Assert.DoesNotContain("482193", file);
        Assert.Contains("staffPinHash", file);
        Assert.Contains("http://server:5080/", file); // the other settings are kept
        Assert.Equal(7, KioskSettings.Load(_dir, Path.Combine(_dir, "data")).RefreshSeconds);
    }

    [Fact]
    public void Five_wrong_pins_lock_the_pin_screen_and_staff_can_change_the_pin()
    {
        File.WriteAllText(Path.Combine(_dir, "kiosk.settings.json"), """{ "kioskMode": true, "staffPin": "482193" }""");
        var controller = NewController(KioskSettings.Load(_dir, Path.Combine(_dir, "data")), new FakeTracker());

        Assert.Null(controller.CheckStaffPin("482193"));
        for (var i = 0; i < 4; i++) Assert.Equal("Incorrect PIN.", controller.CheckStaffPin("0000"));
        Assert.Contains("locked", controller.CheckStaffPin("0000"));
        Assert.Contains("Too many", controller.CheckStaffPin("482193")); // even the right PIN, for a minute

        Assert.NotNull(controller.ChangeStaffPin("1234", "1234"));       // too short (6+)
        Assert.NotNull(controller.ChangeStaffPin("739104", "739140"));   // typo
        Assert.Null(controller.ChangeStaffPin("739104", "739104"));
        Assert.True(PinHasher.Verify("739104", KioskSettings.Load(_dir, Path.Combine(_dir, "data")).StaffPinHash));
    }

    [Fact]
    public void A_short_pin_in_the_file_still_works_but_staff_are_asked_to_change_it()
    {
        File.WriteAllText(Path.Combine(_dir, "kiosk.settings.json"), """{ "kioskMode": true, "staffPin": "4821" }""");
        var settings = KioskSettings.Load(_dir, Path.Combine(_dir, "data"));
        Assert.True(PinHasher.Verify("4821", settings.StaffPinHash));
        Assert.Contains("shorter than 6", settings.SaveWarning);
    }

    [Fact]
    public async Task Countdowns_use_the_trackers_clock_not_this_pcs()
    {
        var tracker = new FakeTracker { ServerTime = DateTimeOffset.UtcNow.AddMinutes(7) }; // this PC is 7 minutes slow
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();

        var gap = controller.NowUtc - DateTime.UtcNow;
        Assert.InRange(gap.TotalMinutes, 6.9, 7.1);

        tracker.ServerTime = DateTimeOffset.UtcNow.AddSeconds(1); // within rounding: ignored
        await controller.RefreshAsync();
        Assert.InRange((controller.NowUtc - DateTime.UtcNow).TotalSeconds, -0.5, 0.5);
    }

    [Fact]
    public void Kiosk_mode_without_a_pin_keeps_the_staff_menu_closed()
    {
        File.WriteAllText(Path.Combine(_dir, "kiosk.settings.json"), """{ "kioskMode": true }""");
        var controller = NewController(KioskSettings.Load(_dir, Path.Combine(_dir, "data")), new FakeTracker());
        Assert.NotNull(controller.CheckStaffPin(""));
    }

    // ---- Lock-screen QR ------------------------------------------------------

    [Fact]
    public void Qr_address_opens_the_phone_app_with_this_pc_chosen()
    {
        var settings = new KioskSettings { VisitorUrl = "http://192.168.1.10:5173/#/visit" };
        Assert.Equal("http://192.168.1.10:5173/#/visit?pc=4", settings.VisitorUrlFor(4));
        Assert.Equal("http://x/#/visit?lang=en&pc=4", new KioskSettings { VisitorUrl = "http://x/#/visit?lang=en" }.VisitorUrlFor(4));
        Assert.Null(new KioskSettings().VisitorUrlFor(4)); // not set: no QR
    }

    // ---- Time left -----------------------------------------------------------

    [Fact]
    public void Time_left_warns_in_the_last_minutes()
    {
        var now = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
        var session = Session(endsAt: now.AddMinutes(9).AddSeconds(30));

        var left = SessionClock.Remaining(session, now);
        Assert.Equal(TimeSpan.FromSeconds(570), left);
        Assert.Equal(TimePhase.Warning, SessionClock.Phase(left, 10));
        Assert.Equal(TimePhase.Normal, SessionClock.Phase(TimeSpan.FromMinutes(11), 10));
        Assert.Equal(TimePhase.Final, SessionClock.Phase(TimeSpan.FromSeconds(40), 10));
        Assert.Equal(TimeSpan.Zero, SessionClock.Remaining(session, now.AddHours(1)));
        Assert.Equal("9:30", SessionClock.Format(left));
        Assert.Equal("2:59:59", SessionClock.Format(TimeSpan.FromSeconds(3 * 3600 - 1)));
        Assert.Equal("10 minutes", SessionClock.Words(left));
    }

    // ---- What the screens show ----------------------------------------------

    [Fact]
    public async Task Reserved_screen_gets_the_pass_expiry()
    {
        var until = DateTime.UtcNow.AddMinutes(8);
        var tracker = new FakeTracker { Next = State("Reserved", reservedUntil: until) };
        var controller = NewController(PairedSettings(), tracker);

        await controller.RefreshAsync();

        Assert.Equal(KioskView.Reserved, controller.State.View);
        Assert.Equal(until, controller.State.ReservedUntil);
    }

    [Fact]
    public async Task Session_ended_by_the_time_limit_says_goodbye()
    {
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddSeconds(-5))) };
        var controller = NewController(PairedSettings(), tracker);

        await controller.RefreshAsync();
        Assert.Equal(KioskView.InSession, controller.State.View);

        tracker.Next = State("Available"); // the cleanup job ended it on the server
        await controller.RefreshAsync();

        Assert.Equal(KioskView.Farewell, controller.State.View);
        Assert.Equal("Maria Santos", controller.State.FarewellName);
        Assert.Contains("time is up", controller.State.FarewellText);
    }

    [Fact]
    public async Task Session_ended_by_staff_says_goodbye_too()
    {
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddHours(2))) };
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();

        tracker.Next = State("Available");
        await controller.RefreshAsync();

        Assert.Equal(KioskView.Farewell, controller.State.View);
        Assert.Contains("was ended", controller.State.FarewellText);
    }

    [Fact]
    public async Task Extend_asks_the_tracker_and_shows_the_new_end_time()
    {
        var end = DateTime.UtcNow.AddMinutes(5);
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: end, canExtend: true)) };
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();

        tracker.Next = State("InUse", Session(endsAt: end.AddHours(1), extensions: 1));
        Assert.Null(await controller.ExtendAsync());

        Assert.Contains("api/kiosk/session/extend", tracker.Calls);
        Assert.Equal(end.AddHours(1), controller.State.Session!.ScheduledEndAtUtc);
        Assert.Equal(1, controller.State.Session.ExtensionCount);
    }

    [Fact]
    public async Task Extend_refused_by_the_tracker_shows_its_reason()
    {
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddMinutes(5))) };
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();

        tracker.Fail = (HttpStatusCode.Conflict, "Someone is waiting for a computer, so sessions can't be extended.");
        Assert.Equal("Someone is waiting for a computer, so sessions can't be extended.", await controller.ExtendAsync());
    }

    // ---- When the tracker can't be reached --------------------------------------

    [Fact]
    public async Task Idle_logout_locks_the_pc_even_when_the_tracker_is_down_and_tells_it_later()
    {
        var idle = new SetIdle();
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddHours(2))) };
        var settings = PairedSettings();
        settings.IdleTimeoutSeconds = 1200;
        var controller = NewController(settings, tracker, idle);
        await controller.RefreshAsync();

        tracker.Unreachable = true;
        idle.Idle = TimeSpan.FromMinutes(21);
        await controller.CheckTimersAsync();

        Assert.Equal(KioskView.Farewell, controller.State.View);

        // Still down, and the tracker's last word was "in use": the screen stays locked.
        await controller.RefreshAsync();
        Assert.NotEqual(KioskView.InSession, controller.State.View);

        // Back: the kiosk tells the tracker why it ended, and stays locked.
        tracker.Unreachable = false;
        tracker.Calls.Clear();
        tracker.AfterEnd = State("Available");
        await controller.RefreshAsync();
        Assert.Contains("api/kiosk/session/end", tracker.Calls);
        Assert.Contains("\"reason\":\"Idle\"", tracker.LastEndBody);
        Assert.NotEqual(KioskView.InSession, controller.State.View);
    }

    [Fact]
    public async Task Log_out_works_without_the_tracker()
    {
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddHours(2))) };
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();

        tracker.Unreachable = true;
        Assert.Null(await controller.LogOutAsync()); // no error: the PC locks anyway
        Assert.Equal(KioskView.Farewell, controller.State.View);
    }

    [Fact]
    public async Task Out_of_time_locks_even_if_the_tracker_never_ends_the_session()
    {
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddSeconds(-31))) };
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();
        Assert.Equal(KioskView.InSession, controller.State.View);

        tracker.Unreachable = true;
        await controller.CheckTimersAsync();
        Assert.Equal(KioskView.Farewell, controller.State.View);
        Assert.Contains("time is up", controller.State.FarewellText);
        Assert.DoesNotContain("api/kiosk/session/end", tracker.Calls); // the tracker ends it by itself
    }

    [Fact]
    public async Task A_session_extended_elsewhere_during_an_outage_comes_back()
    {
        var tracker = new FakeTracker { Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddSeconds(-31))) };
        var controller = NewController(PairedSettings(), tracker);
        await controller.RefreshAsync();
        tracker.Unreachable = true;
        await controller.CheckTimersAsync(); // locked: out of time, as far as this PC knew

        tracker.Unreachable = false;
        tracker.Next = State("InUse", Session(endsAt: DateTime.UtcNow.AddMinutes(50), extensions: 1)); // extended on the phone
        await controller.RefreshAsync();
        Assert.NotNull(controller.State.Session);
    }

    // ---- Helpers -------------------------------------------------------------

    private KioskSettings PairedSettings()
    {
        var settings = KioskSettings.Load(_dir, Path.Combine(_dir, "data"));
        settings.ComputerId = 1;
        settings.KioskKey = "test-key";
        return settings;
    }

    private static KioskController NewController(KioskSettings settings, FakeTracker tracker, IIdleSource? idle = null) =>
        new(settings, new ApiClient("http://tracker.test/", () => settings.KioskKey, () => settings.ComputerId, tracker),
            idle ?? new NoIdle());

    private static SessionDto Session(DateTime endsAt, bool canExtend = false, int extensions = 0) =>
        new(7, 1, "Maria Santos", "Use of ICT Equipment", endsAt.AddHours(-3), null, endsAt, extensions, canExtend,
            canExtend ? null : "You can extend only in the last 10 minutes.");

    private static object State(string status, SessionDto? session = null, DateTime? reservedUntil = null) => new
    {
        computer = new { id = 1, name = "PC-01", status, peripherals = Array.Empty<object>() },
        session,
        reservedUntil
    };

    private sealed class NoIdle : IIdleSource
    {
        public TimeSpan GetIdleTime() => TimeSpan.Zero;
    }

    private sealed class SetIdle : IIdleSource
    {
        public TimeSpan Idle { get; set; }
        public TimeSpan GetIdleTime() => Idle;
    }

    // Stands in for TrackerApi: every call answers with Next (or Fail).
    private sealed class FakeTracker : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public object Next { get; set; } = State("Available");
        public (HttpStatusCode Status, string Message)? Fail { get; set; }
        public List<string> Calls { get; } = new();
        public DateTimeOffset? ServerTime { get; set; } // sent as the Date header
        public bool Unreachable { get; set; }          // the network is down
        public object? AfterEnd { get; set; }          // what heartbeats return once a session was ended
        public string LastEndBody { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (Unreachable) throw new HttpRequestException("No route to host");
            Calls.Add(path);
            if (path == "api/kiosk/session/end")
            {
                LastEndBody = request.Content!.ReadAsStringAsync(ct).Result;
                if (AfterEnd is not null) { Next = AfterEnd; AfterEnd = null; }
                return Task.FromResult(Respond(HttpStatusCode.OK, new { id = 7, computerId = 1, userName = "Maria Santos", purpose = "x", startedAt = DateTime.UtcNow, endedAt = DateTime.UtcNow }));
            }

            if (Fail is { } fail && path != "api/kiosk/heartbeat")
                return Task.FromResult(Respond(fail.Status, new { message = fail.Message }));

            // Extend answers with the session; everything else here is a heartbeat.
            object body = path == "api/kiosk/session/extend"
                ? JsonSerializer.SerializeToElement(Next, Json).GetProperty("session")
                : Next;
            return Task.FromResult(Respond(HttpStatusCode.OK, body));
        }

        private HttpResponseMessage Respond(HttpStatusCode status, object body)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json")
            };
            response.Headers.Date = ServerTime ?? DateTimeOffset.UtcNow;
            return response;
        }
    }
}
