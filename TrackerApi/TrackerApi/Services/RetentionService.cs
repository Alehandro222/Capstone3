using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Data retention: DTC keeps visit records for 1 year (Tracker:KeepVisitsDays). The cleanup
// job calls this; it deletes every visit older than that, together with everything that
// belongs to it: its PC sessions, passes, waitlist places, and sector/service answers.
// Reports for those dates then come out empty. Each run is recorded in the audit log
// (counts only); older audit entries keep only record numbers, never names.
public class RetentionService
{
    private const int BatchSize = 500; // a large backlog is cleared over a few runs

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

    // Returns how many visits were deleted.
    public async Task<int> DeleteOldVisitsAsync()
    {
        if (_options.KeepVisitsDays <= 0) return 0;

        var cutoff = _clock.UtcNow().AddDays(-_options.KeepVisitsDays);
        var ids = await _db.Visits
            .Where(v => v.CreatedAt < cutoff)
            .Where(v => !v.Sessions.Any(s => s.EndedAt == null)) // never pull a running session out from under a PC
            .OrderBy(v => v.CreatedAt)
            .Select(v => v.Id)
            .Take(BatchSize)
            .ToListAsync();
        if (ids.Count == 0) return 0;

        // Children first (the database refuses to leave them without their visit). All or nothing.
        await using var tx = await _db.Database.BeginTransactionAsync();
        var sessions = await _db.Sessions.Where(s => ids.Contains(s.VisitId)).ExecuteDeleteAsync();
        await _db.Passes.Where(p => ids.Contains(p.VisitId)).ExecuteDeleteAsync();
        await _db.WaitlistEntries.Where(w => ids.Contains(w.VisitId)).ExecuteDeleteAsync();
        await _db.VisitSectors.Where(s => ids.Contains(s.VisitId)).ExecuteDeleteAsync();
        await _db.VisitServices.Where(s => ids.Contains(s.VisitId)).ExecuteDeleteAsync();
        await _db.Visits.Where(v => ids.Contains(v.Id)).ExecuteDeleteAsync();
        await tx.CommitAsync();

        await _audit.RecordAsAsync(ActorType.System, null, "Data retention", "retention.visits_deleted", "Visit", null,
            $"{ids.Count} visits and {sessions} PC sessions from before {cutoff:yyyy-MM-dd} (kept {_options.KeepVisitsDays} days)");
        return ids.Count;
    }
}
