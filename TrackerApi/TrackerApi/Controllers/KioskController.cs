using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using TrackerApi.Auth;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Everything the kiosk app on a PC may do. The PC is identified by its key
// (see KioskAuthHandler), never by an id in the URL or body, so a PC can only
// ever see and change its OWN computer and session.
[ApiController]
[Route("api/kiosk")]
[Authorize(Policy = Policies.Kiosk)]
public class KioskController : ControllerBase
{
    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ComputerStatusService _status;
    private readonly SessionService _sessions;
    private readonly KioskPairingService _pairing;
    private readonly AuditService _audit;
    private readonly AssistService _assist;
    private readonly VisitService _visits;

    public KioskController(TrackerDbContext db, TimeProvider clock, ComputerStatusService status,
        SessionService sessions, KioskPairingService pairing, AuditService audit, AssistService assist,
        VisitService visits)
    {
        _visits = visits;
        _assist = assist;
        _db = db;
        _clock = clock;
        _status = status;
        _sessions = sessions;
        _pairing = pairing;
        _audit = audit;
    }

    private int ComputerId => User.KioskComputerId();

    // POST api/kiosk/pair   body: { "code": "ABCD-2345" }
    // The setup screen trades a one-time pairing code for this PC's secret key.
    [HttpPost("pair")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Pairing)]
    public async Task<KioskPairedDto> Pair(PairKioskRequest request)
    {
        var paired = await _pairing.PairAsync(request.Code);
        await _audit.RecordAsAsync(ActorType.Kiosk, null, $"Kiosk {paired.ComputerName}", "kiosk.paired",
            "Computer", paired.ComputerId);
        return paired;
    }

    // POST api/kiosk/heartbeat -> "this PC is on", answered with what the screens need.
    // Called every few seconds; a PC that stops calling it shows as Offline.
    [HttpPost("heartbeat")]
    public async Task<KioskStateDto> Heartbeat()
    {
        var computer = await _db.Computers.FindAsync(ComputerId);
        computer!.LastHeartbeatAt = _clock.UtcNow();
        await _db.SaveChangesAsync();

        var view = (await _status.GetStaffViewAsync(ComputerId))[0];
        return new KioskStateDto(view, await _sessions.GetActiveOnComputerAsync(ComputerId));
    }

    // POST api/kiosk/sessions   body: the registration form
    //   { "firstName", "lastName", "ageGroup", "sex", "sectors": [...], "services": [...] }
    // The staff-only form on the PC (behind the staff PIN), e.g. for a visitor
    // whose phone died: registers the visit and starts the session on this PC.
    [HttpPost("sessions")]
    public async Task<SessionDto> Start(RegistrationRequest request)
    {
        var session = await _visits.StartAtKioskAsync(ComputerId, request);
        await _audit.RecordAsync("visit.kiosk", "Session", session.Id, $"computer {ComputerId}, visit {session.VisitId}");
        return session;
    }

    // POST api/kiosk/session/end   body (optional): { "reason": "Idle" }
    // Ends THIS PC's session. Without a reason: the visitor pressed Log out.
    [HttpPost("session/end")]
    public async Task<SessionDto> End([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EndSessionRequest? request)
    {
        var reason = request?.Reason ?? SessionEndReason.Visitor;
        if (reason is not (SessionEndReason.Visitor or SessionEndReason.Idle))
            throw TrackerException.BadRequest("The kiosk can only end a session as Visitor or Idle.");

        var current = await CurrentSessionAsync();
        var session = await _sessions.EndAsync(current.Id, reason);
        await _audit.RecordAsync("session.end", "Session", session.Id, $"computer {ComputerId}, reason: {reason}");
        return session;
    }

    // POST api/kiosk/session/extend -> THIS PC's session, if the extension rules allow it
    [HttpPost("session/extend")]
    public async Task<SessionDto> Extend()
    {
        var current = await CurrentSessionAsync();
        var session = await _sessions.ExtendAsync(current.Id);
        await _audit.RecordAsync("session.extend", "Session", session.Id,
            $"now ends {session.ScheduledEndAt:u} (extension {session.ExtensionCount})");
        return session;
    }

    // POST api/kiosk/help   body: { "message": "I can't print" } -> the "Need help?" button
    [HttpPost("help")]
    public Task<AssistDto> Help(KioskHelpRequest request) => _assist.CreateAsync(ComputerId, request.Message);

    private async Task<SessionDto> CurrentSessionAsync() =>
        await _sessions.GetActiveOnComputerAsync(ComputerId)
        ?? throw TrackerException.Conflict("There is no session on this computer.");
}
