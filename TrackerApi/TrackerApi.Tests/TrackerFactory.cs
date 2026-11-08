using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// Starts the whole API in memory for each test, with:
//  - a fresh in-memory SQLite database (same engine as the real one),
//  - a fake clock the test can move forward ("10 minutes later..."),
//  - heartbeats required, so Offline can be tested,
//  - the background cleanup job off (tests run it by hand),
//  - rate limits off (RateLimitTests switch them on).
public class TrackerFactory : WebApplicationFactory<Program>
{
    public const string Password = "correct-horse-battery";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly bool _rateLimits;
    private readonly Dictionary<int, HttpClient> _kiosks = new();

    public TrackerFactory(bool rateLimits = false)
    {
        _rateLimits = rateLimits;
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 7, 1, 0, 0, TimeSpan.Zero));

    public static readonly JsonSerializerOptions Json = CreateJson();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open(); // an in-memory database lives as long as this connection

        builder.UseEnvironment("Testing");
        builder.UseSetting("Tracker:RequireHeartbeat", "true");
        builder.UseSetting("Tracker:CleanupIntervalSeconds", "0");
        builder.UseSetting("RateLimits:Enabled", _rateLimits ? "true" : "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TrackerDbContext>>();
            services.AddDbContext<TrackerDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    // A browser with nobody signed in (a visitor's phone).
    public HttpClient Anonymous() => CreateClient();

    // The kiosk app on a PC, paired the real way: an admin creates a pairing
    // code and the app trades it for its key. One client per PC.
    public async Task<HttpClient> KioskAsync(int computerId)
    {
        if (_kiosks.TryGetValue(computerId, out var existing)) return existing;

        var code = await NewPairingCodeAsync(computerId);
        var client = CreateClient();
        var paired = await client.PostJson<KioskPairedDto>("/api/kiosk/pair", new { code });
        var kiosk = KioskWithKey(computerId, paired.KioskKey);
        _kiosks[computerId] = kiosk;
        return kiosk;
    }

    public async Task<string> NewPairingCodeAsync(int computerId)
    {
        using var scope = Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<KioskPairingService>().CreateCodeAsync(computerId)).Code;
    }

    public HttpClient KioskWithKey(int computerId, string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Kiosk-Key", key);
        client.DefaultRequestHeaders.Add("X-Kiosk-Computer-Id", computerId.ToString());
        return client;
    }

    // The kiosk app on each of these PCs says "I'm on".
    public async Task Heartbeat(params int[] computerIds)
    {
        foreach (var id in computerIds)
            await (await KioskAsync(id)).PostJson<KioskStateDto>("/api/kiosk/heartbeat");
    }

    // Creates a staff account straight in the database.
    public async Task<StaffDto> CreateStaffAsync(string username, StaffRole role = StaffRole.Staff,
        string? email = null, string? mobile = null, string password = Password)
    {
        using var scope = Services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<StaffService>();
        return await staff.CreateAsync(new CreateStaffRequest(username, $"{username} Full Name", role, password, email, mobile));
    }

    // A browser signed in as a (new) staff member. The client keeps the cookie.
    public async Task<HttpClient> SignedInAsync(string username = "admin", StaffRole role = StaffRole.Admin)
    {
        await CreateStaffAsync(username, role);
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

// Small helpers so the tests read like the real flow.
public static class ApiExtensions
{
    public static async Task<T> GetJson<T>(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        await response.EnsureOk();
        return (await response.Content.ReadFromJsonAsync<T>(TrackerFactory.Json))!;
    }

    public static async Task<T> PostJson<T>(this HttpClient client, string url, object? body = null)
    {
        var response = await client.PostAsJsonAsync(url, body ?? new { }, TrackerFactory.Json);
        await response.EnsureOk();
        return (await response.Content.ReadFromJsonAsync<T>(TrackerFactory.Json))!;
    }

    public static Task<HttpResponseMessage> Post(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, TrackerFactory.Json);

    public static async Task<string> Message(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("message").GetString()!;
    }

    private static async Task EnsureOk(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Exception($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static async Task<ComputerStatus> StatusOf(this HttpClient client, int computerId)
    {
        var computers = await client.GetJson<List<PublicComputerDto>>("/api/public/computers");
        return computers.Single(c => c.Id == computerId).Status;
    }

    // The registration form, filled in.
    public static object Registration(string first = "Juan", string last = "Dela Cruz") => new
    {
        firstName = first,
        lastName = last,
        ageGroup = "Age18To30",
        sex = "Male",
        sectors = new[] { "Youth", "Academe" },
        services = new[] { "IctEquipment", "Connectivity" }
    };

    // The desk's walk-in form: the registration, plus a PC (or null to only log the visit).
    public static object WalkIn(int? computerId, string first = "Juan", string last = "Dela Cruz") => new
    {
        firstName = first,
        lastName = last,
        ageGroup = "Age60AndAbove",
        sex = "Female",
        sectors = new[] { "SeniorCitizen" },
        services = new[] { "IctEquipment" },
        computerId
    };

    // A phone registers (logs the visit).
    public static Task<CreatedVisitDto> Register(this HttpClient phone, string first = "Juan", string last = "Dela Cruz") =>
        phone.PostJson<CreatedVisitDto>("/api/visits", Registration(first, last));

    // A registered phone picks a PC.
    public static Task<VisitDto> Pick(this HttpClient phone, string token, int computerId) =>
        phone.PostJson<VisitDto>($"/api/visits/{token}/pass", new { computerId });

    // Register and pick in one go; returns the token and the pass code.
    public static async Task<(string Token, string Code)> RegisterAndPick(this HttpClient phone, int computerId,
        string first = "Juan", string last = "Dela Cruz")
    {
        var visit = await phone.Register(first, last);
        var picked = await phone.Pick(visit.Token, computerId);
        return (visit.Token, picked.Pass!.Code);
    }
}
