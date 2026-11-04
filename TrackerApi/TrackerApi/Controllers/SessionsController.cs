using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionsController : ControllerBase
{
    private readonly TrackerDbContext _db;

    public SessionsController(TrackerDbContext db)
    {
        _db = db;
    }

    // GET api/sessions/active -> everyone currently using a computer
    [HttpGet("active")]
    public async Task<List<SessionDto>> GetActive()
    {
        var sessions = await _db.Sessions
            .Where(s => s.EndedAt == null)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();

        return sessions.Select(ToDto).ToList();
    }

    // POST api/sessions/start
    // body: { "computerId": 1, "userName": "Maria", "purpose": "Job application" }
    [HttpPost("start")]
    public async Task<ActionResult<SessionDto>> Start(StartSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Purpose))
            return BadRequest(new { message = "UserName and Purpose are required." });

        // Keep stored text a sensible size (the kiosk form enforces the same limits).
        if (request.UserName.Trim().Length > 100)
            return BadRequest(new { message = "Name must be 100 characters or fewer." });
        if (request.Purpose.Trim().Length > 200)
            return BadRequest(new { message = "Purpose must be 200 characters or fewer." });

        var computer = await _db.Computers.FindAsync(request.ComputerId);
        if (computer is null) return NotFound(new { message = "Computer not found." });

        if (computer.Status != ComputerStatus.Available)
            return Conflict(new { message = $"Computer is {computer.Status}." });

        var session = new UsageSession
        {
            ComputerId = computer.Id,
            UserName = request.UserName.Trim(),
            Purpose = request.Purpose.Trim(),
            StartedAt = DateTime.UtcNow
        };

        computer.Status = ComputerStatus.InUse;
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(ToDto(session));
    }

    // POST api/sessions/5/end -> frees the computer and its peripherals
    [HttpPost("{id:int}/end")]
    public async Task<ActionResult<SessionDto>> End(int id)
    {
        var session = await _db.Sessions.FindAsync(id);
        if (session is null) return NotFound();
        if (session.EndedAt is not null) return Conflict(new { message = "Session already ended." });

        session.EndedAt = DateTime.UtcNow;

        var computer = await _db.Computers.FindAsync(session.ComputerId);
        if (computer is not null && computer.Status == ComputerStatus.InUse)
            computer.Status = ComputerStatus.Available;

        // Release any peripherals that were marked in use on this computer.
        var busy = await _db.Peripherals
            .Where(p => p.ComputerId == session.ComputerId && p.IsInUse)
            .ToListAsync();
        foreach (var p in busy)
        {
            p.IsInUse = false;
            p.InUseSince = null;
        }

        await _db.SaveChangesAsync();
        return Ok(ToDto(session));
    }

    private static SessionDto ToDto(UsageSession s) =>
        new(s.Id, s.ComputerId, s.UserName, s.Purpose, s.StartedAt, s.EndedAt);
}
