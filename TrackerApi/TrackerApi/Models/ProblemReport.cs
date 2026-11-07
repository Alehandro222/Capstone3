namespace TrackerApi.Models;

public enum ProblemStatus
{
    Open,
    Resolved
}

// Something wrong with a PC. While a report is open the PC shows as Maintenance
// and visitors cannot pick it. Resolving it puts the PC back in service.
public class ProblemReport
{
    public int Id { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    public string Description { get; set; } = string.Empty;
    public ProblemStatus Status { get; set; } = ProblemStatus.Open;

    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}
