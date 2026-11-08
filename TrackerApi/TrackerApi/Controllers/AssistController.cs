using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Staff only. (The kiosk app sends help requests through /api/kiosk/help.)
[ApiController]
[Route("api/[controller]")]
public class AssistController : ControllerBase
{
    private readonly AssistService _assist;
    private readonly AuditService _audit;

    public AssistController(AssistService assist, AuditService audit)
    {
        _assist = assist;
        _audit = audit;
    }

    // GET api/assist  (optional filter: ?status=Open)
    [HttpGet]
    public Task<List<AssistDto>> GetAll([FromQuery] AssistStatus? status) => _assist.ListAsync(status);

    // POST api/assist   body: { "computerId": 1, "message": "I can't print my file" }
    [HttpPost]
    public Task<AssistDto> Create(CreateAssistRequest request) => _assist.CreateAsync(request.ComputerId, request.Message);

    // PUT api/assist/4/status   body: { "status": "Resolved" }
    [HttpPut("{id:int}/status")]
    public async Task<AssistDto> UpdateStatus(int id, UpdateAssistStatusRequest request)
    {
        var ticket = await _assist.SetStatusAsync(id, request.Status);
        await _audit.RecordAsync("assist.status", "AssistTicket", id, request.Status.ToString());
        return ticket;
    }
}
