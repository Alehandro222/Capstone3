using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProblemsController : ControllerBase
{
    private readonly ProblemService _problems;
    private readonly AuditService _audit;

    public ProblemsController(ProblemService problems, AuditService audit)
    {
        _problems = problems;
        _audit = audit;
    }

    // GET api/problems?status=Open&computerId=3
    [HttpGet]
    public Task<List<ProblemReportDto>> List([FromQuery] ProblemStatus? status, [FromQuery] int? computerId) =>
        _problems.ListAsync(status, computerId);

    // POST api/problems   body: { "computerId": 3, "description": "No sound from headset jack" }
    // The PC shows as Maintenance until the report is resolved.
    [HttpPost]
    public async Task<ProblemReportDto> Open(CreateProblemRequest request)
    {
        var report = await _problems.OpenAsync(request);
        await _audit.RecordAsync("problem.open", "ProblemReport", report.Id, $"{report.ComputerName}: {report.Description}");
        return report;
    }

    // PUT api/problems/1/resolve   body: { "notes": "Replaced the cable" }
    [HttpPut("{id:int}/resolve")]
    public async Task<ProblemReportDto> Resolve(int id, ResolveProblemRequest request)
    {
        var report = await _problems.ResolveAsync(id, request.Notes);
        await _audit.RecordAsync("problem.resolve", "ProblemReport", id, $"{report.ComputerName}: {report.ResolutionNotes}");
        return report;
    }
}
