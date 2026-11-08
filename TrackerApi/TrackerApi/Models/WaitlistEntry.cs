namespace TrackerApi.Models;

public enum WaitlistStatus
{
    Waiting,
    Served,  // got a PC
    Left,    // gave up
    Expired, // waited too long without getting a PC
    Missed   // a PC was held for them, but they did not pick it in time
}

// A registered visitor waiting because no PC is free. The line is first come,
// first served: when a PC frees up it is held for the first person waiting
// (an "offer"), and nobody else can take it until the offer runs out.
// While anyone is waiting, sessions cannot be extended.
public class WaitlistEntry
{
    public int Id { get; set; }

    public int VisitId { get; set; }
    public Visit? Visit { get; set; }

    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Set when a free PC is held for this person: they must pick it before this time.
    public DateTime? OfferExpiresAt { get; set; }

    public DateTime? EndedAt { get; set; }
}
