namespace TrackerApi.Models;

public enum ActorType
{
    Staff,
    Kiosk,  // the kiosk app on a PC
    Visitor,
    System  // e.g. the cleanup job
}

// One line in the audit log: who did what, to what, and when.
// Rows are only ever added, never changed or deleted.
public class AuditEntry
{
    public long Id { get; set; }

    public DateTime At { get; set; }

    public ActorType ActorType { get; set; }
    public int? StaffId { get; set; }                  // set when a staff member did it
    public string ActorName { get; set; } = string.Empty; // copied, so it survives renames

    public string Action { get; set; } = string.Empty;  // e.g. "pass.check_in"
    public string? TargetType { get; set; }             // e.g. "Session"
    public string? TargetId { get; set; }               // e.g. "42"
    public string? Details { get; set; }                // short, human-readable

    public string? IpAddress { get; set; }
}
