using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Issuing passes, letting visitors manage theirs, and staff check-in.
public class PassService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly ComputerStatusService _status;
    private readonly SessionService _sessions;
    private readonly WaitlistService _waitlist;

    public PassService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options,
        ComputerStatusService status, SessionService sessions, WaitlistService waitlist)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _status = status;
        _sessions = sessions;
        _waitlist = waitlist;
    }

    // ---- Visitor: get a pass -----------------------------------------------

    public async Task<CreatedPassDto> CreateAsync(CreatePassRequest request)
    {
        var visitor = Validate(request.FirstName, request.LastName, request.Age, request.Sex, request.Purpose, request.Sectors);
        var now = _clock.UtcNow();

        // One all-or-nothing step: check the PC is free, then reserve it.
        // Two phones tapping the same PC at the same moment cannot both win.
        await using var tx = await _db.Database.BeginTransactionAsync();

        await ExpireOldAsync(); // so an old unscanned pass does not block this PC

        var status = await _status.GetAsync(request.ComputerId);
        if (status != ComputerStatus.Available)
            throw TrackerException.Conflict(status is ComputerStatus.Reserved or ComputerStatus.InUse
                ? "That computer was just taken. Please choose another."
                : $"That computer is {SessionService.Describe(status)}. Please choose another.");

        if (!string.IsNullOrWhiteSpace(request.WaitlistToken))
            await _waitlist.MarkServedAsync(request.WaitlistToken, now);

        var token = Secrets.NewToken();
        var pass = NewPass(request.ComputerId, visitor, now);
        pass.TokenHash = Secrets.Hash(token);
        pass.Code = await NewUniqueCodeAsync();
        pass.ExpiresAt = now.AddMinutes(_options.PassLifetimeMinutes);

        _db.Passes.Add(pass);
        await SaveOrConflictAsync("That computer was just taken. Please choose another.");
        await tx.CommitAsync();

        return new CreatedPassDto(token, await GetForVisitorAsync(token));
    }

    // ---- Visitor: their own pass (the phone polls this) ---------------------

    public async Task<VisitorPassDto> GetForVisitorAsync(string token)
    {
        var pass = await FindByTokenAsync(token);
        var now = _clock.UtcNow();

        SessionDto? session = null;
        if (pass.Session is not null)
            session = _sessions.ToDto(pass.Session, now, await _sessions.CountWaitingAsync(now));

        return new VisitorPassDto(EffectiveStatus(pass, now), pass.Code, pass.ComputerId,
            pass.Computer!.Name, pass.FirstName, pass.CreatedAt, pass.ExpiresAt, session);
    }

    // "Choose a different PC": gives the PC back.
    public async Task<VisitorPassDto> CancelAsync(string token)
    {
        var pass = await FindByTokenAsync(token);
        if (EffectiveStatus(pass, _clock.UtcNow()) != PassStatus.Pending)
            throw TrackerException.Conflict("This pass can no longer be cancelled.");

        pass.Status = PassStatus.Cancelled;
        await _db.SaveChangesAsync();
        return await GetForVisitorAsync(token);
    }

    public async Task<VisitorPassDto> EndSessionAsync(string token)
    {
        var session = await ActiveSessionForAsync(token);
        await _sessions.EndAsync(session.Id, SessionEndReason.Visitor);
        return await GetForVisitorAsync(token);
    }

    public async Task<VisitorPassDto> ExtendSessionAsync(string token)
    {
        var session = await ActiveSessionForAsync(token);
        await _sessions.ExtendAsync(session.Id);
        return await GetForVisitorAsync(token);
    }

    // ---- Staff -------------------------------------------------------------

    // Look a pass up before checking the visitor in, to see who it belongs to.
    public async Task<StaffPassDto> LookupAsync(string? token, string? code)
    {
        var pass = await FindForStaffAsync(token, code);
        return ToStaffDto(pass, _clock.UtcNow());
    }

    // The scan at the help desk: checks the pass and starts the session.
    public async Task<SessionDto> CheckInAsync(CheckInRequest request)
    {
        var now = _clock.UtcNow();
        await using var tx = await _db.Database.BeginTransactionAsync();

        var pass = await FindForStaffAsync(request.Token, request.Code);

        switch (pass.Status)
        {
            case PassStatus.Used:
                throw TrackerException.Conflict("This pass has already been used.");
            case PassStatus.Cancelled:
                throw TrackerException.Conflict("This pass was cancelled.");
            case PassStatus.Expired:
                throw TrackerException.Conflict("This pass has expired. Ask the visitor to pick a PC again.");
        }

        if (pass.ExpiresAt <= now)
        {
            pass.Status = PassStatus.Expired;
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            throw TrackerException.Conflict("This pass has expired. Ask the visitor to pick a PC again.");
        }

        // The pass itself makes the PC "Reserved"; anything else is a problem.
        var status = await _status.GetAsync(pass.ComputerId);
        if (status == ComputerStatus.Offline)
            throw TrackerException.Conflict(
                $"{pass.Computer!.Name} is offline. Check that it is on and the kiosk app is running.");
        if (status != ComputerStatus.Reserved)
            throw TrackerException.Conflict($"{pass.Computer!.Name} is {SessionService.Describe(status)}.");

        pass.Status = PassStatus.Used;
        pass.UsedAt = now;
        var session = _sessions.Begin(pass.ComputerId, FullName(pass), PurposeText(pass.Purpose), pass, now);
        _db.Sessions.Add(session);

        await SaveOrConflictAsync($"{pass.Computer!.Name} already has an active session.");
        await tx.CommitAsync();

        return await _sessions.GetAsync(session.Id);
    }

    // A visitor without a phone: staff fill in the form and the session starts right away.
    public async Task<SessionDto> WalkInAsync(WalkInRequest request)
    {
        var visitor = Validate(request.FirstName, request.LastName, request.Age, request.Sex, request.Purpose, request.Sectors);
        var now = _clock.UtcNow();

        await using var tx = await _db.Database.BeginTransactionAsync();
        await ExpireOldAsync();

        var status = await _status.GetAsync(request.ComputerId);
        if (status != ComputerStatus.Available)
            throw TrackerException.Conflict($"That computer is {SessionService.Describe(status)}.");

        var pass = NewPass(request.ComputerId, visitor, now);
        pass.TokenHash = Secrets.Hash(Secrets.NewToken()); // never shown: there is no phone
        pass.Code = Secrets.NewSixDigitCode();
        pass.Status = PassStatus.Used;
        pass.IsWalkIn = true;
        pass.ExpiresAt = now;
        pass.UsedAt = now;

        var session = _sessions.Begin(request.ComputerId, FullName(pass), PurposeText(pass.Purpose), pass, now);
        _db.Passes.Add(pass);
        _db.Sessions.Add(session);

        await SaveOrConflictAsync("That computer was just taken.");
        await tx.CommitAsync();

        return await _sessions.GetAsync(session.Id);
    }

    public async Task<List<StaffPassDto>> ListAsync(PassStatus? status, int limit)
    {
        var now = _clock.UtcNow();
        var query = _db.Passes.Include(p => p.Computer).Include(p => p.Sectors).Include(p => p.Session).AsQueryable();
        if (status is not null) query = query.Where(p => p.Status == status);

        var passes = await query.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).ToListAsync();
        return passes.Select(p => ToStaffDto(p, now)).ToList();
    }

    // ---- Cleanup -----------------------------------------------------------

    // Marks unscanned passes past their expiry as Expired. Returns how many.
    public async Task<int> ExpireOldAsync()
    {
        var now = _clock.UtcNow();
        var old = await _db.Passes
            .Where(p => p.Status == PassStatus.Pending && p.ExpiresAt <= now)
            .ToListAsync();
        foreach (var pass in old) pass.Status = PassStatus.Expired;
        await _db.SaveChangesAsync();
        return old.Count;
    }

    // ---- Helpers -----------------------------------------------------------

    private record Visitor(string FirstName, string LastName, int Age, Sex? Sex, Purpose Purpose, List<Sector> Sectors);

    private static Visitor Validate(string? firstName, string? lastName, int? age, Sex? sex, Purpose? purpose,
        List<Sector>? sectors)
    {
        firstName = firstName?.Trim();
        lastName = lastName?.Trim();

        if (string.IsNullOrEmpty(firstName)) throw TrackerException.BadRequest("Please enter your first name.");
        if (string.IsNullOrEmpty(lastName)) throw TrackerException.BadRequest("Please enter your last name.");
        if (firstName.Length > 100 || lastName.Length > 100)
            throw TrackerException.BadRequest("Names must be 100 characters or fewer.");
        if (age is null or < 1 or > 120) throw TrackerException.BadRequest("Please enter a valid age.");
        if (sex is not null && !Enum.IsDefined(sex.Value)) throw TrackerException.BadRequest("Unknown value for sex.");
        if (purpose is null || !Enum.IsDefined(purpose.Value))
            throw TrackerException.BadRequest("Please choose what you will use the computer for.");
        if (sectors is null || sectors.Count == 0)
            throw TrackerException.BadRequest("Please select at least one sector.");
        if (sectors.Any(s => !Enum.IsDefined(s)))
            throw TrackerException.BadRequest("Unknown sector.");

        return new Visitor(firstName, lastName, age.Value, sex, purpose.Value, sectors.Distinct().ToList());
    }

    private static Pass NewPass(int computerId, Visitor visitor, DateTime now) => new()
    {
        ComputerId = computerId,
        FirstName = visitor.FirstName,
        LastName = visitor.LastName,
        Age = visitor.Age,
        Sex = visitor.Sex,
        Purpose = visitor.Purpose,
        Sectors = visitor.Sectors.Select(s => new PassSector { Sector = s }).ToList(),
        CreatedAt = now
    };

    private async Task<string> NewUniqueCodeAsync()
    {
        // With at most a few dozen pending passes, a clash is very unlikely; retry if it happens.
        for (var i = 0; i < 20; i++)
        {
            var code = Secrets.NewSixDigitCode();
            if (!await _db.Passes.AnyAsync(p => p.Status == PassStatus.Pending && p.Code == code))
                return code;
        }
        throw new InvalidOperationException("Could not generate a unique pass code.");
    }

    private async Task<Pass> FindByTokenAsync(string token)
    {
        var hash = Secrets.Hash(token);
        return await _db.Passes
                   .Include(p => p.Computer)
                   .Include(p => p.Session)
                   .FirstOrDefaultAsync(p => p.TokenHash == hash)
               ?? throw TrackerException.NotFound("Pass not found.");
    }

    // Staff can find a pass by its QR token, or by its 6-digit code while it is still pending.
    private async Task<Pass> FindForStaffAsync(string? token, string? code)
    {
        var query = _db.Passes.Include(p => p.Computer).Include(p => p.Sectors).Include(p => p.Session);

        if (!string.IsNullOrWhiteSpace(token))
        {
            var hash = Secrets.Hash(token.Trim());
            return await query.FirstOrDefaultAsync(p => p.TokenHash == hash)
                   ?? throw TrackerException.NotFound("This QR code is not a valid pass.");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            code = code.Trim();
            return await query.FirstOrDefaultAsync(p => p.Status == PassStatus.Pending && p.Code == code)
                   ?? throw TrackerException.NotFound("No waiting pass has that code. It may have expired.");
        }

        throw TrackerException.BadRequest("Scan the QR code or type the 6-digit code.");
    }

    private async Task<UsageSession> ActiveSessionForAsync(string token)
    {
        var pass = await FindByTokenAsync(token);
        if (pass.Session is null || pass.Session.EndedAt is not null)
            throw TrackerException.Conflict("This pass has no active session.");
        return pass.Session;
    }

    // An unscanned pass past its expiry counts as Expired even before the cleanup job marks it.
    private static PassStatus EffectiveStatus(Pass pass, DateTime now) =>
        pass.Status == PassStatus.Pending && pass.ExpiresAt <= now ? PassStatus.Expired : pass.Status;

    private static StaffPassDto ToStaffDto(Pass p, DateTime now) => new(
        p.Id, EffectiveStatus(p, now), p.Code, p.ComputerId, p.Computer?.Name ?? string.Empty,
        p.FirstName, p.LastName, p.Age, p.Sex, p.Purpose,
        p.Sectors.Select(s => s.Sector).OrderBy(s => s).ToList(),
        p.IsWalkIn, p.CreatedAt, p.ExpiresAt, p.UsedAt, p.Session?.Id);

    private static string FullName(Pass pass) => $"{pass.FirstName} {pass.LastName}";

    public static string PurposeText(Purpose purpose) => purpose switch
    {
        Purpose.Schoolwork => "Schoolwork",
        Purpose.JobSearch => "Job search",
        Purpose.GovernmentServices => "Government services",
        _ => "Other"
    };

    private async Task SaveOrConflictAsync(string message)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw TrackerException.Conflict(message);
        }
    }
}
