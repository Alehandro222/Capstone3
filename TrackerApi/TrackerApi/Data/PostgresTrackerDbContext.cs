using Microsoft.EntityFrameworkCore;

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
