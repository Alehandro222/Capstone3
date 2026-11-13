using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TrackerApi.Data;

namespace TrackerApi.Tests;

// "dotnet ef migrations add ... --context TrackerDbContext" must make SQLite migrations.
// EF picks the first IDesignTimeDbContextFactory in the assembly that fits the context, and
// the PostgreSQL factory also fits TrackerDbContext (the interface is covariant), so the
// SQLite factory has to come first (Data/DesignTimeFactories.cs).
public class DesignTimeTests
{
    private static Type FirstFactoryFor(Type context)
    {
        var wanted = typeof(IDesignTimeDbContextFactory<>).MakeGenericType(context);
        return typeof(TrackerDbContext).Assembly.GetTypes()
            .First(t => t is { IsClass: true, IsAbstract: false } && wanted.IsAssignableFrom(t));
    }

    [Fact]
    public void Sqlite_migrations_come_from_the_sqlite_factory()
    {
        var factory = FirstFactoryFor(typeof(TrackerDbContext));
        Assert.Equal(typeof(SqliteDesignTimeFactory), factory);

        using var db = ((IDesignTimeDbContextFactory<TrackerDbContext>)Activator.CreateInstance(factory)!).CreateDbContext([]);
        Assert.True(db.Database.IsSqlite());
    }

    [Fact]
    public void Postgres_migrations_come_from_the_postgres_factory()
    {
        var factory = FirstFactoryFor(typeof(PostgresTrackerDbContext));
        Assert.Equal(typeof(PostgresDesignTimeFactory), factory);

        using var db = ((IDesignTimeDbContextFactory<PostgresTrackerDbContext>)Activator.CreateInstance(factory)!).CreateDbContext([]);
        Assert.True(db.Database.IsNpgsql());
    }
}
