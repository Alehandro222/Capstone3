namespace TrackerApi.Models;

public enum SessionEndReason
{
    Visitor,   // the visitor pressed End session / Log out
    Staff,     // ended from the staff dashboard
    Idle,      // the kiosk logged them out after no keyboard or mouse use
    TimeLimit, // the 3-hour limit (plus any extensions) ran out
    System     // anything else
}

// One row = one person using one computer for a period of time.
// EndedAt == null means the session is still active.
// The database allows only one active session per computer (see TrackerDbContext).
public class UsageSession
{
    public int Id { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    // The pass this session was started with (null for the old kiosk login form).
    public int? PassId { get; set; }
    public Pass? Pass { get; set; }

    public string UserName { get; set; } = string.Empty; // who is using it
    public string Purpose { get; set; } = string.Empty;  // what they are using it for

    public DateTime StartedAt { get; set; }
    public DateTime ScheduledEndAt { get; set; } // moves later with each extension
    public int ExtensionCount { get; set; }

    public DateTime? EndedAt { get; set; }
    public SessionEndReason? EndReason { get; set; }
}
