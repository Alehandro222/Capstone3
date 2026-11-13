using System.Net;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// The same limits as appsettings.json, with the limiter switched on.
public class RateLimitTests : IDisposable
{
    private readonly TrackerFactory _factory = new(rateLimits: true);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Eleventh_sign_in_attempt_in_a_minute_is_refused()
    {
        var browser = _factory.Anonymous();
        for (var i = 0; i < 10; i++)
        {
            var attempt = await browser.Post("/api/auth/login", new { username = $"guess{i}", password = "nope" });
            Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
        }

        var refused = await browser.Post("/api/auth/login", new { username = "guess", password = "nope" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("Too many tries", await refused.Message());
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task One_device_cannot_register_endlessly()
    {
        var phone = _factory.Anonymous();
        for (var i = 0; i < 6; i++)
            await phone.Register($"Visitor{i}", "Test");

        var seventh = await phone.Post("/api/visits", ApiExtensions.Registration());
        Assert.Equal(HttpStatusCode.TooManyRequests, seventh.StatusCode);
    }

    [Fact]
    public async Task One_device_cannot_keep_grabbing_pcs()
    {
        await _factory.Heartbeat(1, 2, 4, 5, 6, 7, 8);
        var phone = _factory.Anonymous();
        var visit = await phone.Register();

        // Six picks in 10 minutes are fine (e.g. changing their mind)...
        foreach (var pc in new[] { 1, 2, 4, 5, 6, 7 })
            await phone.Pick(visit.Token, pc);

        // ...the seventh is refused.
        var seventh = await phone.Post($"/api/visits/{visit.Token}/pass", new { computerId = 8 });
        Assert.Equal(HttpStatusCode.TooManyRequests, seventh.StatusCode);
        Assert.Equal(ComputerStatus.Available, await phone.StatusOf(8));
    }

    [Fact]
    public async Task Pairing_codes_cannot_be_guessed_at_speed()
    {
        var device = _factory.Anonymous();
        for (var i = 0; i < 10; i++)
            await device.Post("/api/kiosk/pair", new { code = $"WRONG{i:000}" });

        var refused = await device.Post("/api/kiosk/pair", new { code = "WRONG999" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    // Hosted, every phone on DTC's Wi-Fi shares DTC's one public address.
    [Fact]
    public async Task Phones_sharing_one_address_each_get_their_own_limit()
    {
        var ana = _factory.Anonymous();
        var ben = _factory.Anonymous(); // another phone, same address
        for (var i = 0; i < 6; i++) await ana.Register($"Ana{i}", "Reyes");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ana.Post("/api/visits", ApiExtensions.Registration())).StatusCode);

        await ben.Register("Ben", "Cruz");
    }

    [Fact]
    public async Task Dropping_the_cookie_still_hits_the_cap_for_the_address()
    {
        // 10 x 6 = 60 registrations from one address, however many "devices".
        for (var i = 0; i < 60; i++)
            await _factory.Anonymous().Register($"Visitor{i}", "Test");

        var next = await _factory.Anonymous().Post("/api/visits", ApiExtensions.Registration());
        Assert.Equal(HttpStatusCode.TooManyRequests, next.StatusCode);
    }

    [Fact]
    public async Task The_device_cookie_is_only_for_the_api_and_hidden_from_scripts()
    {
        var response = await _factory.Anonymous().GetAsync("/api/public/computers");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("misa.device="));
        Assert.Contains("path=/api", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^misa\.device=[0-9a-f]{32};", cookie);
    }

    [Fact]
    public async Task A_made_up_device_cookie_is_replaced()
    {
        var phone = _factory.Anonymous();
        phone.DefaultRequestHeaders.Add("Cookie", "misa.device=not-a-real-id");
        var response = await phone.GetAsync("/api/public/computers");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("misa.device="));
    }
}
