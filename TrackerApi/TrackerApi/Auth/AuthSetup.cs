using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Auth;

// Who may call what:
//   Visitors' phones  -> no login; they prove who they are with their pass/waitlist token.
//   Kiosk app on a PC -> sends its own PC's secret key (see KioskAuthHandler); it may
//                        only use the /api/kiosk routes, and only for its own PC.
//   Staff dashboard   -> signs in with a username and password and gets an HttpOnly cookie.
//
// Every endpoint needs a staff login unless it says otherwise
// ([AllowAnonymous] or [Authorize(Policy = Policies.Kiosk)]).
public static class Policies
{
    public const string Staff = "Staff";
    public const string Admin = "Admin";
    public const string Kiosk = "Kiosk";
}

public static class Roles
{
    public const string Staff = nameof(StaffRole.Staff);
    public const string Admin = nameof(StaffRole.Admin);
    public const string Kiosk = "Kiosk";
}

public static class AuthSetup
{
    public const string CookieName = "misa.staff";

    public static IServiceCollection AddTrackerAuth(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;                       // page scripts cannot read it
                o.Cookie.SameSite = SameSiteMode.Lax;           // other websites cannot use it
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS-only once HTTPS is on
                o.ExpireTimeSpan = TimeSpan.FromHours(10);      // about one working day
                o.SlidingExpiration = true;

                // This is an API: answer 401/403 with a message instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => WriteError(ctx.Response, 401, "Please sign in.");
                o.Events.OnRedirectToAccessDenied = ctx => WriteError(ctx.Response, 403, "You do not have permission to do that.");

                // Checked on every request: a deactivated account, or one whose role
                // changed, is signed out straight away instead of when the cookie expires.
                o.Events.OnValidatePrincipal = ValidateStaffAsync;
            })
            .AddScheme<KioskAuthOptions, KioskAuthHandler>(KioskAuthHandler.SchemeName, _ => { });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(Policies.Staff, p => p
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireRole(Roles.Staff, Roles.Admin));

            o.AddPolicy(Policies.Admin, p => p
                .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireRole(Roles.Admin));

            o.AddPolicy(Policies.Kiosk, p => p
                .AddAuthenticationSchemes(KioskAuthHandler.SchemeName)
                .RequireRole(Roles.Kiosk));

            // Secure by default: anything not marked otherwise needs a staff login.
            o.FallbackPolicy = o.GetPolicy(Policies.Staff);
        });

        return services;
    }

    // Builds the signed-in identity stored (encrypted) in the cookie.
    public static ClaimsPrincipal CreatePrincipal(Staff staff)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staff.Id.ToString()),
            new Claim(ClaimTypes.Name, staff.FullName),
            new Claim(ClaimTypes.Role, staff.Role.ToString()),
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static int? StaffId(this ClaimsPrincipal user) =>
        user.IsInRole(Roles.Kiosk) ? null
        : int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static async Task ValidateStaffAsync(CookieValidatePrincipalContext ctx)
    {
        var id = ctx.Principal?.StaffId();
        var db = ctx.HttpContext.RequestServices.GetRequiredService<TrackerDbContext>();
        var staff = id is null ? null : await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

        if (staff is null || !staff.IsActive || !ctx.Principal!.IsInRole(staff.Role.ToString()))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    // When a route accepts staff OR the kiosk, both schemes answer a refused request;
    // only the first one writes the response.
    public static Task WriteError(HttpResponse response, int status, string message)
    {
        if (response.HasStarted) return Task.CompletedTask;
        response.StatusCode = status;
        response.ContentType = "application/json";
        return response.WriteAsync(JsonSerializer.Serialize(new { message }));
    }
}
