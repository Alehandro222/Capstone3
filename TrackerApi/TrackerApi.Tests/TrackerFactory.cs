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
//  - a known kiosk key.
public class TrackerFactory : WebApplicationFactory<Program>
{
    public const string KioskKey = "test-kiosk-key";
    public const string Password = "correct-horse-battery";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 7, 1, 0, 0, TimeSpan.Zero));

    public static readonly JsonSerializerOptions Json = CreateJson();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open(); // an in-memory database lives as long as this connection

        builder.UseEnvironment("Testing");
        builder.UseSetting("Tracker:RequireHeartbeat", "true");
        builder.UseSetting("Tracker:CleanupIntervalSeconds", "0");
        builder.UseSetting("Kiosk:ApiKey", KioskKey);

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

    // The kiosk app on a PC.
    public HttpClient Kiosk(int? computerId = null, string key = KioskKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Kiosk-Key", key);
        if (computerId is not null) client.DefaultRequestHeaders.Add("X-Kiosk-Computer-Id", computerId.ToString());
        return client;
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

    public static async Task Heartbeat(this HttpClient client, params int[] computerIds)
    {
        foreach (var id in computerIds)
            (await client.PostAsync($"/api/computers/{id}/heartbeat", null)).EnsureSuccessStatusCode();
    }

    public static async Task<ComputerStatus> StatusOf(this HttpClient client, int computerId)
    {
        var computers = await client.GetJson<List<PublicComputerDto>>("/api/public/computers");
        return computers.Single(c => c.Id == computerId).Status;
    }

    public static object Visitor(int computerId, string first = "Juan", string last = "Dela Cruz") => new
    {
        computerId,
        firstName = first,
        lastName = last,
        age = 20,
        sex = "Male",
        purpose = "Schoolwork",
        sectors = new[] { "Youth", "Academe" }
    };
}
