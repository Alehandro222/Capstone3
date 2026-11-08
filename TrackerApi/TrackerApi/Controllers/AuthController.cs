using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrackerApi.Auth;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Staff sign-in and sign-out. A successful sign-in sets an HttpOnly cookie that
// the browser sends with every later request.
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly StaffService _staff;
    private readonly AuditService _audit;

    public AuthController(StaffService staff, AuditService audit)
    {
        _staff = staff;
        _audit = audit;
    }

    // POST api/auth/login   body: { "username": "maria", "password": "..." }
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.SignIn)]
    public async Task<ActionResult<StaffDto>> Login(LoginRequest request)
    {
        var result = await _staff.CheckPasswordAsync(request.Username, request.Password);
        if (result.Error is not null)
        {
            await _audit.RecordAsAsync(ActorType.Visitor, null, "Unknown", "auth.login_failed",
                "Staff", result.Staff?.Id, $"username: {request.Username?.Trim()}");
            return Unauthorized(new { message = result.Error });
        }

        var staff = result.Staff!;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthSetup.CreatePrincipal(staff));
        await _audit.RecordAsAsync(ActorType.Staff, staff.Id, staff.FullName, "auth.login", "Staff", staff.Id);
        return StaffService.ToDto(staff);
    }

    // GET api/auth/me -> who is signed in (401 if nobody)
    [HttpGet("me")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<StaffDto> Me() => await _staff.GetAsync(User.StaffId()!.Value);

    // POST api/auth/logout
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (User.StaffId() is not null) await _audit.RecordAsync("auth.logout", "Staff", User.StaffId());
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    // POST api/auth/password   body: { "currentPassword": "...", "newPassword": "..." }
    [HttpPost("password")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        await _staff.ChangeOwnPasswordAsync(User.StaffId()!.Value, request.CurrentPassword, request.NewPassword);
        await _audit.RecordAsync("auth.password_changed", "Staff", User.StaffId());
        return NoContent();
    }
}
