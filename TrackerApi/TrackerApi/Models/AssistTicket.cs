namespace TrackerApi.Models;

public enum AssistStatus
{
    Open,
    InProgress,
    Resolved
}

// The "assist" feature: a user at a computer asks staff for help.
public class AssistTicket
{
    public int Id { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    public string Message { get; set; } = string.Empty;
    public AssistStatus Status { get; set; } = AssistStatus.Open;

    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
