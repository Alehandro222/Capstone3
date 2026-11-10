namespace TrackerApi.Models;

// DTOs = Data Transfer Objects: the shapes we send to / receive from the frontends.
// A C# "record" is a short way to declare an immutable data class
// (very similar to Java records). We use them so we never send entities
// directly, which avoids circular JSON (Computer -> Peripheral -> Computer...)
// and keeps private data (like a visitor's age group) out of public responses.

// ======================= Responses =======================

public record PeripheralDto(
    int Id,
    string Name,
    PeripheralType Type,
    int? ComputerId,
    bool IsInUse,
    DateTime? InUseSince);

// Staff view of a computer.
public record ComputerDto(
    int Id,
    string Name,
    ComputerStatus Status,
    string? CurrentUser,
    string? CurrentPurpose,
    List<PeripheralDto> Peripherals,
    DateTime? LastHeartbeatAt,
    int? ActiveSessionId,
    DateTime? SessionEndsAt,
    string? OpenProblem,
    DateTime? PairedAt); // when its kiosk app was paired; null = not paired

// Public view of a computer for visitors' phones: no names, nothing personal.
public record PublicComputerDto(int Id, string Name, ComputerStatus Status);

// UserName and Purpose come from the visit (full name; services availed).
public record SessionDto(
    int Id,
    int ComputerId,
    string UserName,
    string Purpose,
    DateTime StartedAt,
    DateTime? EndedAt,
    DateTime ScheduledEndAt,
    int ExtensionCount,
    SessionEndReason? EndReason,
    int? PassId,
    bool CanExtend,
    string? CannotExtendReason,
    int VisitId);

// ---- Visitor's phone ----

// A PC reservation, as the phone shows it (QR = the visit token, plus the code).
public record PassDto(
    PassStatus Status,
    string Code,
    int ComputerId,
    string ComputerName,
    DateTime CreatedAt,
    DateTime ExpiresAt);

public record WaitlistStatusDto(
    WaitlistStatus Status,
    int? Position, // 1 = next in line; null when no longer waiting
    int AvailableComputers,
    DateTime ExpiresAt,
    DateTime? ComputerReadyUntil); // set when a free PC is held for them: pick before this time

// Everything the phone needs about its own visit. The phone checks this every few seconds.
public record VisitDto(
    string FirstName,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    PassDto? Pass,               // the latest PC reservation, if any
    SessionDto? Session,         // the running (or last) session, if any
    WaitlistStatusDto? Waitlist); // only while waiting

// Returned once, when the visitor registers. The phone must keep the token:
// the server only stores its hash and cannot show it again.
public record CreatedVisitDto(string Token, VisitDto Visit);

// ---- Staff ----

// A visit with the visitor's details: the daily log, and what staff see on check-in.
public record StaffVisitDto(
    int Id,
    string FirstName,
    string LastName,
    AgeGroup AgeGroup,
    Sex Sex,
    List<Sector> Sectors,
    List<ServiceAvailed> Services,
    VisitSource Source,
    DateTime CreatedAt);

public record StaffPassDto(
    int Id,
    PassStatus Status,
    string Code,
    int ComputerId,
    string ComputerName,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    int? SessionId,
    StaffVisitDto Visit);

// A walk-in: the visit, and its session when staff also gave them a PC.
public record WalkInResultDto(StaffVisitDto Visit, SessionDto? Session);

public record WaitlistEntryDto(
    int Id,
    int VisitId,
    string FirstName,
    WaitlistStatus Status,
    int? Position,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? ComputerReadyUntil);

public record ProblemReportDto(
    int Id,
    int ComputerId,
    string ComputerName,
    string Description,
    ProblemStatus Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolutionNotes);

public record StaffDto(
    int Id,
    string Username,
    string FullName,
    StaffRole Role,
    bool IsActive,
    string? Email,
    string? Mobile,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    DateTime? LockedUntil);

public record AuditEntryDto(
    long Id,
    DateTime At,
    ActorType ActorType,
    int? StaffId,
    string ActorName,
    string Action,
    string? TargetType,
    string? TargetId,
    string? Details,
    string? IpAddress);

