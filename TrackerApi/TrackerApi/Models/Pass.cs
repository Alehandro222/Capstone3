namespace TrackerApi.Models;

public enum PassStatus
{
    Pending,   // issued, waiting for staff to scan it
    Used,      // scanned: a session was started with it
    Expired,   // not scanned in time
    Cancelled  // the visitor chose a different PC
}

public enum Purpose
{
    Schoolwork,
    JobSearch,
    GovernmentServices,
    Other
}

public enum Sex
{
    Male,
    Female
}

// The 10 sectors from the registration form. A visitor can belong to several.
public enum Sector
{
    Nga,
    Lgu,
    Academe,
    Youth,
    SeniorCitizen,
    Msme,
    IndigenousPeople,
    Pwd,
    InformalSector,
    Others
}

// A pass is one registration: who the visitor is, and which PC they picked.
// The phone shows it as a QR code (the token) plus a 6-digit backup code.
// Walk-ins registered by staff also get a pass, already marked Used.
public class Pass
{
    public int Id { get; set; }

    // SHA-256 of the secret token in the QR code. The token itself is never stored,
    // so someone who copies the database still cannot use the passes in it.
    public string TokenHash { get; set; } = string.Empty;

    // 6-digit code staff can type if the QR code will not scan.
    public string Code { get; set; } = string.Empty;

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int Age { get; set; }
    public Sex? Sex { get; set; } // optional on the form
    public Purpose Purpose { get; set; }
    public List<PassSector> Sectors { get; set; } = new();

    public PassStatus Status { get; set; } = PassStatus.Pending;
    public bool IsWalkIn { get; set; } // registered at the desk by staff

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    public UsageSession? Session { get; set; } // the session started with this pass
}

// One row per sector a visitor ticked (a "join table").
public class PassSector
{
    public int PassId { get; set; }
    public Pass? Pass { get; set; }
    public Sector Sector { get; set; }
}
