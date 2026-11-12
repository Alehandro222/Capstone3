using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Problem reports: opening one puts a PC into Maintenance, resolving it puts it back.
public class ProblemService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;

    public ProblemService(TrackerDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<List<ProblemReportDto>> ListAsync(ProblemStatus? status, int? computerId)
    {
        var query = _db.ProblemReports.Include(r => r.Computer).AsQueryable();
        if (status is not null) query = query.Where(r => r.Status == status);
        if (computerId is not null) query = query.Where(r => r.ComputerId == computerId);

        var reports = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return reports.Select(ToDto).ToList();
    }

    public async Task<ProblemReportDto> OpenAsync(CreateProblemRequest request)
    {
        var description = request.Description?.Trim();
        if (string.IsNullOrEmpty(description)) throw TrackerException.BadRequest("Please describe the problem.");
        if (description.Length > 500) throw TrackerException.BadRequest("Description must be 500 characters or fewer.");

        var computer = await _db.Computers.FindAsync(request.ComputerId)
                       ?? throw TrackerException.NotFound("Computer not found.");

        var report = new ProblemReport
        {
            ComputerId = computer.Id,
            Computer = computer,
            Description = description,
            CreatedAt = _clock.UtcNow()
        };
        _db.ProblemReports.Add(report);
        await _db.SaveChangesAsync();
        return ToDto(report);
    }

    public async Task<ProblemReportDto> ResolveAsync(int id, string? notes)
    {
        var report = await _db.ProblemReports.Include(r => r.Computer).FirstOrDefaultAsync(r => r.Id == id)
                     ?? throw TrackerException.NotFound("Problem report not found.");
        if (report.Status == ProblemStatus.Resolved) throw TrackerException.Conflict("Already resolved.");

        notes = notes?.Trim();
        if (notes?.Length > 500) throw TrackerException.BadRequest("Notes must be 500 characters or fewer.");

        report.Status = ProblemStatus.Resolved;
        report.ResolvedAt = _clock.UtcNow();
        report.ResolutionNotes = string.IsNullOrEmpty(notes) ? null : notes;
        await _db.SaveChangesAsync();
        return ToDto(report);
    }

    private static ProblemReportDto ToDto(ProblemReport r) => new(
        r.Id, r.ComputerId, r.Computer?.Name ?? string.Empty, r.Description, r.Status,
        r.CreatedAt, r.ResolvedAt, r.ResolutionNotes);
}
