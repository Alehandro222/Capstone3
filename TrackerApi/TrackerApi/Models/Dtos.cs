namespace TrackerApi.Models;

// DTOs = Data Transfer Objects: the shapes we send to / receive from the frontends.
// A C# "record" is a short way to declare an immutable data class
// (very similar to Java records). We use them so we never send entities
// directly, which avoids circular JSON (Computer -> Peripheral -> Computer...)
// and keeps private data (like a visitor's age) out of public responses.
//
// New fields are added at the END of existing records, so the kiosk app and
// dashboard keep working with the shapes they already know.

// ---- Responses ----
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
    string? OpenProblem);

// Public view of a computer for visitors' phones: no names, nothing personal.
public record PublicComputerDto(int Id, string Name, ComputerStatus Status);

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
    string? CannotExtendReason);

// What a visitor's phone sees about its own pass.
public record VisitorPassDto(
    PassStatus Status,
    string Code,
    int ComputerId,
    string ComputerName,
    string FirstName,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    SessionDto? Session);

// Returned once, when the pass is created. The phone must keep the token:
// the server only stores its hash and cannot show it again.
public record CreatedPassDto(string Token, VisitorPassDto Pass);

// What staff see when they scan or type a pass, before checking the visitor in.
public record StaffPassDto(
    int Id,
    PassStatus Status,
    string Code,
    int ComputerId,
    string ComputerName,
    string FirstName,
    string LastName,
    int Age,
    Sex? Sex,
    Purpose Purpose,
    List<Sector> Sectors,
    bool IsWalkIn,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    int? SessionId);

public record ProblemReportDto(
    int Id,
    int ComputerId,
    string ComputerName,
    string Description,
    ProblemStatus Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolutionNotes);

public record WaitlistStatusDto(
    WaitlistStatus Status,
    int? Position, // 1 = next in line; null when no longer waiting
    int AvailableComputers,
    DateTime ExpiresAt);

public record WaitlistJoinedDto(string Token, WaitlistStatusDto Entry);

public record WaitlistEntryDto(
    int Id,
    string? FirstName,
    WaitlistStatus Status,
    int? Position,
    DateTime CreatedAt,
    DateTime ExpiresAt);

public record AssistDto(
    int Id,
    int ComputerId,
    string ComputerName,
    string Message,
    AssistStatus Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt);

// ---- Requests ----
public record CreateComputerRequest(string Name);
public record UpdateComputerStatusRequest(ComputerStatus Status);

// The old kiosk login form (name + purpose typed on the PC).
public record StartSessionRequest(int ComputerId, string UserName, string Purpose);

// The registration form on the phone. Nullable fields are checked by the
// server, so a missing value gets a clear message instead of a silent default.
public record CreatePassRequest(
    int ComputerId,
    string? FirstName,
    string? LastName,
    int? Age,
    Sex? Sex,
    Purpose? Purpose,
    List<Sector>? Sectors,
    string? WaitlistToken); // set when the visitor came from the waitlist

// Staff registering a visitor who has no phone. Starts the session right away.
public record WalkInRequest(
    int ComputerId,
    string? FirstName,
    string? LastName,
    int? Age,
    Sex? Sex,
    Purpose? Purpose,
    List<Sector>? Sectors);

// Staff check-in: the token from the QR code, or the 6-digit code.
public record CheckInRequest(string? Token, string? Code);

public record EndSessionRequest(SessionEndReason? Reason);

public record CreateProblemRequest(int ComputerId, string? Description);
public record ResolveProblemRequest(string? Notes);

public record JoinWaitlistRequest(string? FirstName);

public record CreatePeripheralRequest(string Name, PeripheralType Type, int? ComputerId);
public record AssignPeripheralRequest(int? ComputerId);
public record UsePeripheralRequest(bool InUse);

public record CreateAssistRequest(int ComputerId, string Message);
public record UpdateAssistStatusRequest(AssistStatus Status);
