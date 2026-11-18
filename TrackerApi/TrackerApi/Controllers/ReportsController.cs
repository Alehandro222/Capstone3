using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Reports for DTC. Staff see the counts; only admins download lists with names
// (Data Privacy Act: least access), and every download is in the audit log.
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly AuditService _audit;
    private readonly ReportAssistant _assistant;
    private readonly ReportRunner _runner;
    private readonly ReportRunQueue _queue;
    private readonly TrackerDbContext _db;

    public ReportsController(ReportService reports, AuditService audit, ReportAssistant assistant,
        ReportRunner runner, ReportRunQueue queue, TrackerDbContext db)
    {
        _reports = reports;
        _audit = audit;
        _assistant = assistant;
        _runner = runner;
        _queue = queue;
        _db = db;
    }

    // GET api/reports/assistant -> is the Gemini report assistant set up? (admin)
    [HttpGet("assistant")]
    [Authorize(Policy = Policies.Admin)]
    public AssistantStatusDto AssistantStatus() => new(_assistant.IsConfigured, _assistant.Model);

    // POST api/reports/runs   body: { "kind": "Month", "date": "2026-11-15" }   (admin)
    // Starts a day / week / month report as a background job and returns at once; the page checks
    // on it with GET api/reports/runs/{id}. `date` is any day inside the period (default: today).
    // Only counts are ever sent to Gemini (ReportAssistant.Facts).
    [HttpPost("runs")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ReportRunDto> StartRun(ReportRunRequest request)
    {
        var run = await _runner.CreateAsync(request.Kind, request.Date);
        await _audit.RecordAsync("report.assistant", "ReportRun", run.Id,
            $"{request.Kind} {run.From:yyyy-MM-dd} to {run.To:yyyy-MM-dd}, {(request.Kind == ReportKind.Day ? "no Gemini" : _assistant.Model + " (counts only)")}");
        _queue.Enqueue(run.Id);
        return ReportRunner.ToDto(run);
    }

    // GET api/reports/runs -> the saved reports, newest first (admin)
    [HttpGet("runs")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<List<ReportRunListItemDto>> Runs() =>
        await _db.ReportRuns.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(50)
            .Select(r => new ReportRunListItemDto(r.Id, r.Kind, r.From, r.To, r.Status, r.Note, r.CreatedAt, r.FinishedAt))
            .ToListAsync();

    // GET api/reports/runs/{id} -> progress, and the finished report (admin)
    [HttpGet("runs/{id:int}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ReportRunDto> Run(int id) =>
        ReportRunner.ToDto(await _db.ReportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id)
            ?? throw TrackerException.NotFound("There is no such report."));

    // DELETE api/reports/runs/{id} (admin)
    [HttpDelete("runs/{id:int}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> DeleteRun(int id)
    {
        var run = await _db.ReportRuns.FindAsync(id) ?? throw TrackerException.NotFound("There is no such report.");
        _db.ReportRuns.Remove(run);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // GET api/reports/summary?from=2026-11-01&to=2026-11-30
    // Days are in the center's time zone, both included. Without dates: today.
    [HttpGet("summary")]
    public Task<ReportDto> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var (start, end) = _reports.Range(from, to);
        return _reports.SummaryAsync(start, end);
    }

    // GET api/reports/export?from=&to=&format=xlsx            -> workbook: summary and days
    // GET api/reports/export?from=&to=&format=xlsx&names=true -> also the visits and sessions lists (admin)
    // GET api/reports/export?from=&to=&format=csv&table=summary|days|visits|sessions (visits/sessions: admin)
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string format = "xlsx", [FromQuery] string? table = null, [FromQuery] bool names = false)
    {
        var (start, end) = _reports.Range(from, to);
        format = format.Trim().ToLowerInvariant();
        table = table?.Trim().ToLowerInvariant();

        var withNames = format == "csv" ? table is not null && ReportService.HasNames(table) : names;
        if (withNames && !User.IsInRole(Roles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Only admins can download lists with visitors' names." });

        var period = start == end ? $"{start:yyyy-MM-dd}" : $"{start:yyyy-MM-dd}_to_{end:yyyy-MM-dd}";
        byte[] file;
        string contentType, fileName;
        switch (format)
        {
            case "xlsx":
                file = await _reports.ExcelAsync(start, end, withNames);
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                fileName = $"misa-report_{period}.xlsx";
                break;
            case "csv":
                if (table is null || !ReportService.Tables.Contains(table))
                    throw TrackerException.BadRequest($"Choose a table: {string.Join(", ", ReportService.Tables)}.");
                file = await _reports.CsvAsync(start, end, table);
                contentType = "text/csv; charset=utf-8";
                fileName = $"misa-{table}_{period}.csv";
                break;
            default:
                throw TrackerException.BadRequest("The format must be xlsx or csv.");
        }

        await _audit.RecordAsync("report.export", null, null,
            $"{format}{(table is null ? "" : " " + table)}, {start:yyyy-MM-dd} to {end:yyyy-MM-dd}{(withNames ? ", with names" : "")}");
        return File(file, contentType, fileName);
    }
}
