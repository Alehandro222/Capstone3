using Microsoft.EntityFrameworkCore;

namespace TrackerApi.Data;

public static class DatabaseSetup
{
    // Applies any migrations the database does not have yet.
    public static void Migrate(TrackerDbContext db)
    {
        // A tracker.db made by the old version (EnsureCreated) has tables but no
        // migration history, so migrating it would fail with a confusing error.
        if (db.Database.IsSqlite() && !db.Database.GetAppliedMigrations().Any() && HasTable(db, "Computers"))
        {
            throw new InvalidOperationException(
                "tracker.db was created by an older version of the API and cannot be upgraded. " +
                "Stop the API, delete tracker.db (and tracker.db-shm / tracker.db-wal), then start it again. " +
                "This erases the old sample data.");
        }

        db.Database.Migrate();
    }

    private static bool HasTable(TrackerDbContext db, string name) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {name}")
            .AsEnumerable()
            .First() > 0;
}
