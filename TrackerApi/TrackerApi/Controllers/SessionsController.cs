using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Staff only. (The kiosk app uses /api/kiosk instead; see KioskController.)
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
    public Task<List<SessionDto>> GetActive() => _sessions.GetActiveAsync();

    // GET api/sessions?computerId=1&from=2026-11-01&to=2026-12-01&limit=100 -> history, newest first
    [HttpGet]
    public Task<List<SessionDto>> GetHistory([FromQuery] int? computerId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] int limit = 100) =>
        _sessions.GetHistoryAsync(computerId, from?.ToUniversalTime(), to?.ToUniversalTime(), limit);

    // GET api/sessions/5
    [HttpGet("{id:int}")]
    public Task<SessionDto> Get(int id) => _sessions.GetAsync(id);

    // POST api/sessions/start   (the dashboard's "Start session": name + purpose typed by staff)
    // body: { "computerId": 1, "userName": "Maria", "purpose": "Job application" }
    [HttpPost("start")]
    public async Task<SessionDto> Start(StartSessionRequest request)
    {
        var session = await _sessions.StartByNameAsync(request);
        await _audit.RecordAsync("session.start", "Session", session.Id,
            $"computer {session.ComputerId}, {session.UserName}");
        return session;
    }

    // POST api/sessions/5/end   body (optional): { "reason": "Idle" }
    // Frees the computer and its peripherals. Without a reason it counts as ended by staff.
    [HttpPost("{id:int}/end")]
    public async Task<SessionDto> End(int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EndSessionRequest? request)
    {
        // Staff can only record that staff ended it; the other reasons belong to the
        // visitor, the kiosk app and the cleanup job, so the history stays truthful.
        if (request?.Reason is not (null or SessionEndReason.Staff))
            throw TrackerException.BadRequest("Staff can only end a session with the reason \"Staff\".");
        var reason = SessionEndReason.Staff;
        var session = await _sessions.EndAsync(id, reason);
        await _audit.RecordAsync("session.end", "Session", id, $"computer {session.ComputerId}, reason: {reason}");
        return session;
    }

    // POST api/sessions/5/extend -> adds time, if the extension rules allow it
    [HttpPost("{id:int}/extend")]
    public async Task<SessionDto> Extend(int id)
    {
        var session = await _sessions.ExtendAsync(id);
        await _audit.RecordAsync("session.extend", "Session", id,
            $"now ends {session.ScheduledEndAt:u} (extension {session.ExtensionCount})");
        return session;
    }
}
