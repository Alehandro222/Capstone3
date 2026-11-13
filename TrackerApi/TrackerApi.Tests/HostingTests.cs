using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrackerApi.Auth;

namespace TrackerApi.Tests;

// Running on the internet: Netlify (website) -> Render (API). See Auth/HostingSetup.cs.
public class HostingTests
{
    private const string Secret = "netlify-test-secret";

    // A token like the one Netlify puts in x-nf-sign (a JWS signed with HMAC-SHA256).
    private static string NetlifyToken(string secret = Secret, long? exp = null, string iss = "netlify")
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss,
            exp = exp ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
            site_url = "https://misa.netlify.app"
        }));
        var signature = B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    [Fact]
    public async Task Every_answer_has_security_headers_and_api_answers_are_never_cached()
    {
        using var factory = new TrackerFactory();
        var response = await factory.Anonymous().GetAsync("/api/public/computers");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Health_check_needs_no_sign_in()
    {
        using var factory = new TrackerFactory();
        var health = await factory.Anonymous().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("ok", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_a_netlify_secret_only_requests_signed_by_netlify_get_through()
    {
        using var factory = new TrackerFactory(settings: new() { ["Hosting:ProxySignatureSecret"] = Secret });
        var client = factory.Anonymous();

        // Straight to Render, skipping the website: refused.
        var direct = await client.GetAsync("/api/public/computers");
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        Assert.Contains("MISA website", await direct.Message());

        // Render's own health check still works.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        async Task<HttpStatusCode> With(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/public/computers");
            request.Headers.Add("x-nf-sign", token);
            return (await client.SendAsync(request)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await With(NetlifyToken()));
        Assert.Equal(HttpStatusCode.Forbidden, await With(NetlifyToken(secret: "someone-elses")));
        Assert.Equal(HttpStatusCode.Forbidden, await With(NetlifyToken(exp: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds())));
        Assert.Equal(HttpStatusCode.Forbidden, await With(NetlifyToken(iss: "not-netlify")));
        Assert.Equal(HttpStatusCode.Forbidden, await With("not.a.token"));
    }

    [Fact]
    public async Task Behind_netlify_and_render_each_visitor_keeps_their_own_rate_limit()
    {
        // Render's load balancer calls the API; X-Forwarded-For is "<visitor>, <Netlify>".
        // Each new client here is a new device (no cookie), so this checks the cap per
        // address, set to the device limit (factor 1) to make it easy to reach.
        using var factory = new TrackerFactory(rateLimits: true, remoteIp: IPAddress.Parse("10.10.0.1"),
            settings: new() { ["Hosting:TrustedProxyHops"] = "2", ["RateLimits:PerNetworkFactor"] = "1" });

        HttpClient Phone(string forwardedFor)
        {
            var client = factory.Anonymous();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
            return client;
        }

        for (var i = 0; i < 6; i++) await Phone("203.0.113.5, 18.1.2.3").Register($"Ana{i}", "Reyes");
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Phone("203.0.113.5, 18.1.2.3").Post("/api/visits", ApiExtensions.Registration())).StatusCode);

        // A visitor who adds a made-up address at the front is still themselves.
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Phone("1.1.1.1, 203.0.113.5, 18.1.2.3").Post("/api/visits", ApiExtensions.Registration())).StatusCode);

        // Another visitor through the same Netlify server has their own limit.
        await Phone("198.51.100.9, 18.1.2.3").Register("Ben", "Cruz");
    }

    [Fact]
    public async Task On_the_internet_http_goes_to_https_and_the_sign_in_cookie_is_https_only()
    {
        using var factory = new TrackerFactory(remoteIp: IPAddress.Parse("10.10.0.1"),
            settings: new() { ["Hosting:RequireHttps"] = "true", ["Hosting:TrustedProxyHops"] = "1" });
        await factory.CreateStaffAsync("maria");
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var plain = await client.GetAsync("/api/public/computers");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, plain.StatusCode);
        Assert.StartsWith("https://", plain.Headers.Location!.ToString());

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username = "maria", password = TrackerFactory.Password })
        };
        login.Headers.Add("X-Forwarded-Proto", "https");
        login.Headers.Add("X-Forwarded-For", "203.0.113.5");
        var signedIn = await client.SendAsync(login);
        signedIn.EnsureSuccessStatusCode();
        var cookie = signedIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthSetup.CookieName));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
