using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProblemsController : ControllerBase
{
    private readonly ProblemService _problems;

    public ProblemsController(ProblemService problems)
    {
        _problems = problems;
    }

    // GET api/problems?status=Open&computerId=3
    [HttpGet]
    public Task<List<ProblemReportDto>> List([FromQuery] ProblemStatus? status, [FromQuery] int? computerId) =>
        _problems.ListAsync(status, computerId);

    // POST api/problems   body: { "computerId": 3, "description": "No sound from headset jack" }
    // The PC shows as Maintenance until the report is resolved.
    [HttpPost]
    public Task<ProblemReportDto> Open(CreateProblemRequest request) => _problems.OpenAsync(request);

    // PUT api/problems/1/resolve   body: { "notes": "Replaced the cable" }
    [HttpPut("{id:int}/resolve")]
    public Task<ProblemReportDto> Resolve(int id, ResolveProblemRequest request) =>
        _problems.ResolveAsync(id, request.Notes);
}
