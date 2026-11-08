using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KioskApp.Core;

// Which screen the kiosk should be showing right now.
public enum KioskView
{
    Loading,         // starting up, or the tracker cannot be reached yet
    NeedsSetup,      // staff must pair this PC (type a pairing code)
    Locked,          // available: show the login screen
    OutOfService,    // marked "Not working" by staff
    InUseElsewhere,  // in use, but not by a session we can see
    Reserved,        // a visitor holds a pass for this PC; staff will check them in
    InSession,       // someone is logged in: show the small session bar
    Farewell         // just logged out: "Thank you" for a few seconds
}

// Everything the windows need to draw themselves.
public sealed record KioskState(
    KioskView View,
    ComputerDto? Computer = null,
    SessionDto? Session = null,
    string? FarewellName = null,
    string? FarewellText = null,
    bool Offline = false,
    int? IdleSecondsLeft = null,
    bool KeyRejected = false); // the tracker refused this PC's key: pair it again

// The "brain" of the kiosk. It contains no WPF code, so the windows stay simple:
// they show whatever KioskState says and call the methods below on button clicks.
public sealed class KioskController
{
    private readonly KioskSettings _settings;
    private readonly ApiClient _api;
    private readonly IIdleSource _idle;
    private readonly TimeSpan _tick;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private ComputerDto? _computer;
    private SessionDto? _session;
    private bool _keyRejected;
    private bool _offline;
    private string? _farewellName;
    private string? _farewellText;
    private DateTime _farewellUntilUtc;
    private int? _idleSecondsLeft;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private DateTime _lastHelpUtc = DateTime.MinValue;

    public KioskController(KioskSettings settings, ApiClient api, IIdleSource idle, TimeSpan? tick = null)
    {
        _settings = settings;
        _api = api;
        _idle = idle;
        _tick = tick ?? TimeSpan.FromSeconds(1);
        State = Compute();
    }

    public KioskSettings Settings => _settings;
    public KioskState State { get; private set; }

    // How long the "Thank you" screen stays after a log out.
    public TimeSpan FarewellDuration { get; set; } = TimeSpan.FromSeconds(5);

    // Minimum wait between help requests from this computer (stops button spamming).
    public TimeSpan HelpCooldown { get; set; } = TimeSpan.FromSeconds(30);

    // Raised only when something the screens show has changed.
    public event Action<KioskState>? StateChanged;

    // Raised every tick (once a second), so the session timer can update.
    public event Action? Ticked;

    // Raised when something unexpected goes wrong inside the loop. The loop keeps running.
    public event Action<Exception>? ErrorOccurred;

    // The main loop: checks idle time every second and re-reads the tracker every few seconds.
    // Start it from the UI thread; the code after each await then resumes on the UI thread too.
    public async Task RunAsync(CancellationToken ct)
    {
        await RefreshAsync();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_tick, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var refreshEvery = TimeSpan.FromSeconds(Math.Max(1, _settings.RefreshSeconds));
                if (DateTime.UtcNow - _lastRefreshUtc >= refreshEvery)
                {
                    await RefreshAsync();
                }

                await CheckIdleAsync();
                Publish();
                Ticked?.Invoke();
            }
            catch (Exception ex)
            {
                // One bad tick must never stop idle logout or refreshing for good.
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    // Re-reads this computer and the active sessions from the tracker.
    public async Task RefreshAsync()
    {
        if (!_settings.IsPaired)
        {
            Publish();
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            // One call: "this PC is on" (otherwise the tracker shows it Offline),
            // answered with this PC's status and its session.
            var state = await _api.HeartbeatAsync();
            _computer = state.Computer;
            _session = state.Session;
            _keyRejected = false;
            _offline = false;
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            _keyRejected = true; // unpaired by an admin, or paired again elsewhere
            _offline = false;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            _offline = true; // keep showing the last known screen
        }
        finally
        {
            _lastRefreshUtc = DateTime.UtcNow;
            _refreshGate.Release();
        }

        Publish();
    }

    // Returns null on success, or a message to show the user.
    public async Task<string?> LoginAsync(string userName, string purpose)
    {
        userName = (userName ?? "").Trim();
        purpose = (purpose ?? "").Trim();

        if (userName.Length < 2) return "Please enter your full name.";
        if (userName.Length > 100) return "Your name is too long (100 characters at most).";
        if (purpose.Length == 0) return "Please choose what you will use the computer for.";
        if (purpose.Length > 200) return "The purpose is too long (200 characters at most).";

        try
        {
            // This is the step that records the person in the tracker.
            await _api.StartSessionAsync(userName, purpose);
            _farewellName = null;
            await RefreshAsync();
            return null;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            return Describe(ex);
        }
    }

    // The user pressed "Log out".
    public Task<string?> LogOutAsync() =>
        EndSessionAsync("Visitor", "You have been logged out. Have a great day.");

    // Sends a help request to staff. Returns null on success, or a message.
    public async Task<string?> RequestHelpAsync(string message)
    {
        message = (message ?? "").Trim();
        if (message.Length == 0) return "Please describe what you need help with.";

        if (DateTime.UtcNow - _lastHelpUtc < HelpCooldown)
            return "A help request was just sent. Please wait a moment before sending another.";

        try
        {
            await _api.CreateAssistAsync(message);
            _lastHelpUtc = DateTime.UtcNow;
            return null;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            return Describe(ex);
        }
    }

    // ---- Staff setup -------------------------------------------------------

    // The setup screen: trade the pairing code staff typed for this PC's key.
    // Returns null on success, or a message to show.
    public async Task<string?> PairAsync(string code)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0) return "Type the pairing code from the dashboard.";

