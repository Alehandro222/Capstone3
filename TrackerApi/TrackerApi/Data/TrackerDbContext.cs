using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Models;

namespace TrackerApi.Data;

// The DbContext is your gateway to the database (like JPA's EntityManager).
// Each DbSet<T> is a table. The tables are created and changed by the
// migrations in the Migrations folder (see README).
public class TrackerDbContext : DbContext, IDataProtectionKeyContext
{
    public TrackerDbContext(DbContextOptions<TrackerDbContext> options) : base(options)
    {
    }

    // For PostgresTrackerDbContext (same tables, its own migrations).
    protected TrackerDbContext(DbContextOptions options) : base(options)
    {
    }

    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<Peripheral> Peripherals => Set<Peripheral>();
    public DbSet<UsageSession> Sessions => Set<UsageSession>();
    public DbSet<AssistTicket> AssistTickets => Set<AssistTicket>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitSector> VisitSectors => Set<VisitSector>();
    public DbSet<VisitServiceAvailed> VisitServices => Set<VisitServiceAvailed>();
    public DbSet<Pass> Passes => Set<Pass>();
    public DbSet<ProblemReport> ProblemReports => Set<ProblemReport>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    // The keys that protect staff sign-in cookies. Only used when hosted (PostgreSQL, see
    // Program.cs): a cloud container forgets its files on every restart, which would sign
    // every staff member out. (On a PC at DTC, Windows keeps them in the user profile.)
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Store every enum as text ("Pending") instead of a number (0), so the
    // database is readable and adding a new enum value never shifts old data.
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);

        // All times are stored in UTC, but SQLite forgets that when reading them back.
        // This marks them as UTC again, so the JSON always ends in "Z" and browsers
        // never mistake them for local time. (PostgreSQL also insists that what it stores
        // is marked UTC, so an unmarked time is treated as UTC on the way in.)
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            toDb => toDb.Kind == DateTimeKind.Local ? toDb.ToUniversalTime() : DateTime.SpecifyKind(toDb, DateTimeKind.Utc),
            fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc))
        {
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ---- Rules the database itself enforces --------------------------------

        // Only ONE active session per computer. A "filtered unique index" only
        // looks at rows where EndedAt is null, so ended sessions don't count.
        modelBuilder.Entity<UsageSession>()
            .HasIndex(s => s.ComputerId)
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL");

        // Only ONE pending pass per computer: two visitors can never hold the same PC.
        modelBuilder.Entity<Pass>()
            .HasIndex(p => p.ComputerId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        // A 6-digit code is unique among pending passes (codes are reused later).
        modelBuilder.Entity<Pass>()
            .HasIndex(p => p.Code)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        // One visitor, one PC at a time: at most one pending pass, one active
        // session and one place on the waitlist per visit.
        modelBuilder.Entity<Pass>()
            .HasIndex(p => p.VisitId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");
        modelBuilder.Entity<UsageSession>()
            .HasIndex(s => s.VisitId)
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL");
        modelBuilder.Entity<WaitlistEntry>()
            .HasIndex(w => w.VisitId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Waiting'");

        modelBuilder.Entity<Visit>().HasIndex(v => v.TokenHash).IsUnique();
        modelBuilder.Entity<Visit>().HasIndex(v => v.CreatedAt); // daily log and reports

        // A session points to at most one pass, and a pass to at most one session.
        modelBuilder.Entity<UsageSession>()
            .HasOne(s => s.Pass)
            .WithOne(p => p.Session)
            .HasForeignKey<UsageSession>(s => s.PassId);

        // A visitor's sectors and services: one row each, no duplicates.
        modelBuilder.Entity<VisitSector>().HasKey(vs => new { vs.VisitId, vs.Sector });
        modelBuilder.Entity<VisitServiceAvailed>().HasKey(vs => new { vs.VisitId, vs.Service });

        // Visits are never deleted while anything points to them.
        modelBuilder.Entity<Pass>().HasOne(p => p.Visit).WithMany(v => v.Passes)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UsageSession>().HasOne(s => s.Visit).WithMany(v => v.Sessions)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WaitlistEntry>().HasOne(w => w.Visit).WithMany(v => v.WaitlistEntries)
            .OnDelete(DeleteBehavior.Restrict);

        // Never delete a computer that has history. (Foreign keys already make every
        // pass, session and report point to a real computer.)
        modelBuilder.Entity<UsageSession>().HasOne(s => s.Computer).WithMany(c => c.Sessions)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Pass>().HasOne(p => p.Computer).WithMany(c => c.Passes)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProblemReport>().HasOne(r => r.Computer).WithMany(c => c.ProblemReports)
            .OnDelete(DeleteBehavior.Restrict);

        // Text length limits (SQLite ignores these, but other databases use them).
        modelBuilder.Entity<Computer>().Property(c => c.Name).HasMaxLength(50);
        modelBuilder.Entity<Computer>().Property(c => c.KioskKeyHash).HasMaxLength(64);
        modelBuilder.Entity<Computer>().Property(c => c.PairingCodeHash).HasMaxLength(64);
        modelBuilder.Entity<Visit>().Property(v => v.FirstName).HasMaxLength(100);
        modelBuilder.Entity<Visit>().Property(v => v.LastName).HasMaxLength(100);
        modelBuilder.Entity<Visit>().Property(v => v.TokenHash).HasMaxLength(64);
        modelBuilder.Entity<Pass>().Property(p => p.Code).HasMaxLength(6);
        modelBuilder.Entity<ProblemReport>().Property(r => r.Description).HasMaxLength(500);
        modelBuilder.Entity<ProblemReport>().Property(r => r.ResolutionNotes).HasMaxLength(500);

        // Staff: usernames are unique. No staff are seeded: the first
        // admin is created with "dotnet run -- create-admin" (see README).
        modelBuilder.Entity<Staff>().HasIndex(s => s.Username).IsUnique();
        modelBuilder.Entity<Staff>().Property(s => s.Username).HasMaxLength(50);
        modelBuilder.Entity<Staff>().Property(s => s.FullName).HasMaxLength(100);
        modelBuilder.Entity<Staff>().Property(s => s.Email).HasMaxLength(200);
        modelBuilder.Entity<Staff>().Property(s => s.Mobile).HasMaxLength(30);

        // Audit log: searched by time, by staff member and by what was changed.
        modelBuilder.Entity<AuditEntry>().HasIndex(a => a.At);
        modelBuilder.Entity<AuditEntry>().HasIndex(a => a.StaffId);
        modelBuilder.Entity<AuditEntry>().HasIndex(a => new { a.TargetType, a.TargetId });
        modelBuilder.Entity<AuditEntry>().Property(a => a.ActorName).HasMaxLength(100);
        modelBuilder.Entity<AuditEntry>().Property(a => a.Action).HasMaxLength(50);
        modelBuilder.Entity<AuditEntry>().Property(a => a.TargetType).HasMaxLength(50);
        modelBuilder.Entity<AuditEntry>().Property(a => a.TargetId).HasMaxLength(50);
        modelBuilder.Entity<AuditEntry>().Property(a => a.Details).HasMaxLength(1000);
        modelBuilder.Entity<AuditEntry>().Property(a => a.IpAddress).HasMaxLength(45);

        // ---- Sample data so the API returns something on first run --------------

        // 12 PCs, matching the Figma design.
        modelBuilder.Entity<Computer>().HasData(
            Enumerable.Range(1, 12).Select(i => new Computer { Id = i, Name = $"PC-{i:00}" }));

        modelBuilder.Entity<Peripheral>().HasData(
            new Peripheral { Id = 1, Name = "USB Mouse", Type = PeripheralType.Mouse, ComputerId = 1 },
            new Peripheral { Id = 2, Name = "Keyboard", Type = PeripheralType.Keyboard, ComputerId = 1 },
            new Peripheral { Id = 3, Name = "Headset A", Type = PeripheralType.Headset, ComputerId = 1 },
            new Peripheral { Id = 4, Name = "USB Mouse", Type = PeripheralType.Mouse, ComputerId = 2 },
            new Peripheral { Id = 5, Name = "Drawing Tablet", Type = PeripheralType.DrawingTablet },
            new Peripheral { Id = 6, Name = "Desk Microphone", Type = PeripheralType.Microphone });

        // One open problem, so PC-03 shows as Maintenance (like the old "NotWorking" sample).
        modelBuilder.Entity<ProblemReport>().HasData(
            new ProblemReport
            {
                Id = 1,
                ComputerId = 3,
                Description = "Monitor flickers and turns off.",
                CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            });
    }
}
