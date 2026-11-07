using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Auth;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Writes the audit log. The person (or kiosk) doing the action is taken from
// the current request, so callers only say what happened.
public class AuditService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IHttpContextAccessor _http;

    public AuditService(TrackerDbContext db, TimeProvider clock, IHttpContextAccessor http)
    {
        _db = db;
        _clock = clock;
        _http = http;
    }

    public Task RecordAsync(string action, string? targetType = null, object? targetId = null, string? details = null)
    {
        var user = _http.HttpContext?.User;
        var (type, staffId, name) = user switch
        {
            not null when user.IsInRole(Roles.Kiosk) => (ActorType.Kiosk, (int?)null, user.Identity!.Name ?? "Kiosk app"),
            not null when user.StaffId() is int id => (ActorType.Staff, id, user.Identity?.Name ?? $"Staff #{id}"),
            _ => (ActorType.Visitor, null, "Visitor")
        };
        return RecordAsAsync(type, staffId, name, action, targetType, targetId, details);
    }

    // For when the actor is not the signed-in user (e.g. a failed sign-in).
    public async Task RecordAsAsync(ActorType type, int? staffId, string actorName, string action,
        string? targetType = null, object? targetId = null, string? details = null)
    {
        _db.AuditLog.Add(new AuditEntry
        {
            At = _clock.UtcNow(),
            ActorType = type,
            StaffId = staffId,
            ActorName = Truncate(actorName, 100)!,
            Action = action,
            TargetType = targetType,
            TargetId = targetId?.ToString(),
            Details = Truncate(details, 1000),
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<AuditEntryDto>> SearchAsync(int? staffId, string? action, string? targetType,
        string? targetId, DateTime? from, DateTime? to, int limit)
    {
        var query = _db.AuditLog.AsQueryable();
        if (staffId is not null) query = query.Where(a => a.StaffId == staffId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrWhiteSpace(targetType)) query = query.Where(a => a.TargetType == targetType);
        if (!string.IsNullOrWhiteSpace(targetId)) query = query.Where(a => a.TargetId == targetId);
        if (from is not null) query = query.Where(a => a.At >= from);
        if (to is not null) query = query.Where(a => a.At < to);

        return await query
            .OrderByDescending(a => a.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(a => new AuditEntryDto(a.Id, a.At, a.ActorType, a.StaffId, a.ActorName, a.Action,
                a.TargetType, a.TargetId, a.Details, a.IpAddress))
            .ToListAsync();
    }

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max];
}
