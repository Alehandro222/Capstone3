namespace TrackerApi.Models;

// DTOs = Data Transfer Objects: the shapes we send to / receive from the frontend.
// A C# "record" is a short way to declare an immutable data class
// (very similar to Java records). We use them so we never send entities
// directly, which avoids circular JSON (Computer -> Peripheral -> Computer...).

// ---- Responses ----
public record PeripheralDto(
    int Id,
    string Name,
    PeripheralType Type,
    int? ComputerId,
    bool IsInUse,
    DateTime? InUseSince);

public record ComputerDto(
    int Id,
    string Name,
    ComputerStatus Status,
    string? CurrentUser,
    string? CurrentPurpose,
    List<PeripheralDto> Peripherals);

public record SessionDto(
    int Id,
    int ComputerId,
    string UserName,
    string Purpose,
    DateTime StartedAt,
    DateTime? EndedAt);

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

public record StartSessionRequest(int ComputerId, string UserName, string Purpose);

public record CreatePeripheralRequest(string Name, PeripheralType Type, int? ComputerId);
public record AssignPeripheralRequest(int? ComputerId);
public record UsePeripheralRequest(bool InUse);

public record CreateAssistRequest(int ComputerId, string Message);
public record UpdateAssistStatusRequest(AssistStatus Status);
