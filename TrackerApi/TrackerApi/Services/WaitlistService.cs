using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The line of registered visitors waiting for a PC when none is free.
//
// First come, first served: free PCs go to the people waiting, in order.
// If 2 PCs are free and 3 people wait, the first 2 each get an "offer": a few
// minutes to pick a PC on their phone. Nobody else can take those PCs meanwhile.
// Someone who lets the offer run out is "Missed" and the next person gets it.
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
        await RefreshAsync();
        if (await CurrentEntryAsync(visit, now) is not null) return; // already waiting

        if (await _db.Sessions.AnyAsync(s => s.VisitId == visit.Id && s.EndedAt == null))
            throw TrackerException.Conflict("You are already using a computer.");
        if (await _db.Passes.AnyAsync(p => p.VisitId == visit.Id && p.Status == PassStatus.Pending && p.ExpiresAt > now))
            throw TrackerException.Conflict("You already have a PC reserved.");

        // Only join when every free PC is already held for someone waiting.
        if (await CountAvailableAsync() > (await WaitingInOrderAsync(now)).Count)
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
        await RefreshAsync(); // their held PC goes to the next person
    }

    // May this visit take a free PC now? Throws 409 when every free PC is held
    // for people ahead of them in line. Call inside the reservation's transaction.
    public async Task EnsureMayPickAsync(Visit visit)
    {
        var now = _clock.UtcNow();
        await RefreshAsync();

        var waiting = await WaitingInOrderAsync(now);
        var available = await CountAvailableAsync();
        var place = waiting.FindIndex(w => w.VisitId == visit.Id);

        var allowed = place >= 0
            ? place < available            // their turn: a PC is held for them
            : waiting.Count < available;   // not waiting: only PCs nobody is waiting for
        if (!allowed)
            throw TrackerException.Conflict(place >= 0
                ? "Please wait for your turn. Your phone will tell you when a computer is ready for you."
                : "That computer is being held for someone on the waitlist. Join the waitlist to get the next one.");
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

    // The visitor's place in line (or a "Missed" notice), or null when not waiting.
    public async Task<WaitlistStatusDto?> StatusForAsync(Visit visit)
    {
        var now = _clock.UtcNow();
        await RefreshAsync();

        var entry = await CurrentEntryAsync(visit, now);
        if (entry is not null)
        {
            var place = (await WaitingInOrderAsync(now)).FindIndex(w => w.Id == entry.Id);
            return new WaitlistStatusDto(entry.Status, place + 1, await CountAvailableAsync(), entry.ExpiresAt,
                entry.OfferExpiresAt);
        }

        // Tell the phone they missed their turn (until they join again or pick a PC).
        var last = await _db.WaitlistEntries
            .Where(w => w.VisitId == visit.Id)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync();
        return last?.Status == WaitlistStatus.Missed
            ? new WaitlistStatusDto(last.Status, null, await CountAvailableAsync(), last.ExpiresAt, last.OfferExpiresAt)
            : null;
    }

    // Staff view: everyone still waiting, first in line first.
    public async Task<List<WaitlistEntryDto>> GetWaitingAsync()
    {
        var now = _clock.UtcNow();
        await RefreshAsync();
        var waiting = await WaitingInOrderAsync(now);
        return waiting
            .Select((w, i) => new WaitlistEntryDto(w.Id, w.VisitId, w.Visit!.FirstName, w.Status, i + 1,
                w.CreatedAt, w.ExpiresAt, w.OfferExpiresAt))
            .ToList();
    }

    // Keeps the line up to date. Runs from the cleanup job, and before anything
    // that reads or changes the line, so it is never out of date:
    //  1. offers that ran out -> Missed;
    //  2. people who waited too long (and hold no offer) -> Expired;
    //  3. the first N people get an offer, where N = free PCs.
    // Returns how many people left the line.
    public async Task<int> RefreshAsync()
    {
        var now = _clock.UtcNow();
        var waiting = await _db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Waiting)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .ToListAsync();

        var removed = 0;
        foreach (var entry in waiting)
        {
            if (entry.OfferExpiresAt is not null && entry.OfferExpiresAt <= now)
            {
                entry.Status = WaitlistStatus.Missed;
                entry.EndedAt = entry.OfferExpiresAt;
                removed++;
            }
            else if (entry.OfferExpiresAt is null && entry.ExpiresAt <= now)
            {
                entry.Status = WaitlistStatus.Expired;
                entry.EndedAt = entry.ExpiresAt;
                removed++;
            }
        }

        var stillWaiting = waiting.Where(w => w.Status == WaitlistStatus.Waiting).ToList();
        var available = await CountAvailableAsync();
        for (var i = 0; i < stillWaiting.Count; i++)
        {
            if (i < available)
                stillWaiting[i].OfferExpiresAt ??= now.AddMinutes(_options.WaitlistOfferMinutes);
            else
                stillWaiting[i].OfferExpiresAt = null; // a PC was taken by staff meanwhile
        }

        await _db.SaveChangesAsync();
        return removed;
    }

    private Task<List<WaitlistEntry>> WaitingInOrderAsync(DateTime now) =>
        _db.WaitlistEntries.Include(w => w.Visit)
            .Where(w => w.Status == WaitlistStatus.Waiting && (w.ExpiresAt > now || w.OfferExpiresAt > now))
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .ToListAsync();

    private Task<WaitlistEntry?> CurrentEntryAsync(Visit visit, DateTime now) =>
        _db.WaitlistEntries.FirstOrDefaultAsync(w =>
            w.VisitId == visit.Id && w.Status == WaitlistStatus.Waiting && (w.ExpiresAt > now || w.OfferExpiresAt > now));

    private async Task<int> CountAvailableAsync() =>
        (await _status.GetAllAsync()).Values.Count(s => s == ComputerStatus.Available);
}
