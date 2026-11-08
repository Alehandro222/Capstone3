using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Registering visits (the DTC digital login), from the phone, the desk or a PC,
// and what the visitor's phone sees about its own visit.
public class VisitService
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly ComputerStatusService _status;
    private readonly SessionService _sessions;
    private readonly WaitlistService _waitlist;

    public VisitService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options,
        ComputerStatusService status, SessionService sessions, WaitlistService waitlist)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _status = status;
        _sessions = sessions;
        _waitlist = waitlist;
    }

    // ---- Registering -------------------------------------------------------

    // The visitor's phone: log the visit. Picking a PC is a separate, optional step.
    public async Task<CreatedVisitDto> RegisterAsync(RegistrationRequest request)
    {
        var (visit, token) = NewVisit(Registration.Check(request), VisitSource.Phone);
        _db.Visits.Add(visit);
        await _db.SaveChangesAsync();
        return new CreatedVisitDto(token, await ToVisitorDtoAsync(visit));
    }

    // Staff at the desk, for a visitor without a phone. With a computer, the
    // session starts right away; without one, the visit is only logged.
    public async Task<WalkInResultDto> WalkInAsync(WalkInRequest request)
    {
        var valid = Registration.Check(request.Registration);
        var (visit, _) = NewVisit(valid, VisitSource.Desk);

        if (request.ComputerId is null)
        {
            _db.Visits.Add(visit);
            await _db.SaveChangesAsync();
            return new WalkInResultDto(ToStaffDto(visit), null);
        }

        var session = await StartOnFreeComputerAsync(visit, request.ComputerId.Value);
        return new WalkInResultDto(ToStaffDto(visit), session);
    }

    // Staff at the PC itself (the kiosk app's staff-only form).
    public async Task<SessionDto> StartAtKioskAsync(int computerId, RegistrationRequest request)
    {
        var (visit, _) = NewVisit(Registration.Check(request), VisitSource.Kiosk);
        return await StartOnFreeComputerAsync(visit, computerId);
    }

    // One all-or-nothing step: check the PC is free, save the visit, start the session.
    private async Task<SessionDto> StartOnFreeComputerAsync(Visit visit, int computerId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        await PassService.ExpireStaleAsync(_db, _clock.UtcNow());

        var status = await _status.GetAsync(computerId);
        if (status != ComputerStatus.Available)
            throw TrackerException.Conflict($"That computer is {SessionService.Describe(status)}.");

        var session = _sessions.Begin(computerId, visit, null, _clock.UtcNow());
        _db.Visits.Add(visit);
        _db.Sessions.Add(session);
        await _db.SaveOrConflictAsync("That computer was just taken.");
        await tx.CommitAsync();

        return await _sessions.GetAsync(session.Id);
    }

    private (Visit Visit, string Token) NewVisit(Registration.Valid valid, VisitSource source)
    {
        var now = _clock.UtcNow();
        var token = Secrets.NewToken();
        var visit = new Visit
        {
            TokenHash = Secrets.Hash(token),
            FirstName = valid.FirstName,
            LastName = valid.LastName,
            AgeGroup = valid.AgeGroup,
            Sex = valid.Sex,
            Sectors = valid.Sectors.Select(s => new VisitSector { Sector = s }).ToList(),
            Services = valid.Services.Select(s => new VisitServiceAvailed { Service = s }).ToList(),
            Source = source,
            CreatedAt = now,
            ExpiresAt = now.AddHours(_options.VisitLifetimeHours)
        };
        return (visit, token);
    }

    // ---- The visitor's own visit (phone) ------------------------------------

    public async Task<VisitDto> GetForVisitorAsync(string token) =>
        await ToVisitorDtoAsync(await FindByTokenAsync(token, mustBeCurrent: false));

    // Finds a visit by the phone's token. mustBeCurrent = refuse visits past their
    // lifetime (to pick a PC or join the waitlist, register again).
    public async Task<Visit> FindByTokenAsync(string token, bool mustBeCurrent = true)
    {
        var hash = Secrets.Hash(token);
        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.TokenHash == hash)
                    ?? throw TrackerException.NotFound("Registration not found. Please register again.");
        if (mustBeCurrent && visit.ExpiresAt <= _clock.UtcNow())
            throw TrackerException.Conflict("This registration has expired. Please register again.");
        return visit;
    }

    public async Task<VisitDto> EndSessionAsync(string token)
    {
        var visit = await FindByTokenAsync(token, mustBeCurrent: false);
        var session = await ActiveSessionAsync(visit);
        await _sessions.EndAsync(session.Id, SessionEndReason.Visitor);
        return await ToVisitorDtoAsync(visit);
    }

    public async Task<VisitDto> ExtendSessionAsync(string token)
    {
        var visit = await FindByTokenAsync(token, mustBeCurrent: false);
        var session = await ActiveSessionAsync(visit);
        await _sessions.ExtendAsync(session.Id);
        return await ToVisitorDtoAsync(visit);
    }

    public async Task<VisitDto> JoinWaitlistAsync(string token)
    {
        var visit = await FindByTokenAsync(token);
        await _waitlist.JoinAsync(visit);
        return await ToVisitorDtoAsync(visit);
    }

    public async Task<VisitDto> LeaveWaitlistAsync(string token)
    {
        var visit = await FindByTokenAsync(token, mustBeCurrent: false);
        await _waitlist.LeaveAsync(visit);
        return await ToVisitorDtoAsync(visit);
    }

    private async Task<UsageSession> ActiveSessionAsync(Visit visit) =>
        await _db.Sessions.FirstOrDefaultAsync(s => s.VisitId == visit.Id && s.EndedAt == null)
        ?? throw TrackerException.Conflict("You do not have a computer session right now.");

    public async Task<VisitDto> ToVisitorDtoAsync(Visit visit)
    {
        var now = _clock.UtcNow();

        var pass = await _db.Passes.Include(p => p.Computer)
            .Where(p => p.VisitId == visit.Id)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        // The running session, else the most recent one (so the phone can say "ended").
        var session = await _db.Sessions.Include(s => s.Visit!).ThenInclude(v => v.Services)
            .Where(s => s.VisitId == visit.Id)
            .OrderBy(s => s.EndedAt == null ? 0 : 1).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync();

        return new VisitDto(
            visit.FirstName,
            visit.CreatedAt,
            visit.ExpiresAt,
            pass is null ? null : new PassDto(PassService.EffectiveStatus(pass, now), pass.Code, pass.ComputerId,
                pass.Computer!.Name, pass.CreatedAt, pass.ExpiresAt),
            session is null ? null : _sessions.ToDto(session, now, await _sessions.CountWaitingAsync(now)),
            await _waitlist.StatusForAsync(visit));
    }

    // ---- Staff: the daily log -----------------------------------------------

    public async Task<List<StaffVisitDto>> ListAsync(DateTime? from, DateTime? to, int limit)
    {
        var query = _db.Visits.Include(v => v.Sectors).Include(v => v.Services).AsQueryable();
        if (from is not null) query = query.Where(v => v.CreatedAt >= from);
        if (to is not null) query = query.Where(v => v.CreatedAt < to);

        var visits = await query.OrderByDescending(v => v.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).ToListAsync();
        return visits.Select(ToStaffDto).ToList();
    }

    // The visit must have been loaded with its sectors and services.
    public static StaffVisitDto ToStaffDto(Visit v) => new(
        v.Id, v.FirstName, v.LastName, v.AgeGroup, v.Sex,
        v.Sectors.Select(s => s.Sector).OrderBy(s => s).ToList(),
        v.Services.Select(s => s.Service).OrderBy(s => s).ToList(),
        v.Source, v.CreatedAt);
}
