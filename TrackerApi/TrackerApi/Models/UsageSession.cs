namespace TrackerApi.Models;

public enum SessionEndReason
{
    Visitor,   // the visitor pressed End session / Log out
    Staff,     // ended from the staff dashboard
    Idle,      // the kiosk logged them out after no keyboard or mouse use
    TimeLimit, // the 3-hour limit (plus any extensions) ran out
    System     // anything else
}

// One row = one visitor using one computer for a period of time.
// EndedAt == null means the session is still active.
// The database allows only one active session per computer, and per visit.
public class UsageSession
{
    public int Id { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    // Who is using it: every session belongs to a registered visit.
    public int VisitId { get; set; }
    public Visit? Visit { get; set; }

    // The pass it was started with (null for walk-ins started by staff).
    public int? PassId { get; set; }
    public Pass? Pass { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime ScheduledEndAt { get; set; } // moves later with each extension
    public int ExtensionCount { get; set; }

    public DateTime? EndedAt { get; set; }
    public SessionEndReason? EndReason { get; set; }
}
