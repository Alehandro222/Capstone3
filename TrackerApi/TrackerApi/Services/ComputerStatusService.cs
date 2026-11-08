using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Works out each computer's status from the heartbeat, sessions, passes and
// problem reports. Nothing stores the status, so it is always current.
public class ComputerStatusService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;

    public ComputerStatusService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    // Status of every computer, keyed by computer id.
    public async Task<Dictionary<int, ComputerStatus>> GetAllAsync()
    {
        var now = _clock.UtcNow();

        var computers = await _db.Computers
            .Select(c => new { c.Id, c.LastHeartbeatAt })
            .ToListAsync();

        var inUse = (await _db.Sessions
            .Where(s => s.EndedAt == null)
            .Select(s => s.ComputerId)
            .ToListAsync()).ToHashSet();

        var reserved = (await _db.Passes
            .Where(p => p.Status == PassStatus.Pending && p.ExpiresAt > now)
            .Select(p => p.ComputerId)
            .ToListAsync()).ToHashSet();

        var broken = (await _db.ProblemReports
            .Where(r => r.Status == ProblemStatus.Open)
            .Select(r => r.ComputerId)
            .ToListAsync()).ToHashSet();

        return computers.ToDictionary(
            c => c.Id,
            c => Decide(c.LastHeartbeatAt, inUse.Contains(c.Id), broken.Contains(c.Id), reserved.Contains(c.Id), now));
    }

    // The staff view of one computer (id) or all of them (null): status, who is
    // using it, when their session ends, any open problem, and peripherals.
    public async Task<List<ComputerDto>> GetStaffViewAsync(int? id)
    {
        var statuses = await GetAllAsync();

        var computers = await _db.Computers
            .Where(c => id == null || c.Id == id)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.LastHeartbeatAt,
                Session = c.Sessions.Where(s => s.EndedAt == null)
                    .Select(s => new { s.Id, s.UserName, s.Purpose, s.ScheduledEndAt }).FirstOrDefault(),
                Problem = c.ProblemReports.Where(r => r.Status == ProblemStatus.Open)
                    .OrderBy(r => r.CreatedAt).Select(r => r.Description).FirstOrDefault(),
                Peripherals = c.Peripherals
                    .Select(p => new PeripheralDto(p.Id, p.Name, p.Type, p.ComputerId, p.IsInUse, p.InUseSince))
                    .ToList()
            })
            .ToListAsync();

        return computers.Select(c => new ComputerDto(
            c.Id,
            c.Name,
            statuses[c.Id],
            c.Session?.UserName,
            c.Session?.Purpose,
            c.Peripherals,
            c.LastHeartbeatAt,
            c.Session?.Id,
            c.Session?.ScheduledEndAt,
            c.Problem)).ToList();
    }

    // Status of one computer. Throws 404 if it does not exist.
    public async Task<ComputerStatus> GetAsync(int computerId)
    {
        var now = _clock.UtcNow();

        var computer = await _db.Computers
            .Where(c => c.Id == computerId)
            .Select(c => new { c.LastHeartbeatAt })
            .FirstOrDefaultAsync()
            ?? throw TrackerException.NotFound("Computer not found.");

        var inUse = await _db.Sessions.AnyAsync(s => s.ComputerId == computerId && s.EndedAt == null);
        var broken = await _db.ProblemReports.AnyAsync(r => r.ComputerId == computerId && r.Status == ProblemStatus.Open);
        var reserved = await _db.Passes.AnyAsync(p =>
            p.ComputerId == computerId && p.Status == PassStatus.Pending && p.ExpiresAt > now);

        return Decide(computer.LastHeartbeatAt, inUse, broken, reserved, now);
    }

    // The rule itself, in priority order (see the ComputerStatus enum).
    public ComputerStatus Decide(DateTime? lastHeartbeatAt, bool inUse, bool openProblem, bool reserved, DateTime now)
    {
        if (IsOffline(lastHeartbeatAt, now)) return ComputerStatus.Offline;
        if (inUse) return ComputerStatus.InUse;
        if (openProblem) return ComputerStatus.Maintenance;
        if (reserved) return ComputerStatus.Reserved;
        return ComputerStatus.Available;
    }

    public bool IsOffline(DateTime? lastHeartbeatAt, DateTime now)
    {
        if (!_options.RequireHeartbeat) return false;
        if (lastHeartbeatAt is null) return true;
        return now - lastHeartbeatAt.Value > TimeSpan.FromSeconds(_options.OfflineAfterSeconds);
    }
}

public static class ClockExtensions
{
    // Every timestamp comes from the server's clock, in UTC.
    public static DateTime UtcNow(this TimeProvider clock) => clock.GetUtcNow().UtcDateTime;
}
