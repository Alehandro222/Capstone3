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
// Staff only, except the routes the kiosk app needs (marked StaffOrKiosk).
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

    // The framework injects these automatically (registered in Program.cs).
    public ComputersController(TrackerDbContext db, ComputerStatusService status, ProblemService problems,
        SessionService sessions, TimeProvider clock, AuditService audit)
    {
        _audit = audit;
        _db = db;
        _status = status;
        _problems = problems;
        _sessions = sessions;
        _clock = clock;
    }

    // GET api/computers -> staff dashboard: status, who is using it, peripherals.
    [HttpGet]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public async Task<List<ComputerDto>> GetAll() => await LoadAsync(null);

    // GET api/computers/1
    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public async Task<ActionResult<ComputerDto>> GetById(int id)
    {
        var list = await LoadAsync(id);
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
        var dto = (await LoadAsync(computer.Id))[0];
        return CreatedAtAction(nameof(GetById), new { id = computer.Id }, dto);
    }

    // POST api/computers/1/heartbeat
    // The kiosk app on each PC calls this every few seconds. A PC that stops
    // calling it shows as Offline and cannot be picked or checked in.
    [HttpPost("{id:int}/heartbeat")]
    [Authorize(Policy = Policies.StaffOrKiosk)]
    public async Task<IActionResult> Heartbeat(int id)
    {
        var computer = await _db.Computers.FindAsync(id);
        if (computer is null) return NotFound(new { message = "Computer not found." });

        computer.LastHeartbeatAt = _clock.UtcNow();
        await _db.SaveChangesAsync();
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

    // Builds the staff view of one computer (id) or all of them (null).
    private async Task<List<ComputerDto>> LoadAsync(int? id)
    {
        var statuses = await _status.GetAllAsync();

        var computers = await _db.Computers
            .Where(c => id == null || c.Id == id)
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.LastHeartbeatAt,
                Session = c.Sessions.Where(s => s.EndedAt == null)
                    .Select(s => new { s.Id, s.UserName, s.Purpose, s.ScheduledEndAt }).FirstOrDefault(),
                Problem = c.ProblemReports.Where(r => r.Status == ProblemStatus.Open)
                    .OrderBy(r => r.CreatedAt).Select(r => r.Description).FirstOrDefault(),
                Peripherals = c.Peripherals
                    .Select(p => new PeripheralDto(p.Id, p.Name, p.Type, p.ComputerId, p.IsInUse, p.InUseSince))
                    .ToList()
            })
            .ToListAsync();

        return computers.Select(c => new ComputerDto(
            c.Id,
            c.Name,
            statuses[c.Id],
            c.Session?.UserName,
            c.Session?.Purpose,
            c.Peripherals,
            c.LastHeartbeatAt,
            c.Session?.Id,
            c.Session?.ScheduledEndAt,
            c.Problem)).ToList();
    }
}
