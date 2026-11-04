namespace TrackerApi.Models; // like "package" in Java (file-scoped, so no braces needed)

public enum ComputerStatus
{
    Available,
    InUse,
    NotWorking
}

// An "entity": EF Core maps this class to a database table named Computers.
public class Computer
{
    public int Id { get; set; }                      // primary key by convention
    public string Name { get; set; } = string.Empty; // e.g. "PC-01"
    public ComputerStatus Status { get; set; } = ComputerStatus.Available;

    // Navigation properties: EF Core uses these to build the relationships.
    public List<Peripheral> Peripherals { get; set; } = new();
    public List<UsageSession> Sessions { get; set; } = new();
}
