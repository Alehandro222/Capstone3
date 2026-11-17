using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// The tests above run on SQLite; this one runs the real PostgreSQL migrations. It does nothing
// (and passes) unless MISA_TEST_POSTGRES is set to the connection string of a THROWAWAY database
// whose name contains "test" (a local Postgres, or a scratch Supabase project). It erases that
// database first. Never point it at the live one.
public class PostgresTests
{
    [Fact]
    public async Task Postgres_migrations_apply_keep_RLS_on_and_refuse_two_sessions_on_one_PC()
    {
        var text = Environment.GetEnvironmentVariable("MISA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(text)) return; // not set: nothing to test against

        Assert.Contains("test", new NpgsqlConnectionStringBuilder(text).Database ?? "", StringComparison.OrdinalIgnoreCase);
        var options = new DbContextOptionsBuilder<PostgresTrackerDbContext>().UseNpgsql(text).Options;

        await using var db = new PostgresTrackerDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        // Row-level security stays on for every table (Supabase's Data API must see nothing).
        var open = await db.Database
            .SqlQueryRaw<string>("SELECT c.relname AS \"Value\" FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
                                "WHERE n.nspname = 'public' AND c.relkind = 'r' AND NOT c.relrowsecurity")
            .ToListAsync();
        Assert.Empty(open);

        // The "one active session per PC" index is what stops two phones taking the same PC.
        var now = DateTime.UtcNow;
        foreach (var hash in new[] { "a", "b" })
        {
            var visit = new Visit { TokenHash = hash, FirstName = "Ana", LastName = "Reyes", CreatedAt = now, ExpiresAt = now.AddHours(1) };
            db.Sessions.Add(new UsageSession { ComputerId = 1, Visit = visit, StartedAt = now, ScheduledEndAt = now.AddHours(3) });
            if (hash == "a") await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<TrackerException>(() => db.SaveOrConflictAsync("taken"));
    }
}
