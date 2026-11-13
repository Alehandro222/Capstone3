using System.Net;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// Phones reach the API through the website's server on the same PC, which passes on
// each phone's address in X-Forwarded-For. Rate limits and the audit log must use it,
// but only when the request really comes from this machine.
public class ForwardedAddressTests
{
    private static HttpClient Phone(TrackerFactory factory, string address)
    {
        var client = factory.Anonymous();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        return client;
    }

    [Fact]
    public async Task Phones_behind_the_web_server_each_get_their_own_limit()
    {
        using var factory = new TrackerFactory(rateLimits: true, remoteIp: IPAddress.Loopback);
        var ana = Phone(factory, "192.168.1.21");
        var ben = Phone(factory, "192.168.1.22");

        for (var i = 0; i < 6; i++) await ana.Register($"Ana{i}", "Reyes");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ana.Post("/api/visits", ApiExtensions.Registration())).StatusCode);

        // Ben is another phone: before the fix, all phones shared the server's one limit.
        await ben.Register("Ben", "Cruz");
    }

    [Fact]
    public async Task The_audit_log_records_the_visitors_address()
    {
        using var factory = new TrackerFactory(remoteIp: IPAddress.Loopback);
        var admin = await factory.SignedInAsync();
        var browser = Phone(factory, "192.168.1.30");
        await browser.Post("/api/auth/login", new { username = "admin", password = "wrong-password" });

        var failed = (await admin.GetJson<List<AuditEntryDto>>("/api/audit?action=auth.login_failed")).Single();
        Assert.Equal("192.168.1.30", failed.IpAddress);
    }

    [Fact]
    public async Task A_device_on_the_network_cannot_fake_its_address()
    {
        // Each limit is per device (cookie), with a shared cap per real address: 2 x 6 = 12 here.
        using var factory = new TrackerFactory(rateLimits: true, remoteIp: IPAddress.Parse("192.168.1.50"),
            settings: new() { ["RateLimits:PerNetworkFactor"] = "2" });

        // A script on one device, a new made-up address (and no cookie) each time:
        // it still counts against its one real address.
        for (var i = 0; i < 12; i++) await Phone(factory, $"10.0.0.{i}").Register($"Fake{i}", "Test");
        var next = await Phone(factory, "10.0.0.99").Post("/api/visits", ApiExtensions.Registration());
        Assert.Equal(HttpStatusCode.TooManyRequests, next.StatusCode);
    }
}
