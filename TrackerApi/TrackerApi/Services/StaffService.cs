using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Staff accounts: passwords, sign-in checks, lockout and admin changes.
public class StaffService
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(15);
    public const int MinPasswordLength = 8;

    // ASP.NET Core's built-in hasher: PBKDF2 with a random salt per password.
    private static readonly PasswordHasher<Staff> Hasher = new();

    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;

    public StaffService(TrackerDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    // ---- Signing in --------------------------------------------------------

    public record LoginResult(Staff? Staff, string? Error);

    // Returns the staff member, or a message to show. The message never says
    // whether the username exists, so it cannot be used to discover accounts.
    public async Task<LoginResult> CheckPasswordAsync(string? username, string? password)
    {
        const string wrong = "Wrong username or password.";
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return new(null, wrong);

        var staff = await _db.Staff.FirstOrDefaultAsync(s => s.Username == username.Trim().ToLower());
        if (staff is null || !staff.IsActive) return new(null, wrong);

        var now = _clock.UtcNow();
        if (staff.LockedUntil > now)
            return new(staff, "Too many wrong passwords. Try again in a few minutes or ask an admin to unlock the account.");

        var result = Hasher.VerifyHashedPassword(staff, staff.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            staff.FailedLoginCount++;
            if (staff.FailedLoginCount >= MaxFailedLogins)
            {
                staff.LockedUntil = now + LockoutTime;
                staff.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync();
            return new(staff, wrong);
        }

        // Older hash format: store it again with the current settings.
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            staff.PasswordHash = Hasher.HashPassword(staff, password);

        await MarkSignedInAsync(staff);
        return new(staff, null);
    }

    public async Task MarkSignedInAsync(Staff staff)
    {
        staff.FailedLoginCount = 0;
        staff.LockedUntil = null;
        staff.LastLoginAt = _clock.UtcNow();
        await _db.SaveChangesAsync();
    }

    public async Task ChangeOwnPasswordAsync(int staffId, string? currentPassword, string? newPassword)
    {
        var staff = await GetEntityAsync(staffId);
        if (currentPassword is null ||
            Hasher.VerifyHashedPassword(staff, staff.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw TrackerException.BadRequest("Your current password is wrong.");

        staff.PasswordHash = Hasher.HashPassword(staff, CheckPassword(newPassword));
        await _db.SaveChangesAsync();
    }

    // ---- Admin -------------------------------------------------------------

    public async Task<List<StaffDto>> ListAsync() =>
        (await _db.Staff.OrderBy(s => s.FullName).ToListAsync()).Select(ToDto).ToList();

    public async Task<StaffDto> GetAsync(int id) => ToDto(await GetEntityAsync(id));

    public async Task<StaffDto> CreateAsync(CreateStaffRequest request)
    {
        var username = CheckUsername(request.Username);
        var fullName = CheckFullName(request.FullName);
        if (request.Role is null || !Enum.IsDefined(request.Role.Value))
            throw TrackerException.BadRequest("Choose a role: Staff or Admin.");
        if (await _db.Staff.AnyAsync(s => s.Username == username))
            throw TrackerException.Conflict("That username is taken.");

        var staff = new Staff
        {
            Username = username,
            FullName = fullName,
            Role = request.Role.Value,
            Email = Clean(request.Email, 200),
            Mobile = Clean(request.Mobile, 30),
            CreatedAt = _clock.UtcNow()
        };
        staff.PasswordHash = Hasher.HashPassword(staff, CheckPassword(request.Password));

        _db.Staff.Add(staff);
        await _db.SaveChangesAsync();
        return ToDto(staff);
    }

    public async Task<StaffDto> UpdateAsync(int id, UpdateStaffRequest request)
    {
        var staff = await GetEntityAsync(id);

        var newRole = request.Role ?? staff.Role;
        var newActive = request.IsActive ?? staff.IsActive;
        if (!Enum.IsDefined(newRole)) throw TrackerException.BadRequest("Unknown role.");

        // Never leave the library without an admin who can sign in.
        var losesAdmin = staff.Role == StaffRole.Admin && staff.IsActive &&
                         (newRole != StaffRole.Admin || !newActive);
        if (losesAdmin && !await _db.Staff.AnyAsync(s => s.Id != id && s.Role == StaffRole.Admin && s.IsActive))
            throw TrackerException.Conflict("There must always be at least one active admin.");

        if (request.FullName is not null) staff.FullName = CheckFullName(request.FullName);
        if (request.Email is not null) staff.Email = Clean(request.Email, 200);
        if (request.Mobile is not null) staff.Mobile = Clean(request.Mobile, 30);
        staff.Role = newRole;
        staff.IsActive = newActive;

        await _db.SaveChangesAsync();
        return ToDto(staff);
    }

    public async Task<StaffDto> SetPasswordAsync(int id, string? password)
    {
        var staff = await GetEntityAsync(id);
        staff.PasswordHash = Hasher.HashPassword(staff, CheckPassword(password));
        staff.FailedLoginCount = 0;
        staff.LockedUntil = null;
        await _db.SaveChangesAsync();
        return ToDto(staff);
    }

    public async Task<StaffDto> UnlockAsync(int id)
    {
        var staff = await GetEntityAsync(id);
        staff.FailedLoginCount = 0;
        staff.LockedUntil = null;
        await _db.SaveChangesAsync();
        return ToDto(staff);
    }

    // Used by "dotnet run -- create-admin".
    public async Task<StaffDto> CreateFirstAdminAsync(string username, string fullName, string password) =>
        await CreateAsync(new CreateStaffRequest(username, fullName, StaffRole.Admin, password, null, null));

    // ---- Helpers -----------------------------------------------------------

    public async Task<Staff> GetEntityAsync(int id) =>
        await _db.Staff.FindAsync(id) ?? throw TrackerException.NotFound("Staff member not found.");

    public static StaffDto ToDto(Staff s) => new(
        s.Id, s.Username, s.FullName, s.Role, s.IsActive, s.Email, s.Mobile,
        s.CreatedAt, s.LastLoginAt, s.LockedUntil);

    private static string CheckUsername(string? username)
    {
        username = username?.Trim().ToLower();
        if (string.IsNullOrEmpty(username) || username.Length is < 3 or > 50 ||
            !username.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw TrackerException.BadRequest("Username: 3 to 50 letters, numbers, dots, dashes or underscores.");
        return username;
    }

    private static string CheckFullName(string? fullName)
    {
        fullName = fullName?.Trim();
        if (string.IsNullOrEmpty(fullName)) throw TrackerException.BadRequest("Enter the person's full name.");
        if (fullName.Length > 100) throw TrackerException.BadRequest("Full name must be 100 characters or fewer.");
        return fullName;
    }

    private static string CheckPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw TrackerException.BadRequest($"Passwords must be at least {MinPasswordLength} characters.");
        if (password.Length > 128) throw TrackerException.BadRequest("Password is too long.");
        return password;
    }

    // Empty text becomes null.
    private static string? Clean(string? text, int max)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > max) throw TrackerException.BadRequest($"Must be {max} characters or fewer.");
        return text;
    }
}
