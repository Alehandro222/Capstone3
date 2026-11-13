using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace TrackerApi.Auth;

// The "RateLimits" section of appsettings.json.
public class RateLimitOptions
{
    public const string Section = "RateLimits";

    public bool Enabled { get; set; } = true;

    // Counted per device (a phone, a browser or a kiosk PC: see "Devices" below).
    public int SignInPerMinute { get; set; } = 10;        // staff sign-in attempts
    public int RegistrationsPer10Minutes { get; set; } = 6; // new visits (the login form)
    public int PassesPer10Minutes { get; set; } = 6;      // new passes (picking a PC)
    public int WaitlistJoinsPer10Minutes { get; set; } = 5;
    public int PairingPerMinute { get; set; } = 10;       // kiosk pairing-code attempts

    // One network address may do this many times a device's limit, shared by every device
    // behind it. Hosted (Netlify/Render), all phones on DTC's Wi-Fi reach the internet from
    // the same public address, so this is the limit for the whole centre: 10 x 6 = 60
    // registrations in 10 minutes. Raise it for a big event. On a PC at DTC each phone has
    // its own address, so it rarely matters there.
    public int PerNetworkFactor { get; set; } = 10;
}

// Limits how often one device can use the open routes, so nobody can reserve
// every PC with a script, flood the waitlist, or guess passwords at speed.
// Over the limit, the API answers 429 with a message and a Retry-After header.
//
// Devices: the API gives each browser (and kiosk app) a random "misa.device" cookie and
// counts each limit per cookie. The cookie exists only for counting; nothing is stored
// with it. A script can drop its cookie to look like a new device, so every network
// address also has a cap (PerNetworkFactor x the limit) shared by all devices behind it.
//
// Addresses: phones reach the API through proxies (the Vite dev server, or Netlify and
// Render), which pass on the phone's address in X-Forwarded-For; HostingSetup.cs decides
// which proxies to believe (Hosting:TrustedProxyHops).
public static class RateLimits
{
    public const string SignIn = "signin";
    public const string Registrations = "registrations";
    public const string Passes = "passes";
    public const string WaitlistJoin = "waitlist-join";
    public const string Pairing = "pairing";

    public const string DeviceCookie = "misa.device";
    private const string DeviceItem = "misa.device";

    // Each policy: its per-device limit and its window.
    private static readonly Dictionary<string, (Func<RateLimitOptions, int> Limit, TimeSpan Window)> Policies = new()
    {
        [SignIn] = (x => x.SignInPerMinute, TimeSpan.FromMinutes(1)),
        [Registrations] = (x => x.RegistrationsPer10Minutes, TimeSpan.FromMinutes(10)),
        [Passes] = (x => x.PassesPer10Minutes, TimeSpan.FromMinutes(10)),
        [WaitlistJoin] = (x => x.WaitlistJoinsPer10Minutes, TimeSpan.FromMinutes(10)),
        [Pairing] = (x => x.PairingPerMinute, TimeSpan.FromMinutes(1)),
    };

    public static IServiceCollection AddTrackerRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.Section));

        services.AddRateLimiter(o =>
        {
            // Per device: the policy named on the route ([EnableRateLimiting(...)]).
            foreach (var (name, policy) in Policies)
                o.AddPolicy(name, ctx => Limiter(ctx, Device(ctx), policy.Limit, policy.Window));

            // Per network address: the same routes, PerNetworkFactor times the limit, shared.
            // (The global limiter sees every request; routes without a policy are not limited.)
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var name = ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                if (name is null || !Policies.TryGetValue(name, out var policy))
                    return RateLimitPartition.GetNoLimiter("");

                var address = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return Limiter(ctx, $"{name}|{address}",
                    x => policy.Limit(x) * Math.Max(1, x.PerNetworkFactor), policy.Window);
            });

            o.OnRejected = async (ctx, ct) =>
            {
                var response = ctx.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                response.ContentType = "application/json";
                await response.WriteAsync(JsonSerializer.Serialize(new
                {
                    message = "Too many tries from this device. Please wait a few minutes and try again."
                }), ct);
            };
        });

        return services;
    }

    // Gives each device its counting cookie (before the limits are counted), then applies the limits.
    public static void UseTrackerRateLimits(this WebApplication app)
    {
        var requireHttps = app.Configuration.GetValue<bool>("Hosting:RequireHttps");
        app.Use((ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                var device = ctx.Request.Cookies[DeviceCookie];
                if (!IsDeviceId(device))
                {
                    device = Guid.NewGuid().ToString("N");
                    ctx.Response.Cookies.Append(DeviceCookie, device, new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = requireHttps,
                        Path = "/api",
                        MaxAge = TimeSpan.FromDays(30),
                        IsEssential = true // for fair limits, not tracking
                    });
                }
                ctx.Items[DeviceItem] = device;
            }
            return next(ctx);
        });
        app.UseRateLimiter();
    }

    private static string Device(HttpContext ctx) =>
        ctx.Items[DeviceItem] as string ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // 32 lowercase hex digits, as made above. Anything else gets a new cookie.
    private static bool IsDeviceId(string? value) =>
        value is { Length: 32 } && value.All(char.IsAsciiHexDigitLower);

    private static RateLimitPartition<string> Limiter(HttpContext ctx, string key, Func<RateLimitOptions, int> limit, TimeSpan window)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        if (!options.Enabled) return RateLimitPartition.GetNoLimiter(key);

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limit(options)),
            Window = window,
            QueueLimit = 0 // over the limit = refused straight away, never queued
        });
    }
}
