using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrackerApi.Data;

// The same database, on PostgreSQL (Supabase) instead of SQLite. It only exists so
// PostgreSQL gets its own migrations (Migrations/Postgres): the SQL types differ.
// Chosen with Database:Provider = "Postgres" (see Program.cs and the README).
public class PostgresTrackerDbContext : TrackerDbContext
{
    public PostgresTrackerDbContext(DbContextOptions<PostgresTrackerDbContext> options) : base(options)
    {
    }
}

// Lets "dotnet ef migrations add ... --context PostgresTrackerDbContext" build the model
// without a real database (it never connects).
public class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresTrackerDbContext>
{
    public PostgresTrackerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresTrackerDbContext>()
            .UseNpgsql("Host=localhost;Database=misa_design_time")
            .Options);
}
