namespace TrackerApi.Models;

public enum WaitlistStatus
{
    Waiting,
    Served,  // got a PC
    Left,    // gave up
    Expired  // waited too long without getting a PC
}

// A registered visitor waiting because no PC is free. While anyone is waiting,
// sessions cannot be extended.
public class WaitlistEntry
{
    public int Id { get; set; }

    public int VisitId { get; set; }
    public Visit? Visit { get; set; }

    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
