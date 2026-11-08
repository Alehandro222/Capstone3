using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Visits: the digital version of the DTC login form.
// The visitor's phone uses the routes with {token} (no sign-in: the token is
// the proof, and it is also what the QR pass shows). Staff use the rest.
[ApiController]
[Route("api/[controller]")]
public class VisitsController : ControllerBase
{
    private readonly VisitService _visits;
    private readonly PassService _passes;
    private readonly AuditService _audit;

    public VisitsController(VisitService visits, PassService passes, AuditService audit)
    {
        _visits = visits;
        _passes = passes;
        _audit = audit;
    }

    // ---- Visitor (phone) ----

    // POST api/visits -> register (log the visit); returns the token to keep
    // body: { "firstName": "Juan", "lastName": "Dela Cruz", "ageGroup": "Age18To30", "sex": "Male",
    //         "sectors": ["Youth", "Academe"], "services": ["IctEquipment", "Connectivity"] }
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Registrations)]
    public Task<CreatedVisitDto> Register(RegistrationRequest request) => _visits.RegisterAsync(request);

    // GET api/visits/{token} -> the visit, its PC pass, session and waitlist place (the phone polls this)
    [HttpGet("{token}")]
    [AllowAnonymous]
    public Task<VisitDto> Get(string token) => _visits.GetForVisitorAsync(token);

    // POST api/visits/{token}/pass   body: { "computerId": 4 } -> reserve a PC (replaces an earlier pick)
    [HttpPost("{token}/pass")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Passes)]
    public Task<VisitDto> PickComputer(string token, PickComputerRequest request) =>
        _passes.PickAsync(token, request.ComputerId);

    // POST api/visits/{token}/pass/cancel -> give the PC back
    [HttpPost("{token}/pass/cancel")]
    [AllowAnonymous]
    public Task<VisitDto> CancelPass(string token) => _passes.CancelAsync(token);

    // POST api/visits/{token}/session/end -> "End session" on the phone
    [HttpPost("{token}/session/end")]
    [AllowAnonymous]
    public Task<VisitDto> EndSession(string token) => _visits.EndSessionAsync(token);

    // POST api/visits/{token}/session/extend
    [HttpPost("{token}/session/extend")]
    [AllowAnonymous]
    public Task<VisitDto> ExtendSession(string token) => _visits.ExtendSessionAsync(token);

    // POST api/visits/{token}/waitlist -> join (only when no PC is free)
    [HttpPost("{token}/waitlist")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.WaitlistJoin)]
    public Task<VisitDto> JoinWaitlist(string token) => _visits.JoinWaitlistAsync(token);

    // DELETE api/visits/{token}/waitlist -> leave the line
    [HttpDelete("{token}/waitlist")]
    [AllowAnonymous]
    public Task<VisitDto> LeaveWaitlist(string token) => _visits.LeaveWaitlistAsync(token);

    // ---- Staff (a staff sign-in is needed: the default for every endpoint) ----

    // GET api/visits?from=2026-11-08&to=2026-11-09&limit=200 -> the visitor log, newest first
    [HttpGet]
    public Task<List<StaffVisitDto>> List([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int limit = 200) =>
        _visits.ListAsync(from?.ToUniversalTime(), to?.ToUniversalTime(), limit);

    // POST api/visits/walk-in -> a visitor without a phone; staff fill in the form.
    // With "computerId" the session starts right away; without it the visit is only logged.
    [HttpPost("walk-in")]
    public async Task<WalkInResultDto> WalkIn(WalkInRequest request)
    {
        var result = await _visits.WalkInAsync(request);
        await _audit.RecordAsync("visit.walk_in", "Visit", result.Visit.Id,
            result.Session is null ? "visit only" : $"computer {result.Session.ComputerId}, session {result.Session.Id}");
        return result;
    }
}
