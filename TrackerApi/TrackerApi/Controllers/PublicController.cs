using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Data that anyone on the library Wi-Fi may see (visitors' phones).
// Never put names, ages or other personal data here.
[ApiController]
[Route("api/public")]
public class PublicController : ControllerBase
{
    private readonly TrackerDbContext _db;
    private readonly ComputerStatusService _status;

    public PublicController(TrackerDbContext db, ComputerStatusService status)
    {
        _db = db;
        _status = status;
    }

    // GET api/public/computers -> live availability for the "Choose a computer" screen
    [HttpGet("computers")]
    public async Task<List<PublicComputerDto>> Computers()
    {
        var statuses = await _status.GetAllAsync();
        var computers = await _db.Computers.OrderBy(c => c.Id).Select(c => new { c.Id, c.Name }).ToListAsync();
        return computers.Select(c => new PublicComputerDto(c.Id, c.Name, statuses[c.Id])).ToList();
    }
}
