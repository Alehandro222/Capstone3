using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The line of visitors waiting for a PC when none is free.
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

    public async Task<WaitlistJoinedDto> JoinAsync(JoinWaitlistRequest request)
    {
        var firstName = request.FirstName?.Trim();
        if (firstName?.Length > 100) throw TrackerException.BadRequest("Name must be 100 characters or fewer.");

        if (await CountAvailableAsync() > 0)
            throw TrackerException.Conflict("A computer is free right now. Please pick one instead.");

        var now = _clock.UtcNow();
        var token = Secrets.NewToken();
        var entry = new WaitlistEntry
        {
            TokenHash = Secrets.Hash(token),
            FirstName = string.IsNullOrEmpty(firstName) ? null : firstName,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.WaitlistLifetimeMinutes)
        };
        _db.WaitlistEntries.Add(entry);
        await _db.SaveChangesAsync();

        return new WaitlistJoinedDto(token, await ToStatusDtoAsync(entry, now));
    }

    public async Task<WaitlistStatusDto> GetAsync(string token)
    {
        var entry = await FindAsync(token);
        return await ToStatusDtoAsync(entry, _clock.UtcNow());
    }

    public async Task<WaitlistStatusDto> LeaveAsync(string token)
    {
        var entry = await FindAsync(token);
        var now = _clock.UtcNow();
        if (IsWaiting(entry, now))
        {
            entry.Status = WaitlistStatus.Left;
            entry.EndedAt = now;
            await _db.SaveChangesAsync();
        }
        return await ToStatusDtoAsync(entry, now);
    }

    // Staff view: everyone still waiting, first in line first.
    public async Task<List<WaitlistEntryDto>> GetWaitingAsync()
    {
        var now = _clock.UtcNow();
        var waiting = await _db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Waiting && w.ExpiresAt > now)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id)
            .ToListAsync();

        return waiting
            .Select((w, i) => new WaitlistEntryDto(w.Id, w.FirstName, w.Status, i + 1, w.CreatedAt, w.ExpiresAt))
            .ToList();
    }

    // Called when someone from the waitlist creates a pass. Does not save:
    // the caller saves it together with the new pass.
    public async Task MarkServedAsync(string token, DateTime now)
    {
        var entry = await _db.WaitlistEntries.FirstOrDefaultAsync(w => w.TokenHash == Secrets.Hash(token));
        if (entry is not null && IsWaiting(entry, now))
        {
            entry.Status = WaitlistStatus.Served;
            entry.EndedAt = now;
        }
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

    private async Task<WaitlistEntry> FindAsync(string token) =>
        await _db.WaitlistEntries.FirstOrDefaultAsync(w => w.TokenHash == Secrets.Hash(token))
        ?? throw TrackerException.NotFound("Waitlist entry not found.");

    private static bool IsWaiting(WaitlistEntry entry, DateTime now) =>
        entry.Status == WaitlistStatus.Waiting && entry.ExpiresAt > now;

    private async Task<int> CountAvailableAsync() =>
        (await _status.GetAllAsync()).Values.Count(s => s == ComputerStatus.Available);

    private async Task<WaitlistStatusDto> ToStatusDtoAsync(WaitlistEntry entry, DateTime now)
    {
        var waiting = IsWaiting(entry, now);
        // The cleanup job may not have run yet, so an old entry can still say "Waiting".
        var status = entry.Status == WaitlistStatus.Waiting && !waiting ? WaitlistStatus.Expired : entry.Status;

        int? position = null;
        if (waiting)
        {
            var ahead = await _db.WaitlistEntries.CountAsync(w =>
                w.Status == WaitlistStatus.Waiting && w.ExpiresAt > now &&
                (w.CreatedAt < entry.CreatedAt || (w.CreatedAt == entry.CreatedAt && w.Id < entry.Id)));
            position = ahead + 1;
        }

        return new WaitlistStatusDto(status, position, await CountAvailableAsync(), entry.ExpiresAt);
    }
}
