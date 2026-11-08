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

    // Counted per device (IP address) on the library network.
    public int SignInPerMinute { get; set; } = 10;        // staff sign-in attempts
    public int PassesPer10Minutes { get; set; } = 6;      // new passes (picking a PC)
    public int WaitlistJoinsPer10Minutes { get; set; } = 5;
    public int PairingPerMinute { get; set; } = 10;       // kiosk pairing-code attempts
}

// Limits how often one device can use the open routes, so nobody can reserve
// every PC with a script, flood the waitlist, or guess passwords at speed.
// Over the limit, the API answers 429 with a message and a Retry-After header.
//
// NOTE: limits are per IP address. If the API is ever put behind a proxy, every
// request would share the proxy's address; the proxy's forwarded address must
// then be used instead (UseForwardedHeaders).
public static class RateLimits
{
    public const string SignIn = "signin";
    public const string Passes = "passes";
    public const string WaitlistJoin = "waitlist-join";
    public const string Pairing = "pairing";

    public static IServiceCollection AddTrackerRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.Section));

        services.AddRateLimiter(o =>
        {
            o.AddPolicy(SignIn, ctx => PerDevice(ctx, x => x.SignInPerMinute, TimeSpan.FromMinutes(1)));
            o.AddPolicy(Passes, ctx => PerDevice(ctx, x => x.PassesPer10Minutes, TimeSpan.FromMinutes(10)));
            o.AddPolicy(WaitlistJoin, ctx => PerDevice(ctx, x => x.WaitlistJoinsPer10Minutes, TimeSpan.FromMinutes(10)));
            o.AddPolicy(Pairing, ctx => PerDevice(ctx, x => x.PairingPerMinute, TimeSpan.FromMinutes(1)));

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

    private static RateLimitPartition<string> PerDevice(HttpContext ctx, Func<RateLimitOptions, int> limit, TimeSpan window)
    {
        var options = ctx.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var device = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!options.Enabled) return RateLimitPartition.GetNoLimiter(device);

        return RateLimitPartition.GetFixedWindowLimiter(device, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, limit(options)),
            Window = window,
            QueueLimit = 0 // over the limit = refused straight away, never queued
        });
    }
}
