using Microsoft.EntityFrameworkCore;
using TrackerApi.Models;

namespace TrackerApi.Data;

// The DbContext is your gateway to the database (like JPA's EntityManager).
// Each DbSet<T> is a table.
public class TrackerDbContext : DbContext
{
    public TrackerDbContext(DbContextOptions<TrackerDbContext> options) : base(options)
    {
    }

    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<Peripheral> Peripherals => Set<Peripheral>();
    public DbSet<UsageSession> Sessions => Set<UsageSession>();
    public DbSet<AssistTicket> AssistTickets => Set<AssistTicket>();

    // Sample data so the API returns something on first run.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Computer>().HasData(
            new Computer { Id = 1, Name = "PC-01" },
            new Computer { Id = 2, Name = "PC-02" },
            new Computer { Id = 3, Name = "PC-03", Status = ComputerStatus.NotWorking });

        modelBuilder.Entity<Peripheral>().HasData(
            new Peripheral { Id = 1, Name = "USB Mouse", Type = PeripheralType.Mouse, ComputerId = 1 },
            new Peripheral { Id = 2, Name = "Keyboard", Type = PeripheralType.Keyboard, ComputerId = 1 },
            new Peripheral { Id = 3, Name = "Headset A", Type = PeripheralType.Headset, ComputerId = 1 },
            new Peripheral { Id = 4, Name = "USB Mouse", Type = PeripheralType.Mouse, ComputerId = 2 },
            new Peripheral { Id = 5, Name = "Drawing Tablet", Type = PeripheralType.DrawingTablet },
            new Peripheral { Id = 6, Name = "Desk Microphone", Type = PeripheralType.Microphone });
    }
}