public record AssistDto(
    int Id,
    int ComputerId,
    string ComputerName,
    string Message,
    AssistStatus Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt);

// ---- Reports (aggregated counts only; see ReportService) ----

public record CountDto(string Key, int Count);

// One day (in the center's time zone).
public record ReportDayDto(DateOnly Day, int Visits, int Sessions, double SessionHours);

public record ReportComputerDto(int ComputerId, string Name, int Sessions, double Hours);

public record ReportWaitlistDto(int Joined, int GotAComputer, int MissedTurn, int Expired, int Left);

// Everything the Reports page shows for a range of days. Counts only: no names.
//   VisitsByHour / SessionsByHour: 24 entries, "0".."23" = hour of the day the visit / session started.
//   Breakdowns list every possible answer (also those with 0) in the form's order.
//   A visit can tick several sectors and services, so those add up to more than Visits.
public record ReportDto(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    int Visits,
    int VisitsWithComputer,
    int Sessions,
    int RunningSessions,
    double SessionHours,
    double AverageSessionMinutes,
    int Extensions,
    List<ReportDayDto> ByDay,
    List<CountDto> VisitsByHour,
    List<CountDto> SessionsByHour,
    List<CountDto> ByAgeGroup,
    List<CountDto> BySex,
    List<CountDto> BySector,
    List<CountDto> ByService,
    List<CountDto> BySource,
    List<CountDto> SessionEndReasons,
    List<ReportComputerDto> ByComputer,
    ReportWaitlistDto Waitlist);

// ---- Kiosk app ----

// Shown once to an admin: staff type the code into the PC's setup screen.
public record PairingCodeDto(int ComputerId, string ComputerName, string Code, DateTime ExpiresAt);

// Returned once to the kiosk app when it pairs. It saves the key and sends it on every request.
public record KioskPairedDto(int ComputerId, string ComputerName, string KioskKey);

// What the kiosk app needs to draw its screens: its own PC, and its own session if any.
// ReservedUntil: while a visitor holds an unscanned pass for this PC, when that pass expires.
public record KioskStateDto(ComputerDto Computer, SessionDto? Session, DateTime? ReservedUntil = null);

// ======================= Requests =======================

// The registration form (same fields as the DTC digital login form). Fields are
// nullable so a missing answer gets a clear message instead of a silent default.
public record RegistrationRequest(
    string? FirstName,
    string? LastName,
    AgeGroup? AgeGroup,
    Sex? Sex,
    List<Sector>? Sectors,
    List<ServiceAvailed>? Services);

// Staff registering a visitor at the desk. With a computerId the session starts
// right away; without one the visit is only logged (Wi-Fi, co-working, ...).
public record WalkInRequest(
    string? FirstName,
    string? LastName,
    AgeGroup? AgeGroup,
    Sex? Sex,
    List<Sector>? Sectors,
    List<ServiceAvailed>? Services,
    int? ComputerId)
{
    public RegistrationRequest Registration => new(FirstName, LastName, AgeGroup, Sex, Sectors, Services);
}

public record PickComputerRequest(int ComputerId);

// Staff check-in: the token from the QR code, or the 6-digit code.
public record CheckInRequest(string? Token, string? Code);

public record EndSessionRequest(SessionEndReason? Reason);

public record CreateComputerRequest(string Name);

public record CreateProblemRequest(int ComputerId, string? Description);
public record ResolveProblemRequest(string? Notes);

public record CreatePeripheralRequest(string Name, PeripheralType Type, int? ComputerId);
public record AssignPeripheralRequest(int? ComputerId);
public record UsePeripheralRequest(bool InUse);

public record LoginRequest(string? Username, string? Password);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

// Admin: create or edit a staff account.
public record CreateStaffRequest(
    string? Username,
    string? FullName,
    StaffRole? Role,
    string? Password,
    string? Email,
    string? Mobile);

public record UpdateStaffRequest(string? FullName, StaffRole? Role, bool? IsActive, string? Email, string? Mobile);
public record SetPasswordRequest(string? Password);

public record CreateAssistRequest(int ComputerId, string Message);
public record UpdateAssistStatusRequest(AssistStatus Status);

public record PairKioskRequest(string? Code);
public record KioskHelpRequest(string? Message);
