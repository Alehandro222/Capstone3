using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Passes: the visitor's phone uses the routes with {token}; staff use the rest.
// The token is the long secret inside the QR code. Only the phone that got it
// can see or change its own pass.
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

    // ---- Visitor (phone) ----

    // POST api/passes -> reserve a PC and get a pass
    // body: { "computerId": 1, "firstName": "Juan", "lastName": "Dela Cruz", "age": 20,
    //         "sex": "Male", "purpose": "Schoolwork", "sectors": ["Youth", "Academe"] }
    [AllowAnonymous]
    [HttpPost]
    public Task<CreatedPassDto> Create(CreatePassRequest request) => _passes.CreateAsync(request);

    // GET api/passes/{token} -> the phone checks this every few seconds
    [AllowAnonymous]
    [HttpGet("{token}")]
    public Task<VisitorPassDto> Get(string token) => _passes.GetForVisitorAsync(token);

    // POST api/passes/{token}/cancel -> "Choose a different PC"
    [AllowAnonymous]
    [HttpPost("{token}/cancel")]
    public Task<VisitorPassDto> Cancel(string token) => _passes.CancelAsync(token);

    // POST api/passes/{token}/end -> "End session" on the phone
    [AllowAnonymous]
    [HttpPost("{token}/end")]
    public Task<VisitorPassDto> End(string token) => _passes.EndSessionAsync(token);

    // POST api/passes/{token}/extend -> "Extend" on the phone
    [AllowAnonymous]
    [HttpPost("{token}/extend")]
    public Task<VisitorPassDto> Extend(string token) => _passes.ExtendSessionAsync(token);

    // ---- Staff (a staff login is needed: the default for every endpoint) ----

    // GET api/passes?status=Pending -> recent passes, newest first
    [HttpGet]
    public Task<List<StaffPassDto>> List([FromQuery] PassStatus? status, [FromQuery] int limit = 100) =>
        _passes.ListAsync(status, limit);

    // POST api/passes/lookup   body: { "token": "..." } or { "code": "123456" }
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

    // POST api/passes/walk-in -> visitor without a phone; staff fill in the form
    [HttpPost("walk-in")]
    public async Task<SessionDto> WalkIn(WalkInRequest request)
    {
        var session = await _passes.WalkInAsync(request);
        await _audit.RecordAsync("pass.walk_in", "Session", session.Id, $"computer {session.ComputerId}, pass {session.PassId}");
        return session;
    }
}
