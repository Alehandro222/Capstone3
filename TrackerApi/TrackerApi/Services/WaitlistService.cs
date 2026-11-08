using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The line of registered visitors waiting for a PC when none is free.
public class WaitlistService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly ComputerStatusService _status;

    public WaitlistService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options,
        ComputerStatusService status)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _status = status;
    }

    public async Task JoinAsync(Visit visit)
    {
        var now = _clock.UtcNow();
        if (await CurrentEntryAsync(visit, now) is not null) return; // already waiting

        // An entry that timed out but was not cleaned up yet must not block joining again.
        foreach (var stale in await _db.WaitlistEntries
                     .Where(w => w.VisitId == visit.Id && w.Status == WaitlistStatus.Waiting && w.ExpiresAt <= now)
                     .ToListAsync())
        {
            stale.Status = WaitlistStatus.Expired;
            stale.EndedAt = stale.ExpiresAt;
        }

        if (await _db.Sessions.AnyAsync(s => s.VisitId == visit.Id && s.EndedAt == null))
            throw TrackerException.Conflict("You are already using a computer.");
        if (await _db.Passes.AnyAsync(p => p.VisitId == visit.Id && p.Status == PassStatus.Pending && p.ExpiresAt > now))
            throw TrackerException.Conflict("You already have a PC reserved.");
        if (await CountAvailableAsync() > 0)
            throw TrackerException.Conflict("A computer is free right now. Please pick one instead.");

        _db.WaitlistEntries.Add(new WaitlistEntry
        {
            VisitId = visit.Id,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.WaitlistLifetimeMinutes)
        });
        await _db.SaveOrConflictAsync("You are already on the waitlist.");
    }

    public async Task LeaveAsync(Visit visit)
    {
        var now = _clock.UtcNow();
        var entry = await CurrentEntryAsync(visit, now);
        if (entry is null) return;

        entry.Status = WaitlistStatus.Left;
        entry.EndedAt = now;
        await _db.SaveChangesAsync();
    }

    // Called when the visitor picks a PC. Does not save: the caller saves it
    // together with the new pass.
    public async Task MarkServedAsync(Visit visit, DateTime now)
    {
        var entry = await CurrentEntryAsync(visit, now);
        if (entry is null) return;
        entry.Status = WaitlistStatus.Served;
        entry.EndedAt = now;
    }

    // The visitor's place in line, or null when they are not waiting.
    public async Task<WaitlistStatusDto?> StatusForAsync(Visit visit)
    {
        var now = _clock.UtcNow();
        var entry = await CurrentEntryAsync(visit, now);
        if (entry is null) return null;

        var ahead = await _db.WaitlistEntries.CountAsync(w =>
            w.Status == WaitlistStatus.Waiting && w.ExpiresAt > now &&
            (w.CreatedAt < entry.CreatedAt || (w.CreatedAt == entry.CreatedAt && w.Id < entry.Id)));

        return new WaitlistStatusDto(entry.Status, ahead + 1, await CountAvailableAsync(), entry.ExpiresAt);
    }

    // Staff view: everyone still waiting, first in line first.
    public async Task<List<WaitlistEntryDto>> GetWaitingAsync()
    {
        var now = _clock.UtcNow();
        var waiting = await _db.WaitlistEntries.Include(w => w.Visit)
            .Where(w => w.Status == WaitlistStatus.Waiting && w.ExpiresAt > now)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .ToListAsync();

        return waiting
            .Select((w, i) => new WaitlistEntryDto(w.Id, w.VisitId, w.Visit!.FirstName, w.Status, i + 1,
                w.CreatedAt, w.ExpiresAt))
            .ToList();
    }

    public async Task<int> ExpireOldAsync()
    {
        var now = _clock.UtcNow();
        var old = await _db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Waiting && w.ExpiresAt <= now)
            .ToListAsync();
        foreach (var entry in old)
        {
            entry.Status = WaitlistStatus.Expired;
            entry.EndedAt = entry.ExpiresAt;
        }
        await _db.SaveChangesAsync();
        return old.Count;
    }

    private Task<WaitlistEntry?> CurrentEntryAsync(Visit visit, DateTime now) =>
        _db.WaitlistEntries.FirstOrDefaultAsync(w =>
            w.VisitId == visit.Id && w.Status == WaitlistStatus.Waiting && w.ExpiresAt > now);

    private async Task<int> CountAvailableAsync() =>
        (await _status.GetAllAsync()).Values.Count(s => s == ComputerStatus.Available);
}
