using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;

namespace TrackerApi.Auth;

// How the API is reached: the "Hosting" section of appsettings.json.
//
//   On this PC / a PC at DTC (defaults): requests come straight from phones and kiosks,
//   or through the website's dev server on the same machine.
//   Hosted (Netlify -> Render): Render sets these as environment variables, e.g.
//     Hosting__TrustedProxyHops=2  Hosting__RequireHttps=true  Hosting__ProxySignatureSecret=<secret>
public class HostingOptions
{
    public const string Section = "Hosting";

    // How many proxies in front of the API add themselves to X-Forwarded-For.
    // 0 = trust only a proxy on this same machine (the Vite dev server).
    // Netlify -> Render = 2. Only set this when the API can't be reached any other way,
    // or together with ProxySignatureSecret, or visitors could fake their address.
    public int TrustedProxyHops { get; set; }

    // The secret Netlify signs proxied requests with (netlify.toml: signed = "API_SIGNATURE_TOKEN").
    // When set, every request must carry a valid x-nf-sign header, so nobody can skip the
    // website and talk to the API directly (except GET /health, for Render's health check).
    public string ProxySignatureSecret { get; set; } = "";

    // true on the internet: send http:// to https://, HSTS, and sign-in cookies only over HTTPS.
    public bool RequireHttps { get; set; }
}

public static class HostingSetup
{
    public const string HealthPath = "/health";

    public static void UseTrackerHosting(this WebApplication app)
    {
        var options = app.Configuration.GetSection(HostingOptions.Section).Get<HostingOptions>() ?? new HostingOptions();

        // 1. Who is really calling, and over https or not.
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        if (options.TrustedProxyHops > 0)
        {
            // Hosted: the platform's proxies have changing addresses, so trust them by count:
            // the visitor is the address that many hops back. Anything a visitor adds further
            // left in the header is ignored.
            forwarded.KnownNetworks.Clear();
            forwarded.KnownProxies.Clear();
            forwarded.ForwardLimit = options.TrustedProxyHops;
        }
        // (Otherwise ASP.NET trusts only proxies on this machine: the Vite dev server.)
        app.UseForwardedHeaders(forwarded);

        // 2. Only requests that came through the website (Netlify) when a secret is set.
        if (!string.IsNullOrEmpty(options.ProxySignatureSecret))
        {
            var key = Encoding.UTF8.GetBytes(options.ProxySignatureSecret);
            app.Use(async (ctx, next) =>
            {
                var isHealth = HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase);
                if (!isHealth && !NetlifySignature.IsValid(ctx.Request.Headers["x-nf-sign"].ToString(), key, DateTimeOffset.UtcNow))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await ctx.Response.WriteAsJsonAsync(new { message = "Please use the MISA website address." });
                    return;
                }
                await next();
            });
        }

        // 3. HTTPS on the internet.
        if (options.RequireHttps)
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // 4. Security headers on every answer.
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var h = ctx.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["Referrer-Policy"] = "no-referrer";
                h["X-Frame-Options"] = "DENY";
                h["Permissions-Policy"] = "camera=(self), microphone=(), geolocation=()";
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    // Visitor details and pass tokens must not sit in shared caches or the browser cache.
                    h["Cache-Control"] = "no-store";
                }
                else
                {
                    // The website (when the API serves it, see Program.cs): only its own files.
                    h["Content-Security-Policy"] = WebsiteContentSecurityPolicy;
                }
                return Task.CompletedTask;
            });
            await next();
        });
    }

    // Scripts, styles and data only from the site itself; no framing; camera for the desk scanner page.
    // (React sets inline styles through the DOM, which CSP allows; the QR code is inline SVG.)
    public const string WebsiteContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
}

// Checks Netlify's signed-proxy header: a JWS (compact JWT) signed with HMAC-SHA256.
// Valid = correctly signed with our secret, issued by Netlify, and not expired (if it says when).
public static class NetlifySignature
{
    public static bool IsValid(string? token, byte[] key, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        try
        {
            using (var header = JsonDocument.Parse(Base64Url(parts[0])))
            {
                if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "HS256") return false;
            }

            var expected = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(expected, Base64Url(parts[2]))) return false;

            using var payload = JsonDocument.Parse(Base64Url(parts[1]));
            var claims = payload.RootElement;
            if (claims.TryGetProperty("iss", out var iss) && iss.GetString() != "netlify") return false;
            if (claims.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                && DateTimeOffset.FromUnixTimeSeconds(seconds) < now.AddMinutes(-1))
                return false;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static byte[] Base64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s += new string('=', (4 - s.Length % 4) % 4);
        return Convert.FromBase64String(s);
    }
}
