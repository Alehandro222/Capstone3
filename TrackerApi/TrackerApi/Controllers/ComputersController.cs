using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Auth;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// [ApiController] and [Route] are attributes, like annotations in Java.
// "api/[controller]" becomes "api/computers" (the class name minus "Controller").
// Staff only. (The kiosk app uses /api/kiosk instead; see KioskController.)
[ApiController]
[Route("api/[controller]")]
public class ComputersController : ControllerBase
{
    private readonly TrackerDbContext _db; // "readonly" is similar to Java's "final"
    private readonly ComputerStatusService _status;
    private readonly ProblemService _problems;
    private readonly SessionService _sessions;
    private readonly TimeProvider _clock;
    private readonly AuditService _audit;
    private readonly KioskPairingService _pairing;

    // The framework injects these automatically (registered in Program.cs).
    public ComputersController(TrackerDbContext db, ComputerStatusService status, ProblemService problems,
        SessionService sessions, TimeProvider clock, AuditService audit, KioskPairingService pairing)
    {
        _pairing = pairing;
        _audit = audit;
        _db = db;
        _status = status;
        _problems = problems;
        _sessions = sessions;
        _clock = clock;
    }

    // GET api/computers -> staff dashboard: status, who is using it, peripherals.
    [HttpGet]
    public async Task<List<ComputerDto>> GetAll() => await _status.GetStaffViewAsync(null);

    // GET api/computers/1
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComputerDto>> GetById(int id)
    {
        var list = await _status.GetStaffViewAsync(id);
        if (list.Count == 0) return NotFound();
        return list[0];
    }

    // POST api/computers   body: { "name": "PC-13" }
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ComputerDto>> Create(CreateComputerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name is required." });
        if (request.Name.Trim().Length > 50)
            return BadRequest(new { message = "Name must be 50 characters or fewer." });

        var computer = new Computer { Name = request.Name.Trim() };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        await _audit.RecordAsync("computer.create", "Computer", computer.Id, computer.Name);
        var dto = (await _status.GetStaffViewAsync(computer.Id))[0];
        return CreatedAtAction(nameof(GetById), new { id = computer.Id }, dto);
    }

    // POST api/computers/1/pairing-code   (admin)
    // A one-time code (valid 15 minutes) that staff type into the kiosk app's
    // setup screen on that PC. Pairing gives the PC a new key; the old one stops working.
    [HttpPost("{id:int}/pairing-code")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<PairingCodeDto> CreatePairingCode(int id)
    {
        var code = await _pairing.CreateCodeAsync(id);
        await _audit.RecordAsync("kiosk.pairing_code", "Computer", id, code.ComputerName);
        return code;
    }

    // POST api/computers/1/unpair   (admin) -> the PC's kiosk app stops working until paired again
    [HttpPost("{id:int}/unpair")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Unpair(int id)
    {
        await _pairing.UnpairAsync(id);
        await _audit.RecordAsync("kiosk.unpaired", "Computer", id);
        return NoContent();
    }

    // GET api/computers/1/sessions -> session history for one PC, newest first.
    [HttpGet("{id:int}/sessions")]
    public async Task<ActionResult<List<SessionDto>>> History(int id, [FromQuery] int limit = 100)
    {
        if (!await _db.Computers.AnyAsync(c => c.Id == id)) return NotFound();
        return await _sessions.GetHistoryAsync(id, null, null, limit);
    }

    // PUT api/computers/1/status   body: { "status": "Maintenance" } or { "status": "Available" }
    // Kept for the current dashboard's "Report broken" / "Mark as fixed" buttons.
    // Status is never stored: Maintenance opens a problem report, Available resolves them.
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateComputerStatusRequest request)
    {
        if (!await _db.Computers.AnyAsync(c => c.Id == id)) return NotFound();

        switch (request.Status)
        {
            case ComputerStatus.Maintenance:
                if (await _db.Sessions.AnyAsync(s => s.ComputerId == id && s.EndedAt == null))
                    return Conflict(new { message = "End the active session first." });
                if (!await _db.ProblemReports.AnyAsync(r => r.ComputerId == id && r.Status == ProblemStatus.Open))
                {
                    var report = await _problems.OpenAsync(new CreateProblemRequest(id, "Marked out of service from the dashboard."));
                    await _audit.RecordAsync("problem.open", "ProblemReport", report.Id, $"computer {id}: {report.Description}");
                }
                return NoContent();

            case ComputerStatus.Available:
                await _problems.ResolveAllForComputerAsync(id, "Marked as fixed from the dashboard.");
                await _audit.RecordAsync("problem.resolve_all", "Computer", id, "Marked as fixed from the dashboard.");
                return NoContent();

            default:
                return BadRequest(new { message = "Only Maintenance or Available can be set by hand." });
        }
    }
}
