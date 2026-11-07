using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly SessionService _sessions;

    public SessionsController(SessionService sessions)
    {
        _sessions = sessions;
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

    // POST api/sessions/start   (the kiosk's own login form)
    // body: { "computerId": 1, "userName": "Maria", "purpose": "Job application" }
    [HttpPost("start")]
    public Task<SessionDto> Start(StartSessionRequest request) => _sessions.StartFromKioskFormAsync(request);

    // POST api/sessions/5/end   body (optional): { "reason": "Staff" }
    // Frees the computer and its peripherals. Without a body the reason is "Visitor".
    [HttpPost("{id:int}/end")]
    public Task<SessionDto> End(int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EndSessionRequest? request) =>
        _sessions.EndAsync(id, request?.Reason ?? SessionEndReason.Visitor);

    // POST api/sessions/5/extend -> adds time, if the extension rules allow it
    [HttpPost("{id:int}/extend")]
    public Task<SessionDto> Extend(int id) => _sessions.ExtendAsync(id);
}
