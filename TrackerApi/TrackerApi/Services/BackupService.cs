using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The "Backup" section of appsettings.json (SQLite only: a PC at DTC).
public class BackupOptions
{
    public const string Section = "Backup";

    public bool Enabled { get; set; } = true;

    // Where the copies go, relative to the API folder (or a full path, e.g. on a USB drive).
    public string Folder { get; set; } = "backups";

    // How many days of daily copies to keep. Never more than Tracker:KeepVisitsDays,
    // so a deleted visit doesn't live on in an old backup (DTC's 1-year rule).
    public int KeepDays { get; set; } = 30;
}

// Once a day, copies the SQLite database to Backup:Folder as tracker-YYYY-MM-DD.db, while the
// API keeps running (SQLite's VACUUM INTO makes a consistent copy), and deletes copies older
// than KeepDays. Checks every hour, so a PC that was off at night still gets its copy.
// (On PostgreSQL / Supabase, backups are the database host's job: see the API README.)
public class BackupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackupService> _log;

    public BackupService(IServiceScopeFactory scopes, ILogger<BackupService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await RunOnceAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Backup failed; will try again in an hour.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Makes today's copy if there isn't one yet, then deletes old ones.
    // Returns the new file's path, or null when nothing was needed (or backups are off).
    public static async Task<string?> RunOnceAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<BackupOptions>>().Value;
        var db = services.GetRequiredService<TrackerDbContext>();
        if (!options.Enabled || !db.Database.IsSqlite()) return null;

        var tracker = services.GetRequiredService<IOptions<TrackerOptions>>().Value;
        var zone = ReportService.FindZone(tracker.TimeZone);
        var clock = services.GetRequiredService<TimeProvider>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, zone));

        var folder = Path.GetFullPath(options.Folder, services.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
        Directory.CreateDirectory(folder);

        string? created = null;
        var path = Path.Combine(folder, FileName(today));
        if (!File.Exists(path))
        {
            var temp = path + ".partial";
            if (File.Exists(temp)) File.Delete(temp);
            await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", new object[] { temp }, ct);
            File.Move(temp, path); // a half-written copy never looks like a finished one
            created = path;

            await services.GetRequiredService<AuditService>().RecordAsAsync(ActorType.System, null, "Backup",
                "backup.created", null, null, $"{Path.GetFileName(path)} ({new FileInfo(path).Length / 1024} KB)");
        }

        // Delete copies older than KeepDays (and never keep any longer than visits are kept).
        var keep = Math.Max(1, options.KeepDays);
        if (tracker.KeepVisitsDays > 0) keep = Math.Min(keep, tracker.KeepVisitsDays);
        foreach (var file in Directory.EnumerateFiles(folder, "tracker-*.db"))
        {
            if (DayOf(file) is DateOnly day && day.AddDays(keep) <= today) File.Delete(file);
        }

        return created;
    }

    public static string FileName(DateOnly day) => $"tracker-{day:yyyy-MM-dd}.db";

    private static DateOnly? DayOf(string file) =>
        DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file)["tracker-".Length..], "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
