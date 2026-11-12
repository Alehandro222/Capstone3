namespace TrackerApi.Models;

// The registration form, matching the DTC Baguio digital login form.

public enum AgeGroup
{
    Age17AndUnder,
    Age18To30,
    Age31To59,
    Age60AndAbove
}

public enum Sex
{
    Male,
    Female
}

// A visitor can belong to several sectors.
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

// "Purpose / Services Availed": a visitor can tick several.
public enum ServiceAvailed
{
    Connectivity,
    IctEquipment,   // "Use of ICT Equipment": the library PCs
    CoWorkingSpace,
    EgovServices,
    Others
}

// Where the visit was registered.
public enum VisitSource
{
    Phone, // the visitor, on their own phone (entrance QR code)
    Desk,  // staff, on the dashboard (visitor without a phone)
    Kiosk  // staff, on the PC itself (staff-only form behind the PIN)
}

// One row per visit to the center: the digital version of the DTC login form.
// A visit does not need a PC (Wi-Fi, co-working space, eGov help...). When the
// visitor wants one, a Pass reserves it and a UsageSession records its use.
public class Visit
{
    public int Id { get; set; }

    // SHA-256 of the secret token the visitor's phone keeps. The phone uses it to
    // pick a PC, show its QR pass, join the waitlist and end its session.
    // Desk and kiosk visits get one too, but it is never shown to anyone.
    public string TokenHash { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // DTC keeps visitors' names for 1 year (Tracker:KeepVisitorNamesDays). After that the
    // cleanup job blanks them and sets this; the anonymous answers stay for the reports.
    public DateTime? NamesRemovedAt { get; set; }
    public AgeGroup AgeGroup { get; set; }
    public Sex Sex { get; set; }
    public List<VisitSector> Sectors { get; set; } = new();
    public List<VisitServiceAvailed> Services { get; set; } = new();

    public VisitSource Source { get; set; }
    public DateTime CreatedAt { get; set; }

    // After this the phone can no longer manage the visit (register again next time).
    public DateTime ExpiresAt { get; set; }

    public List<Pass> Passes { get; set; } = new();
    public List<UsageSession> Sessions { get; set; } = new();
    public List<WaitlistEntry> WaitlistEntries { get; set; } = new();

    public const string RemovedName = "(name removed)";

    public string FullName => NamesRemovedAt is null ? $"{FirstName} {LastName}" : RemovedName;
}

// One row per sector ticked (a "join table").
public class VisitSector
{
    public int VisitId { get; set; }
    public Visit? Visit { get; set; }
    public Sector Sector { get; set; }
}

// One row per service ticked.
public class VisitServiceAvailed
{
    public int VisitId { get; set; }
    public Visit? Visit { get; set; }
    public ServiceAvailed Service { get; set; }
}
