namespace TrackerApi.Models;

// One row = one person using one computer for a period of time.
// EndedAt == null means the session is still active.
public class UsageSession
{
    public int Id { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    public string UserName { get; set; } = string.Empty; // who is using it
    public string Purpose { get; set; } = string.Empty;  // what they are using it for

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}
