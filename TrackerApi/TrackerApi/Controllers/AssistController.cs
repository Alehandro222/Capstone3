using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Auth;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssistController : ControllerBase
{
    private readonly TrackerDbContext _db;
    private readonly AuditService _audit;

    public AssistController(TrackerDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // GET api/assist  (optional filter: ?status=Open)
    [HttpGet]
    public async Task<List<AssistDto>> GetAll([FromQuery] AssistStatus? status)
    {
        var query = _db.AssistTickets.Include(t => t.Computer).AsQueryable();

        if (status is not null) query = query.Where(t => t.Status == status);

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        return tickets.Select(ToDto).ToList();
    }

    // POST api/assist   body: { "computerId": 1, "message": "I can't print my file" }
    [HttpPost]
    public async Task<ActionResult<AssistDto>> Create(CreateAssistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Message is required." });

        var computer = await _db.Computers.FindAsync(request.ComputerId);
        if (computer is null) return NotFound(new { message = "Computer not found." });

        var ticket = new AssistTicket
        {
            ComputerId = computer.Id,
            Computer = computer,
            Message = request.Message.Trim()
        };

        _db.AssistTickets.Add(ticket);
        await _db.SaveChangesAsync();
        return Ok(ToDto(ticket));
    }

    // PUT api/assist/4/status   body: { "status": "Resolved" }
    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<AssistDto>> UpdateStatus(int id, UpdateAssistStatusRequest request)
    {
        var ticket = await _db.AssistTickets.Include(t => t.Computer).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        ticket.Status = request.Status;
        ticket.ResolvedAt = request.Status == AssistStatus.Resolved ? DateTime.UtcNow : null;

        await _db.SaveChangesAsync();
        await _audit.RecordAsync("assist.status", "AssistTicket", id, request.Status.ToString());
        return Ok(ToDto(ticket));
    }

    private static AssistDto ToDto(AssistTicket t) =>
        new(t.Id, t.ComputerId, t.Computer?.Name ?? string.Empty, t.Message, t.Status, t.CreatedAt, t.ResolvedAt);
}
