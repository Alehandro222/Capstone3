using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Starting, ending and extending sessions. Every session belongs to a visit,
// so its name and purpose always come from the registration form.
public class SessionService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;

    public SessionService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    // Sessions with the visit details their DTO needs.
    private IQueryable<UsageSession> WithVisit() =>
        _db.Sessions.Include(s => s.Visit!).ThenInclude(v => v.Services);

    // ---- Reading -----------------------------------------------------------

    public async Task<List<SessionDto>> GetActiveAsync()
    {
        var sessions = await WithVisit()
            .Where(s => s.EndedAt == null)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();
        return await ToDtosAsync(sessions);
    }

    // Session history, newest first. All filters are optional.
    public async Task<List<SessionDto>> GetHistoryAsync(int? computerId, DateTime? from, DateTime? to, int limit)
    {
        var query = WithVisit();
        if (computerId is not null) query = query.Where(s => s.ComputerId == computerId);
        if (from is not null) query = query.Where(s => s.StartedAt >= from);
        if (to is not null) query = query.Where(s => s.StartedAt < to);

        var sessions = await query
            .OrderByDescending(s => s.StartedAt)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync();
        return await ToDtosAsync(sessions);
    }

    public async Task<SessionDto> GetAsync(int id)
    {
        var session = await FindAsync(id);
        return (await ToDtosAsync(new[] { session }))[0];
    }

    // The running session on one PC, or null.
    public async Task<SessionDto?> GetActiveOnComputerAsync(int computerId)
    {
        var session = await WithVisit().FirstOrDefaultAsync(s => s.ComputerId == computerId && s.EndedAt == null);
        return session is null ? null : (await ToDtosAsync(new[] { session }))[0];
    }

    // ---- Starting ----------------------------------------------------------

    // Builds a new session (the caller adds it and saves, inside its transaction).
    // The visit's services must be loaded.
    public UsageSession Begin(int computerId, Visit visit, Pass? pass, DateTime now)
    {
        // Using a PC is "Use of ICT Equipment", even if the visitor did not tick it,
        // so the reports never show a PC session without it.
        if (visit.Services.All(s => s.Service != ServiceAvailed.IctEquipment))
            visit.Services.Add(new VisitServiceAvailed { Service = ServiceAvailed.IctEquipment });

        return new UsageSession
        {
            ComputerId = computerId,
            Visit = visit,
            Pass = pass,
            StartedAt = now,
            ScheduledEndAt = now.AddMinutes(_options.SessionMinutes)
        };
    }

    // ---- Ending ------------------------------------------------------------

    public async Task<SessionDto> EndAsync(int id, SessionEndReason reason)
    {
        var session = await FindAsync(id);
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

        // Recorded as ending at its time limit, not "now": if the server was off for a while,
        // the session still only lasted until its limit (this keeps reports' PC hours right).
        foreach (var session in overdue)
            await EndTrackedAsync(session, SessionEndReason.TimeLimit, session.ScheduledEndAt);

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
        var session = await FindAsync(id);
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

    private async Task<UsageSession> FindAsync(int id) =>
        await WithVisit().FirstOrDefaultAsync(s => s.Id == id) ?? throw TrackerException.NotFound("Session not found.");

    public async Task<List<SessionDto>> ToDtosAsync(IEnumerable<UsageSession> sessions)
    {
        var now = _clock.UtcNow();
        var waiting = await CountWaitingAsync(now);
        return sessions.Select(s => ToDto(s, now, waiting)).ToList();
    }

    // The session must have been loaded with its visit and services (see WithVisit).
    public SessionDto ToDto(UsageSession s, DateTime now, int peopleWaiting)
    {
        var (canExtend, reason) = CheckExtend(s, now, peopleWaiting);
        return new SessionDto(s.Id, s.ComputerId, s.Visit!.FullName, Registration.ServicesText(s.Visit.Services),
            s.StartedAt, s.EndedAt, s.ScheduledEndAt, s.ExtensionCount, s.EndReason, s.PassId, canExtend, reason,
            s.VisitId);
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
