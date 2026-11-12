using System;

namespace KioskApp.Core;

public enum TimePhase
{
    Normal,  // plenty of time
    Warning, // the last few minutes (default 10): save your work, extend if allowed
    Final    // the last minute
}

// Works out what the session box shows about the time left. No screen code, so it is easy to test.
public static class SessionClock
{
    public static TimeSpan Remaining(SessionDto session, DateTime nowUtc)
    {
        if (session.ScheduledEndAt == default) return TimeSpan.Zero;
        var left = session.ScheduledEndAtUtc - nowUtc;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public static TimePhase Phase(TimeSpan remaining, int warningMinutes)
    {
        if (remaining <= TimeSpan.FromMinutes(1)) return TimePhase.Final;
        if (remaining <= TimeSpan.FromMinutes(Math.Max(1, warningMinutes))) return TimePhase.Warning;
        return TimePhase.Normal;
    }

    // 1:05:09, or 4:07 under an hour.
    public static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    // "about 9 minutes" style text for the warning banner.
    public static string Words(TimeSpan t)
    {
        var minutes = (int)Math.Ceiling(t.TotalMinutes);
        return minutes <= 1 ? "less than a minute" : $"{minutes} minutes";
    }
}
