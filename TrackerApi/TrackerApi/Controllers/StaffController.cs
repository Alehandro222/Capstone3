using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Admins manage staff accounts. Accounts are deactivated, never deleted,
// so the audit log always shows who did what.
[ApiController]
[Route("api/staff")]
[Authorize(Policy = Policies.Admin)]
public class StaffController : ControllerBase
{
    private readonly StaffService _staff;
    private readonly AuditService _audit;

    public StaffController(StaffService staff, AuditService audit)
    {
        _staff = staff;
        _audit = audit;
    }

    // GET api/staff
    [HttpGet]
    public Task<List<StaffDto>> List() => _staff.ListAsync();

    // POST api/staff   body: { "username": "maria", "fullName": "Maria Santos", "role": "Staff",
    //                          "password": "...", "email": "...", "mobile": "..." }
    [HttpPost]
    public async Task<StaffDto> Create(CreateStaffRequest request)
    {
        var created = await _staff.CreateAsync(request);
        await _audit.RecordAsync("staff.create", "Staff", created.Id, $"{created.Username} ({created.Role})");
        return created;
    }

    // PUT api/staff/5   body: any of { "fullName", "role", "isActive", "email", "mobile" }
    [HttpPut("{id:int}")]
    public async Task<StaffDto> Update(int id, UpdateStaffRequest request)
    {
        var before = await _staff.GetAsync(id);
        var after = await _staff.UpdateAsync(id, request);
        await _audit.RecordAsync("staff.update", "Staff", id, Describe(before, after));
        return after;
    }

    // POST api/staff/5/password   body: { "password": "..." } -> admin sets a new password
    [HttpPost("{id:int}/password")]
    public async Task<StaffDto> SetPassword(int id, SetPasswordRequest request)
    {
        var result = await _staff.SetPasswordAsync(id, request.Password);
        await _audit.RecordAsync("staff.password_reset", "Staff", id);
        return result;
    }

    // POST api/staff/5/unlock -> clear a lockout after too many wrong passwords
    [HttpPost("{id:int}/unlock")]
    public async Task<StaffDto> Unlock(int id)
    {
        var result = await _staff.UnlockAsync(id);
        await _audit.RecordAsync("staff.unlock", "Staff", id);
        return result;
    }

    private static string Describe(StaffDto before, StaffDto after)
    {
        var changes = new List<string>();
        if (before.FullName != after.FullName) changes.Add($"name: {before.FullName} -> {after.FullName}");
        if (before.Role != after.Role) changes.Add($"role: {before.Role} -> {after.Role}");
        if (before.IsActive != after.IsActive) changes.Add(after.IsActive ? "reactivated" : "deactivated");
        if (before.Email != after.Email) changes.Add("email changed");
        if (before.Mobile != after.Mobile) changes.Add("mobile changed");
        return changes.Count == 0 ? "no changes" : string.Join("; ", changes);
    }
}
