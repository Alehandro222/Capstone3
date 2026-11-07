namespace TrackerApi.Models;

public enum WaitlistStatus
{
    Waiting,
    Served,  // got a PC
    Left,    // gave up
    Expired  // waited too long without getting a PC
}

// A visitor waiting because no PC is free. While anyone is waiting,
// sessions cannot be extended.
public class WaitlistEntry
{
    public int Id { get; set; }

    // SHA-256 of the secret token the phone keeps (same idea as passes).
    public string TokenHash { get; set; } = string.Empty;

    public string? FirstName { get; set; } // optional, so staff can call them

    public WaitlistStatus Status { get; set; } = WaitlistStatus.Waiting;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
