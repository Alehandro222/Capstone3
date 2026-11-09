using System;
using System.Collections.Generic;

namespace KioskApp.Core;

// These records mirror the JSON that TrackerApi sends. They are the same
// shapes as the DTOs in TrackerApi/Models/Dtos.cs.

// Worked out by the API (see TrackerApi/Models/Computer.cs).
public enum ComputerStatus
{
    Offline,
    InUse,
    Maintenance,
    Reserved,
    Available
}

public sealed record PeripheralDto(
    int Id,
    string Name,
    string Type,
    int? ComputerId,
    bool IsInUse,
    DateTime? InUseSince);

public sealed record ComputerDto(
    int Id,
    string Name,
    ComputerStatus Status,
    string? CurrentUser,
    string? CurrentPurpose,
    List<PeripheralDto> Peripherals);

public sealed record SessionDto(
    int Id,
    int ComputerId,
    string UserName,
    string Purpose,
    DateTime StartedAt,
    DateTime? EndedAt,
    DateTime ScheduledEndAt = default, // when the time limit ends it (moves when extended)
    int ExtensionCount = 0,
    bool CanExtend = false,           // the tracker's extension rules, worked out on every heartbeat
    string? CannotExtendReason = null)
{
    // The API stores UTC, but SQLite can hand the time back without a "Z",
    // which makes .NET treat it as "unspecified". These always give real UTC.
    public DateTime StartedAtUtc => Utc.Of(StartedAt);
    public DateTime ScheduledEndAtUtc => Utc.Of(ScheduledEndAt);
}

public static class Utc
{
    public static DateTime Of(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc)
    };
}

// The registration form (same fields as the DTC digital login form).
// Values are the API's names, e.g. AgeGroup = "Age18To30", Sectors = ["Youth"].
public sealed record KioskRegistration(
    string FirstName,
    string LastName,
    string AgeGroup,
    string Sex,
    List<string> Sectors,
    List<string> Services);

// From POST api/kiosk/heartbeat: this PC, and its running session (if any).
// From POST api/kiosk/heartbeat: this PC, its running session (if any), and while
// Reserved, when the visitor's pass runs out.
public sealed record KioskStateDto(ComputerDto Computer, SessionDto? Session, DateTime? ReservedUntil = null);

// From POST api/kiosk/pair: which PC this is, and its secret key.
public sealed record KioskPairedDto(int ComputerId, string ComputerName, string KioskKey);

public sealed record AssistDto(
    int Id,
    int ComputerId,
    string ComputerName,
    string Message,
    string Status,
    DateTime CreatedAt,
    DateTime? ResolvedAt);
