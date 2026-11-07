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

    public PassesController(PassService passes)
    {
        _passes = passes;
    }

    // ---- Visitor (phone) ----

    // POST api/passes -> reserve a PC and get a pass
    // body: { "computerId": 1, "firstName": "Juan", "lastName": "Dela Cruz", "age": 20,
    //         "sex": "Male", "purpose": "Schoolwork", "sectors": ["Youth", "Academe"] }
    [HttpPost]
    public Task<CreatedPassDto> Create(CreatePassRequest request) => _passes.CreateAsync(request);

    // GET api/passes/{token} -> the phone checks this every few seconds
    [HttpGet("{token}")]
    public Task<VisitorPassDto> Get(string token) => _passes.GetForVisitorAsync(token);

    // POST api/passes/{token}/cancel -> "Choose a different PC"
    [HttpPost("{token}/cancel")]
    public Task<VisitorPassDto> Cancel(string token) => _passes.CancelAsync(token);

    // POST api/passes/{token}/end -> "End session" on the phone
    [HttpPost("{token}/end")]
    public Task<VisitorPassDto> End(string token) => _passes.EndSessionAsync(token);

    // POST api/passes/{token}/extend -> "Extend" on the phone
    [HttpPost("{token}/extend")]
    public Task<VisitorPassDto> Extend(string token) => _passes.ExtendSessionAsync(token);

    // ---- Staff ----

    // GET api/passes?status=Pending -> recent passes, newest first
    [HttpGet]
    public Task<List<StaffPassDto>> List([FromQuery] PassStatus? status, [FromQuery] int limit = 100) =>
        _passes.ListAsync(status, limit);

    // POST api/passes/lookup   body: { "token": "..." } or { "code": "123456" }
    // Shows who the pass belongs to before checking them in. (POST, so the
    // secret token never ends up in a URL or server log.)
    [HttpPost("lookup")]
    public Task<StaffPassDto> Lookup(CheckInRequest request) => _passes.LookupAsync(request.Token, request.Code);

    // POST api/passes/check-in   body: { "token": "..." } or { "code": "123456" }
    // The scan at the help desk: validates the pass and starts the session.
    [HttpPost("check-in")]
    public Task<SessionDto> CheckIn(CheckInRequest request) => _passes.CheckInAsync(request);

    // POST api/passes/walk-in -> visitor without a phone; staff fill in the form
    [HttpPost("walk-in")]
    public Task<SessionDto> WalkIn(WalkInRequest request) => _passes.WalkInAsync(request);
}
