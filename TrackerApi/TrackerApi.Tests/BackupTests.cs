using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// The daily copy of the SQLite database (a PC at DTC).
public class BackupTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "misa-backup-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private TrackerFactory Factory(int keepDays = 30) => new(settings: new()
    {
        ["Backup:Enabled"] = "true",
        ["Backup:Folder"] = _folder,
        ["Backup:KeepDays"] = keepDays.ToString()
    });

    private static async Task<string?> RunAsync(TrackerFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await BackupService.RunOnceAsync(scope.ServiceProvider);
    }

    [Fact]
    public async Task Makes_one_readable_copy_a_day()
    {
        using var factory = Factory();
        await RunAsync(factory); // (the background job may already have made it when the API started)

        var path = Path.Combine(_folder, "tracker-2026-11-07.db"); // the day in Baguio
        Assert.True(File.Exists(path));
        Assert.Null(await RunAsync(factory)); // one copy a day
        Assert.Empty(Directory.GetFiles(_folder, "*.partial"));

        using (var copy = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            copy.Open();
            using var count = copy.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Computers";
            Assert.Equal(12L, count.ExecuteScalar()); // a complete, working database
        }

        var admin = await factory.SignedInAsync();
        Assert.Contains("tracker-2026-11-07.db", Assert.Single(await admin.GetJson<List<AuditEntryDto>>("/api/audit?action=backup.")).Details);

        factory.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(Path.Combine(_folder, "tracker-2026-11-08.db"), await RunAsync(factory)); // the next day: a new copy
    }

    [Fact]
    public async Task Old_copies_are_deleted()
    {
        using var factory = Factory(keepDays: 7);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "tracker-2026-10-31.db"), "old"); // 7 days before Nov 7: goes
        File.WriteAllText(Path.Combine(_folder, "tracker-2026-11-01.db"), "recent"); // stays
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not a backup"); // never touched

        await RunAsync(factory);

        var names = Directory.GetFiles(_folder).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "notes.txt", "tracker-2026-11-01.db", "tracker-2026-11-07.db" }, names);
    }

    [Fact]
    public async Task Copies_are_never_kept_longer_than_visits()
    {
        using var factory = new TrackerFactory(settings: new()
        {
            ["Backup:Enabled"] = "true",
            ["Backup:Folder"] = _folder,
            ["Backup:KeepDays"] = "5000",
            ["Tracker:KeepVisitsDays"] = "365"
        });
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "tracker-2025-11-06.db"), "a year and a day old");

        await RunAsync(factory);
        Assert.False(File.Exists(Path.Combine(_folder, "tracker-2025-11-06.db")));
    }
}
