using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Starting, ending and extending sessions.
public class SessionService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly ComputerStatusService _status;

    public SessionService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options,
        ComputerStatusService status)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _status = status;
    }

    // ---- Reading -----------------------------------------------------------

    public async Task<List<SessionDto>> GetActiveAsync()
    {
        var sessions = await _db.Sessions
            .Where(s => s.EndedAt == null)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();
        return await ToDtosAsync(sessions);
    }

    // Session history, newest first. All filters are optional.
    public async Task<List<SessionDto>> GetHistoryAsync(int? computerId, DateTime? from, DateTime? to, int limit)
    {
        var query = _db.Sessions.AsQueryable();
        if (computerId is not null) query = query.Where(s => s.ComputerId == computerId);
        if (from is not null) query = query.Where(s => s.StartedAt >= from);
        if (to is not null) query = query.Where(s => s.StartedAt < to);

        var sessions = await query
            .OrderByDescending(s => s.StartedAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync();
        return await ToDtosAsync(sessions);
    }

    // The running session on one PC, or null.
    public async Task<SessionDto?> GetActiveOnComputerAsync(int computerId)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.ComputerId == computerId && s.EndedAt == null);
        return session is null ? null : (await ToDtosAsync(new[] { session }))[0];
    }

    public async Task<SessionDto> GetAsync(int id)
    {
        var session = await _db.Sessions.FindAsync(id) ?? throw TrackerException.NotFound("Session not found.");
        return (await ToDtosAsync(new[] { session }))[0];
    }

    // ---- Starting ----------------------------------------------------------

    // A session from a typed name and purpose, without a pass: the kiosk's own login
    // form and the dashboard's "Start session". (Pass check-in and walk-ins record
    // the full registration instead; see PassService.)
    public async Task<SessionDto> StartByNameAsync(StartSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Purpose))
            throw TrackerException.BadRequest("UserName and Purpose are required.");
        if (request.UserName.Trim().Length > 100)
            throw TrackerException.BadRequest("Name must be 100 characters or fewer.");
        if (request.Purpose.Trim().Length > 200)
            throw TrackerException.BadRequest("Purpose must be 200 characters or fewer.");

        // A transaction makes "check the PC is free, then start" one all-or-nothing step.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var status = await _status.GetAsync(request.ComputerId);
        if (status != ComputerStatus.Available)
            throw TrackerException.Conflict($"Computer is {Describe(status)}.");

        var session = Begin(request.ComputerId, request.UserName.Trim(), request.Purpose.Trim(), null, _clock.UtcNow());
        _db.Sessions.Add(session);
        await _db.SaveOrConflictAsync("Someone else just started a session on this computer.");
        await tx.CommitAsync();

        return await GetAsync(session.Id);
    }

    // Builds a new session (the caller adds it and saves). Used by passes too.
    public UsageSession Begin(int computerId, string userName, string purpose, Pass? pass, DateTime now) => new()
    {
        ComputerId = computerId,
        Pass = pass,
        UserName = userName,
        Purpose = purpose,
        StartedAt = now,
        ScheduledEndAt = now.AddMinutes(_options.SessionMinutes)
    };

    // ---- Ending ------------------------------------------------------------

    public async Task<SessionDto> EndAsync(int id, SessionEndReason reason)
    {
        var session = await _db.Sessions.FindAsync(id) ?? throw TrackerException.NotFound("Session not found.");
        if (session.EndedAt is not null) throw TrackerException.Conflict("Session already ended.");

        await EndTrackedAsync(session, reason, _clock.UtcNow());
        await _db.SaveChangesAsync();
        return await GetAsync(id);
    }

    // Ends every session past its scheduled end. Returns how many were ended.
    public async Task<int> EndOverdueAsync()
    {
        var now = _clock.UtcNow();
        var overdue = await _db.Sessions
            .Where(s => s.EndedAt == null && s.ScheduledEndAt <= now)
            .ToListAsync();

        foreach (var session in overdue)
            await EndTrackedAsync(session, SessionEndReason.TimeLimit, now);

        await _db.SaveChangesAsync();
        return overdue.Count;
    }

    private async Task EndTrackedAsync(UsageSession session, SessionEndReason reason, DateTime now)
    {
        session.EndedAt = now;
        session.EndReason = reason;

        // Release any peripherals that were marked in use on this computer.
        var busy = await _db.Peripherals
            .Where(p => p.ComputerId == session.ComputerId && p.IsInUse)
            .ToListAsync();
        foreach (var p in busy)
        {
            p.IsInUse = false;
            p.InUseSince = null;
        }
    }

    // ---- Extending ---------------------------------------------------------

    public async Task<SessionDto> ExtendAsync(int id)
    {
        var session = await _db.Sessions.FindAsync(id) ?? throw TrackerException.NotFound("Session not found.");
        var now = _clock.UtcNow();

        var (canExtend, reason) = CheckExtend(session, now, await CountWaitingAsync(now));
        if (!canExtend) throw TrackerException.Conflict(reason!);

        session.ScheduledEndAt = session.ScheduledEndAt.AddMinutes(_options.ExtensionMinutes);
        session.ExtensionCount++;
        await _db.SaveChangesAsync();
        return await GetAsync(id);
    }

    // The extension rules. Returns the reason in plain words when it is not allowed.
    public (bool CanExtend, string? Reason) CheckExtend(UsageSession session, DateTime now, int peopleWaiting)
    {
        if (session.EndedAt is not null)
            return (false, "This session has ended.");
        if (session.ExtensionCount >= _options.MaxExtensions)
            return (false, $"A session can only be extended {_options.MaxExtensions} times.");
        if (session.ScheduledEndAt - now > TimeSpan.FromMinutes(_options.ExtendWindowMinutes))
            return (false, $"You can extend during the last {_options.ExtendWindowMinutes} minutes of your session.");
        if (peopleWaiting > 0)
            return (false, "Someone is waiting for a computer.");
        return (true, null);
    }

    public Task<int> CountWaitingAsync(DateTime now) =>
        _db.WaitlistEntries.CountAsync(w => w.Status == WaitlistStatus.Waiting && w.ExpiresAt > now);

    // ---- Helpers -----------------------------------------------------------

    public async Task<List<SessionDto>> ToDtosAsync(IEnumerable<UsageSession> sessions)
    {
        var now = _clock.UtcNow();
        var waiting = await CountWaitingAsync(now);
        return sessions.Select(s => ToDto(s, now, waiting)).ToList();
    }

    public SessionDto ToDto(UsageSession s, DateTime now, int peopleWaiting)
    {
        var (canExtend, reason) = CheckExtend(s, now, peopleWaiting);
        return new SessionDto(s.Id, s.ComputerId, s.UserName, s.Purpose, s.StartedAt, s.EndedAt,
            s.ScheduledEndAt, s.ExtensionCount, s.EndReason, s.PassId, canExtend, reason);
    }

    public static string Describe(ComputerStatus status) => status switch
    {
        ComputerStatus.Offline => "offline",
        ComputerStatus.InUse => "in use",
        ComputerStatus.Maintenance => "under maintenance",
        ComputerStatus.Reserved => "reserved",
        _ => "available"
    };
}
