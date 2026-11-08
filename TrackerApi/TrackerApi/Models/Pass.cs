namespace TrackerApi.Models;

public enum PassStatus
{
    Pending,   // PC reserved, waiting for staff to scan the pass
    Used,      // scanned: a session was started with it
    Expired,   // not scanned in time
    Cancelled  // the visitor chose a different PC
}

// A PC reservation that belongs to a visit. The phone shows it as a QR code
// (the visit's token) plus a 6-digit backup code. Staff scan it at the help
// desk to start the session.
public class Pass
{
    public int Id { get; set; }

    public int VisitId { get; set; }
    public Visit? Visit { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    // 6-digit code staff can type if the QR code will not scan.
    public string Code { get; set; } = string.Empty;

    public PassStatus Status { get; set; } = PassStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public UsageSession? Session { get; set; } // the session started with this pass
}
