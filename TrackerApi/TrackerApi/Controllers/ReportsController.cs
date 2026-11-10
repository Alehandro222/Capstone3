using Microsoft.AspNetCore.Mvc;
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

    public ReportsController(ReportService reports, AuditService audit)
    {
        _reports = reports;
        _audit = audit;
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
