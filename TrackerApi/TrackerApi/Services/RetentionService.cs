using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Data retention: DTC keeps visitors' names for 1 year. The cleanup job calls this; it
// blanks the first and last name of every visit older than that. Everything else about
// the visit (age group, sex, sectors, services, its PC sessions) stays, with no name,
// so reports still count it. Each run is recorded in the audit log.
public class RetentionService
{
    private const int BatchSize = 500; // a long-closed backlog is cleared over a few runs

    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly AuditService _audit;

    public RetentionService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options, AuditService audit)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _audit = audit;
    }

    // Returns how many visits had their names removed.
    public async Task<int> RemoveOldNamesAsync()
    {
        if (_options.KeepVisitorNamesDays <= 0) return 0;

        var now = _clock.UtcNow();
        var cutoff = now.AddDays(-_options.KeepVisitorNamesDays);
        var old = await _db.Visits
            .Where(v => v.NamesRemovedAt == null && v.CreatedAt < cutoff)
            .OrderBy(v => v.CreatedAt)
            .Take(BatchSize)
            .ToListAsync();
        if (old.Count == 0) return 0;

        foreach (var visit in old)
        {
            visit.FirstName = string.Empty;
            visit.LastName = string.Empty;
            visit.NamesRemovedAt = now;
        }
        await _db.SaveChangesAsync();

        await _audit.RecordAsAsync(ActorType.System, null, "Data retention", "retention.names_removed", "Visit", null,
            $"{old.Count} visits from before {cutoff:yyyy-MM-dd} (kept {_options.KeepVisitorNamesDays} days)");
        return old.Count;
    }
}