        KioskPairedDto paired;
        try
        {
            paired = await _api.PairAsync(code);
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            return Describe(ex);
        }

        try
        {
            _settings.SavePairing(paired.ComputerId, paired.KioskKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Paired for this run, but it will ask again after a restart.
            _settings.ComputerId = paired.ComputerId;
            _settings.KioskKey = paired.KioskKey;
            await RefreshAsync();
            return "Paired, but the key could not be saved for the next start: " + ex.Message;
        }

        _computer = null;
        _session = null;
        _keyRejected = false;
        await RefreshAsync();
        return null;
    }

    // Staff menu: forget the pairing (the setup screen shows again).
    public void Unpair()
    {
        _computer = null;
        _session = null;
        _keyRejected = false;
        try
        {
            _settings.ClearPairing();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the setup screen still shows for this run.
            _settings.ComputerId = 0;
            _settings.KioskKey = "";
        }

        Publish();
    }

    public bool VerifyStaffPin(string pin)
    {
        // No PIN set: only allowed while testing (not in kiosk mode).
        if (string.IsNullOrEmpty(_settings.StaffPin)) return !_settings.KioskMode;
        return pin == _settings.StaffPin;
    }

    // ---- Internals ---------------------------------------------------------

    private async Task<string?> EndSessionAsync(string reason, string farewellText)
    {
        var session = _session;
        if (session is null) return null;

        try
        {
            await _api.EndSessionAsync(reason);
            ShowFarewell(session.UserName, farewellText);
            await RefreshAsync();
            return null;
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Staff already ended it from the dashboard. Just catch up.
            await RefreshAsync();
            return null;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            return Describe(ex);
        }
    }

    private void ShowFarewell(string name, string text)
    {
        _farewellName = name;
        _farewellText = text;
        _farewellUntilUtc = DateTime.UtcNow + FarewellDuration;
        _session = null;
        _idleSecondsLeft = null;
    }

    private async Task CheckIdleAsync()
    {
        if (_session is null || _computer?.Status != ComputerStatus.InUse || _settings.IdleTimeoutSeconds <= 0)
        {
            _idleSecondsLeft = null;
            return;
        }

        var remaining = TimeSpan.FromSeconds(_settings.IdleTimeoutSeconds) - _idle.GetIdleTime();

        if (remaining <= TimeSpan.Zero)
        {
            await EndSessionAsync("Idle", "You were logged out because the computer was not being used.");
            return;
        }

        var warning = TimeSpan.FromSeconds(_settings.IdleWarningSeconds);
        _idleSecondsLeft = remaining <= warning ? (int)Math.Ceiling(remaining.TotalSeconds) : null;
    }

    private KioskState Compute()
    {
        if (!_settings.IsPaired || _keyRejected)
            return new KioskState(KioskView.NeedsSetup, KeyRejected: _keyRejected);

        if (_computer is null)
            return new KioskState(KioskView.Loading, Offline: _offline);

        var showFarewell = _farewellName is not null
                           && DateTime.UtcNow < _farewellUntilUtc
                           && _computer.Status != ComputerStatus.InUse;
        if (showFarewell)
            return new KioskState(KioskView.Farewell, _computer, null, _farewellName, _farewellText, _offline);

        return _computer.Status switch
        {
            ComputerStatus.Maintenance =>
                new KioskState(KioskView.OutOfService, _computer, Offline: _offline),
            ComputerStatus.Reserved =>
                new KioskState(KioskView.Reserved, _computer, Offline: _offline),
            ComputerStatus.InUse when _session is not null =>
                new KioskState(KioskView.InSession, _computer, _session, Offline: _offline, IdleSecondsLeft: _idleSecondsLeft),
            ComputerStatus.InUse =>
                new KioskState(KioskView.InUseElsewhere, _computer, Offline: _offline),
            _ =>
                new KioskState(KioskView.Locked, _computer, Offline: _offline)
        };
    }

    private void Publish()
    {
        var next = Compute();
        var changed = !SameOnScreen(State, next);
        State = next;
        if (changed) StateChanged?.Invoke(next);
    }

    // Two states look identical on screen if these fields match.
    private static bool SameOnScreen(KioskState a, KioskState b) =>
        a.View == b.View
        && a.Offline == b.Offline
        && a.Computer?.Name == b.Computer?.Name
        && a.Computer?.Status == b.Computer?.Status
        && a.Session?.Id == b.Session?.Id
        && a.FarewellName == b.FarewellName
        && a.FarewellText == b.FarewellText
        && a.IdleSecondsLeft == b.IdleSecondsLeft
        && a.KeyRejected == b.KeyRejected;

    private static bool IsApiFailure(Exception ex) =>
        ex is ApiException or HttpRequestException or TaskCanceledException or JsonException;

    private static string Describe(Exception ex) =>
        ex is ApiException api
            ? api.Message
            : "Cannot reach the tracker. Please ask a staff member for help.";
}
