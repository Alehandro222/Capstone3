namespace TrackerApi.Services;

// The "Tracker" section of appsettings.json. Every time limit lives here,
// so the library can change them without touching the code.
public class TrackerOptions
{
    public const string Section = "Tracker";

    // How long a registration stays usable on the visitor's phone (pick a PC,
    // join the waitlist, end the session). After that they register again.
    public int VisitLifetimeHours { get; set; } = 12;

    // How long a visitor has to get their pass scanned at the desk.
    public int PassLifetimeMinutes { get; set; } = 10;

    // Length of a session.
    public int SessionMinutes { get; set; } = 180;

    // Each extension adds this much time...
    public int ExtensionMinutes { get; set; } = 60;
    // ...up to this many times per session...
    public int MaxExtensions { get; set; } = 2;
    // ...and only during the last N minutes (when the "time is almost up" warning shows).
    public int ExtendWindowMinutes { get; set; } = 10;

    // A PC that has not sent a heartbeat for this long is Offline.
    public int OfflineAfterSeconds { get; set; } = 30;

    // false = never show PCs as Offline. Handy while developing without
    // the kiosk app running on each PC. Must be true in the library.
    public bool RequireHeartbeat { get; set; } = true;

    // Someone on the waitlist is dropped after this long.
    public int WaitlistLifetimeMinutes { get; set; } = 240;

    // When a PC frees up it is held this long for the first person waiting.
    public int WaitlistOfferMinutes { get; set; } = 5;

    // How often the cleanup job expires passes and ends overdue sessions. 0 = off.
    public int CleanupIntervalSeconds { get; set; } = 15;
}
