using Microsoft.EntityFrameworkCore;

namespace TrackerApi.Data;

public static class DatabaseSetup
{
    private const string Reset =
        "Stop the API, delete tracker.db (and tracker.db-shm / tracker.db-wal), then start it again. " +
        "This erases the old sample data.";

    // Applies any migrations the database does not have yet.
    public static void Migrate(TrackerDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            var applied = db.Database.GetAppliedMigrations().ToList();

            // Made by the very first version (EnsureCreated): tables but no migration history.
            if (applied.Count == 0 && HasTable(db, "Computers"))
                throw new InvalidOperationException("tracker.db was created by an older version of the API. " + Reset);

            // Made by a development build whose migrations were later merged into one
            // (before the first release). Migrating it would fail with a confusing error.
            var known = db.Database.GetMigrations().ToHashSet();
            if (applied.Any(m => !known.Contains(m)))
                throw new InvalidOperationException("tracker.db was made by an older development version of the API. " + Reset);
        }

        db.Database.Migrate();
    }

    private static bool HasTable(TrackerDbContext db, string name) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {name}")
            .AsEnumerable()
            .First() > 0;
}
