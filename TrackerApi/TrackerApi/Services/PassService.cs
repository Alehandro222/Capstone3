using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// PC passes: a registered visitor picks a PC on their phone, then staff scan
// the pass at the help desk to start the session.
public class PassService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly ComputerStatusService _status;
    private readonly SessionService _sessions;
    private readonly VisitService _visits;
    private readonly WaitlistService _waitlist;

    public PassService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options,
        ComputerStatusService status, SessionService sessions, VisitService visits, WaitlistService waitlist)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _status = status;
        _sessions = sessions;
        _visits = visits;
        _waitlist = waitlist;
    }

    // ---- Visitor: pick a PC --------------------------------------------------

    // Reserves a PC for this visit. Picking again ("Choose a different PC")
    // gives back the PC picked before.
    public async Task<VisitDto> PickAsync(string token, int computerId)
    {
        var visit = await _visits.FindByTokenAsync(token);
        var now = _clock.UtcNow();

        // One all-or-nothing step: check the PC is free, then reserve it.
        // Two phones tapping the same PC at the same moment cannot both win.
        await using var tx = await _db.Database.BeginTransactionAsync();
        await ExpireStaleAsync(_db, now);

        if (await _db.Sessions.AnyAsync(s => s.VisitId == visit.Id && s.EndedAt == null))
            throw TrackerException.Conflict("You are already using a computer.");

        var previous = await _db.Passes.FirstOrDefaultAsync(p => p.VisitId == visit.Id && p.Status == PassStatus.Pending);
        if (previous is not null)
        {
            previous.Status = PassStatus.Cancelled;
            await _db.SaveChangesAsync();
        }

        var status = await _status.GetAsync(computerId);
        if (status != ComputerStatus.Available)
            throw TrackerException.Conflict(status is ComputerStatus.Reserved or ComputerStatus.InUse
                ? "That computer was just taken. Please choose another."
                : $"That computer is {SessionService.Describe(status)}. Please choose another.");

        // First come, first served: free PCs are held for the people waiting.
        await _waitlist.EnsureMayPickAsync(visit);
        await _waitlist.MarkServedAsync(visit, now);

        _db.Passes.Add(new Pass
        {
            Visit = visit,
            ComputerId = computerId,
            Code = await NewUniqueCodeAsync(),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.PassLifetimeMinutes)
        });
        await _db.SaveOrConflictAsync("That computer was just taken. Please choose another.");
        await tx.CommitAsync();

        return await _visits.ToVisitorDtoAsync(visit);
    }

    // "Cancel": gives the PC back.
    public async Task<VisitDto> CancelAsync(string token)
    {
        var visit = await _visits.FindByTokenAsync(token, mustBeCurrent: false);
        var now = _clock.UtcNow();
        var pass = await _db.Passes.FirstOrDefaultAsync(p =>
                       p.VisitId == visit.Id && p.Status == PassStatus.Pending && p.ExpiresAt > now)
                   ?? throw TrackerException.Conflict("You have no PC reserved.");

        pass.Status = PassStatus.Cancelled;
        await _db.SaveChangesAsync();
        return await _visits.ToVisitorDtoAsync(visit);
    }

    // ---- Staff ---------------------------------------------------------------

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
        var session = _sessions.Begin(pass.ComputerId, pass.Visit!, pass, now);
        _db.Sessions.Add(session);

        await _db.SaveOrConflictAsync($"{pass.Computer!.Name} already has an active session.");
        await tx.CommitAsync();

        return await _sessions.GetAsync(session.Id);
    }

    public async Task<List<StaffPassDto>> ListAsync(PassStatus? status, int limit)
    {
        var now = _clock.UtcNow();
        var query = WithDetails();
        if (status is not null) query = query.Where(p => p.Status == status);

        var passes = await query.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).ToListAsync();
        return passes.Select(p => ToStaffDto(p, now)).ToList();
    }

    // ---- Cleanup ---------------------------------------------------------------

    // Marks unscanned passes past their expiry as Expired. Returns how many.
    public Task<int> ExpireOldAsync() => ExpireStaleAsync(_db, _clock.UtcNow());

    // Also run at the start of every reservation, so an old unscanned pass
    // never blocks a PC while waiting for the cleanup job.
    public static async Task<int> ExpireStaleAsync(TrackerDbContext db, DateTime now)
    {
        var old = await db.Passes
            .Where(p => p.Status == PassStatus.Pending && p.ExpiresAt <= now)
            .ToListAsync();
        foreach (var pass in old) pass.Status = PassStatus.Expired;
        await db.SaveChangesAsync();
        return old.Count;
    }

    // An unscanned pass past its expiry counts as Expired even before the cleanup job marks it.
    public static PassStatus EffectiveStatus(Pass pass, DateTime now) =>
        pass.Status == PassStatus.Pending && pass.ExpiresAt <= now ? PassStatus.Expired : pass.Status;

    // ---- Helpers -----------------------------------------------------------------

    private IQueryable<Pass> WithDetails() => _db.Passes
        .Include(p => p.Computer)
        .Include(p => p.Session)
        .Include(p => p.Visit!).ThenInclude(v => v.Sectors)
        .Include(p => p.Visit!).ThenInclude(v => v.Services);

    // Staff find a pass by the QR code (the visit's token) or by its 6-digit code.
    private async Task<Pass> FindForStaffAsync(string? token, string? code)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            var hash = Secrets.Hash(token.Trim());
            return await WithDetails()
                       .Where(p => p.Visit!.TokenHash == hash)
                       .OrderByDescending(p => p.Id)
                       .FirstOrDefaultAsync()
                   ?? throw TrackerException.NotFound("This visitor has not picked a PC yet.");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            code = code.Trim();
            return await WithDetails().FirstOrDefaultAsync(p => p.Status == PassStatus.Pending && p.Code == code)
                   ?? throw TrackerException.NotFound("No waiting pass has that code. It may have expired.");
        }

        throw TrackerException.BadRequest("Scan the QR code or type the 6-digit code.");
    }

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

    private static StaffPassDto ToStaffDto(Pass p, DateTime now) => new(
        p.Id, EffectiveStatus(p, now), p.Code, p.ComputerId, p.Computer?.Name ?? string.Empty,
        p.CreatedAt, p.ExpiresAt, p.UsedAt, p.Session?.Id, VisitService.ToStaffDto(p.Visit!));
}
