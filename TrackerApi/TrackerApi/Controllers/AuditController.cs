using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Admins read the audit log. There is no way to change or delete entries.
[ApiController]
[Route("api/audit")]
[Authorize(Policy = Policies.Admin)]
public class AuditController : ControllerBase
{
    private readonly AuditService _audit;

    public AuditController(AuditService audit)
    {
        _audit = audit;
    }

    // GET api/audit?staffId=2&action=pass.&targetType=Session&targetId=5&from=...&to=...&limit=200
    // "action" matches the start, so "pass." finds every pass action.
    [HttpGet]
    public Task<List<AuditEntryDto>> Search([FromQuery] int? staffId, [FromQuery] string? action,
        [FromQuery] string? targetType, [FromQuery] string? targetId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, [FromQuery] int limit = 200) =>
        _audit.SearchAsync(staffId, action, targetType, targetId, from?.ToUniversalTime(), to?.ToUniversalTime(), limit);
}
