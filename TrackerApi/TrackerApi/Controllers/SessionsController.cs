using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Staff only, except the routes the kiosk app needs (marked StaffOrKiosk).
[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly SessionService _sessions;
    private readonly AuditService _audit;

    public SessionsController(SessionService sessions, AuditService audit)
    {
        _sessions = sessions;
        _audit = audit;
    }

    // GET api/sessions/active -> everyone currently using a computer
    [HttpGet("active")]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public Task<List<SessionDto>> GetActive() => _sessions.GetActiveAsync();

    // GET api/sessions?computerId=1&from=2026-11-01&to=2026-12-01&limit=100 -> history, newest first
    [HttpGet]
    public Task<List<SessionDto>> GetHistory([FromQuery] int? computerId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] int limit = 100) =>
        _sessions.GetHistoryAsync(computerId, from?.ToUniversalTime(), to?.ToUniversalTime(), limit);

    // GET api/sessions/5
    [HttpGet("{id:int}")]
    public Task<SessionDto> Get(int id) => _sessions.GetAsync(id);

    // POST api/sessions/start   (the kiosk's own login form)
    // body: { "computerId": 1, "userName": "Maria", "purpose": "Job application" }
    [HttpPost("start")]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public async Task<SessionDto> Start(StartSessionRequest request)
    {
        var session = await _sessions.StartFromKioskFormAsync(request);
        await _audit.RecordAsync("session.start", "Session", session.Id,
            $"computer {session.ComputerId}, {session.UserName}");
        return session;
    }

    // POST api/sessions/5/end   body (optional): { "reason": "Idle" }
    // Frees the computer and its peripherals. Without a reason: "Staff" when
    // staff end it, "Visitor" when the kiosk app does (the Log out button).
    [HttpPost("{id:int}/end")]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public async Task<SessionDto> End(int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EndSessionRequest? request)
    {
        var reason = request?.Reason ?? (User.IsInRole(Roles.Kiosk) ? SessionEndReason.Visitor : SessionEndReason.Staff);
        var session = await _sessions.EndAsync(id, reason);
        await _audit.RecordAsync("session.end", "Session", id, $"computer {session.ComputerId}, reason: {reason}");
        return session;
    }

    // POST api/sessions/5/extend -> adds time, if the extension rules allow it
    [HttpPost("{id:int}/extend")]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public async Task<SessionDto> Extend(int id)
    {
        var session = await _sessions.ExtendAsync(id);
        await _audit.RecordAsync("session.extend", "Session", id,
            $"now ends {session.ScheduledEndAt:u} (extension {session.ExtensionCount})");
        return session;
    }
}
