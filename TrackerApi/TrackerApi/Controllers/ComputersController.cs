using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Controllers;

// [ApiController] and [Route] are attributes, like annotations in Java.
// "api/[controller]" becomes "api/computers" (the class name minus "Controller").
[ApiController]
[Route("api/[controller]")]
public class ComputersController : ControllerBase
{
    private readonly TrackerDbContext _db; // "readonly" is similar to Java's "final"

    // The framework injects TrackerDbContext here automatically.
    public ComputersController(TrackerDbContext db)
    {
        _db = db;
    }

    // GET api/computers -> dashboard data: status, who is using it, peripherals.
    [HttpGet]
    public async Task<List<ComputerDto>> GetAll()
    {
        return await ToDto(_db.Computers.OrderBy(c => c.Id)).ToListAsync();
    }

    // GET api/computers/1
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ComputerDto>> GetById(int id)
    {
        var dto = await ToDto(_db.Computers.Where(c => c.Id == id)).FirstOrDefaultAsync();
        if (dto is null) return NotFound();
        return dto;
    }

    // POST api/computers   body: { "name": "PC-04" }
    [HttpPost]
    public async Task<ActionResult<ComputerDto>> Create(CreateComputerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name is required." });

        var computer = new Computer { Name = request.Name.Trim() };
        _db.Computers.Add(computer);
        await _db.SaveChangesAsync();

        var dto = await ToDto(_db.Computers.Where(c => c.Id == computer.Id)).FirstAsync();
        return CreatedAtAction(nameof(GetById), new { id = computer.Id }, dto);
    }

    // PUT api/computers/1/status   body: { "status": "NotWorking" }
    // Only Available / NotWorking can be set by hand. "InUse" is set by starting a session.
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateComputerStatusRequest request)
    {
        if (request.Status == ComputerStatus.InUse)
            return BadRequest(new { message = "Start a session to mark a computer as InUse." });

        var computer = await _db.Computers.FindAsync(id);
        if (computer is null) return NotFound();

        var hasActiveSession = await _db.Sessions.AnyAsync(s => s.ComputerId == id && s.EndedAt == null);
        if (hasActiveSession)
            return Conflict(new { message = "End the active session first." });

        computer.Status = request.Status;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Builds the response shape directly in SQL (a LINQ "projection").
    private static IQueryable<ComputerDto> ToDto(IQueryable<Computer> computers)
    {
        return computers.Select(c => new ComputerDto(
            c.Id,
            c.Name,
            c.Status,
            c.Sessions.Where(s => s.EndedAt == null).Select(s => s.UserName).FirstOrDefault(),
            c.Sessions.Where(s => s.EndedAt == null).Select(s => s.Purpose).FirstOrDefault(),
            c.Peripherals
                .Select(p => new PeripheralDto(p.Id, p.Name, p.Type, p.ComputerId, p.IsInUse, p.InUseSince))
                .ToList()));
    }
}
