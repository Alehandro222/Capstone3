using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Staff side of PC passes. (Visitors pick a PC through /api/visits/{token}/pass.)
[ApiController]
[Route("api/[controller]")]
public class PassesController : ControllerBase
{
    private readonly PassService _passes;
    private readonly AuditService _audit;

    public PassesController(PassService passes, AuditService audit)
    {
        _passes = passes;
        _audit = audit;
    }

    // GET api/passes?status=Pending -> recent passes, newest first
    [HttpGet]
    public Task<List<StaffPassDto>> List([FromQuery] PassStatus? status, [FromQuery] int limit = 100) =>
        _passes.ListAsync(status, limit);

    // POST api/passes/lookup   body: { "token": "..." } (the QR code) or { "code": "123456" }
    // Shows who the pass belongs to before checking them in. (POST, so the
    // secret token never ends up in a URL or server log.)
    [HttpPost("lookup")]
    public async Task<StaffPassDto> Lookup(CheckInRequest request)
    {
        var pass = await _passes.LookupAsync(request.Token, request.Code);
        // Viewing a visitor's personal details is logged too (Data Privacy Act).
        await _audit.RecordAsync("pass.lookup", "Pass", pass.Id, $"{pass.ComputerName}, {pass.Status}");
        return pass;
    }

    // POST api/passes/check-in   body: { "token": "..." } or { "code": "123456" }
    // The scan at the help desk: validates the pass and starts the session.
    [HttpPost("check-in")]
    public async Task<SessionDto> CheckIn(CheckInRequest request)
    {
        var session = await _passes.CheckInAsync(request);
        var method = string.IsNullOrWhiteSpace(request.Token) ? "6-digit code" : "QR";
        await _audit.RecordAsync("pass.check_in", "Session", session.Id,
            $"computer {session.ComputerId}, pass {session.PassId}, by {method}");
        return session;
    }
}
