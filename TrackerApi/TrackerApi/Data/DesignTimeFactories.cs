using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrackerApi.Data;

// Let "dotnet ef migrations add ..." build each model without a real database (they never
// connect for "migrations add"):
//   --context TrackerDbContext                                              -> Migrations/ (SQLite)
//   --context PostgresTrackerDbContext --output-dir Migrations/Postgres    -> PostgreSQL
//
// Keep the SQLite factory FIRST in this file. EF picks the first factory that fits, and the
// PostgreSQL one also "fits" TrackerDbContext (PostgresTrackerDbContext is a TrackerDbContext),
// so without this one, or with the order swapped, --context TrackerDbContext would silently
// make PostgreSQL migrations. DesignTimeTests checks this.
public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TrackerDbContext>()
            .UseSqlite("Data Source=tracker.db")
            .Options);
}

public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresTrackerDbContext>
{
    public PostgresTrackerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresTrackerDbContext>()
            .UseNpgsql("Host=localhost;Database=misa_design_time")
            .Options);
}
