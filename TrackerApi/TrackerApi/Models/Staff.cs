namespace TrackerApi.Models;

public enum StaffRole
{
    Staff, // day-to-day work: check-ins, sessions, problem reports
    Admin  // also manages staff accounts, PCs and reads the audit log
}

// A library employee who can sign in to the dashboard.
public class Staff
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty; // stored lowercase
    public string FullName { get; set; } = string.Empty;
    public StaffRole Role { get; set; } = StaffRole.Staff;

    // false = can no longer sign in (accounts are never deleted, for the audit log).
    public bool IsActive { get; set; } = true;

    // A salted, slow hash (PBKDF2); the password itself is never stored.
    public string PasswordHash { get; set; } = string.Empty;

    // Optional contact details, so other staff can reach them.
    public string? Email { get; set; }
    public string? Mobile { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Lockout after too many wrong passwords.
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
}
