namespace TrackerApi.Models;

public enum ReportKind { Day, Week, Month }

public enum RunStatus { Queued, Running, Done, Failed }

// One generated report, kept after it is done. Holds counts and the written text only,
// never a name, so it can stay after the visit records are deleted (1 year).
public class ReportRun
{
    public int Id { get; set; }
    public ReportKind Kind { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public RunStatus Status { get; set; }

    // What the job is doing now ("Asking Gemini for a draft"), shown while it runs.
    public string Step { get; set; } = "Waiting";

    // Why the written summary is missing, or why the run failed.
    public string? Note { get; set; }
    public string? Model { get; set; }

    // Exactly what was sent to Gemini (null when nothing was sent).
    public string? Facts { get; set; }

    // The finished report as JSON (ReportDocumentDto).
    public string? Document { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
