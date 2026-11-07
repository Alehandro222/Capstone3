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

namespace TrackerApi.Tests;

// Starts the whole API in memory for each test, with:
//  - a fresh in-memory SQLite database (same engine as the real one),
//  - a fake clock the test can move forward ("10 minutes later..."),
//  - heartbeats required, so Offline can be tested,
//  - the background cleanup job off (tests run it by hand).
public class TrackerFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 7, 1, 0, 0, TimeSpan.Zero));

    public static readonly JsonSerializerOptions Json = CreateJson();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open(); // an in-memory database lives as long as this connection

        builder.UseEnvironment("Testing");
        builder.UseSetting("Tracker:RequireHeartbeat", "true");
        builder.UseSetting("Tracker:CleanupIntervalSeconds", "0");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TrackerDbContext>>();
            services.AddDbContext<TrackerDbContext>(o => o.UseSqlite(_connection));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
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
