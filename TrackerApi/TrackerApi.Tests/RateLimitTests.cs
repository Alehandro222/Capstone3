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
    public async Task One_device_cannot_reserve_every_pc()
    {
        await _factory.Heartbeat(1, 2, 4, 5, 6, 7, 8);
        var phone = _factory.Anonymous();

        // Six passes in 10 minutes are fine (e.g. picking, cancelling, picking again)...
        foreach (var pc in new[] { 1, 2, 4, 5, 6, 7 })
            await phone.PostJson<CreatedPassDto>("/api/passes", ApiExtensions.Visitor(pc));

        // ...the seventh is refused, so PC-08 stays free for someone else.
        var seventh = await phone.Post("/api/passes", ApiExtensions.Visitor(8));
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
}
