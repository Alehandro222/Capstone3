namespace TrackerApi.Models; // like "package" in Java (file-scoped, so no braces needed)

// A computer's status is never stored. The server works it out every time from
// other data (see Services/ComputerStatusService.cs), so it can never be out of date.
// Checked in this order; the first one that matches wins:
public enum ComputerStatus
{
    Offline,     // the kiosk app on the PC has not checked in recently (heartbeat)
    InUse,       // a session is running
    Maintenance, // there is an open problem report
    Reserved,    // someone holds a pass for it that staff have not scanned yet
    Available    // none of the above: a visitor can pick it
}

// An "entity": EF Core maps this class to a database table named Computers.
public class Computer
{
    public int Id { get; set; }                      // primary key by convention
    public string Name { get; set; } = string.Empty; // e.g. "PC-01"

    // Last time the kiosk app on this PC said "I'm alive". null = never.
    public DateTime? LastHeartbeatAt { get; set; }

    // Navigation properties: EF Core uses these to build the relationships.
    public List<Peripheral> Peripherals { get; set; } = new();
    public List<UsageSession> Sessions { get; set; } = new();
    public List<Pass> Passes { get; set; } = new();
    public List<ProblemReport> ProblemReports { get; set; } = new();
}
