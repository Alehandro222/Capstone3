using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Help requests ("Need help?"), from the kiosk app on a PC or from the dashboard.
public class AssistService
{
    public const int MaxMessageLength = 500;

    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;

    public AssistService(TrackerDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<List<AssistDto>> ListAsync(AssistStatus? status)
    {
        var query = _db.AssistTickets.Include(t => t.Computer).AsQueryable();
        if (status is not null) query = query.Where(t => t.Status == status);

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
        return tickets.Select(ToDto).ToList();
    }

    public async Task<AssistDto> CreateAsync(int computerId, string? message)
    {
        message = message?.Trim();
        if (string.IsNullOrEmpty(message)) throw TrackerException.BadRequest("Please describe what you need help with.");
        if (message.Length > MaxMessageLength)
            throw TrackerException.BadRequest($"Please keep it under {MaxMessageLength} characters.");

        var computer = await _db.Computers.FindAsync(computerId)
                       ?? throw TrackerException.NotFound("Computer not found.");

        var ticket = new AssistTicket
        {
            ComputerId = computer.Id,
            Computer = computer,
            Message = message,
            CreatedAt = _clock.UtcNow()
        };
        _db.AssistTickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ToDto(ticket);
    }

    public async Task<AssistDto> SetStatusAsync(int id, AssistStatus status)
    {
        if (!Enum.IsDefined(status)) throw TrackerException.BadRequest("Unknown status.");

        var ticket = await _db.AssistTickets.Include(t => t.Computer).FirstOrDefaultAsync(t => t.Id == id)
                     ?? throw TrackerException.NotFound("Help request not found.");

        ticket.Status = status;
        ticket.ResolvedAt = status == AssistStatus.Resolved ? _clock.UtcNow() : null;
        await _db.SaveChangesAsync();
        return ToDto(ticket);
    }

    private static AssistDto ToDto(AssistTicket t) =>
        new(t.Id, t.ComputerId, t.Computer?.Name ?? string.Empty, t.Message, t.Status, t.CreatedAt, t.ResolvedAt);
}
