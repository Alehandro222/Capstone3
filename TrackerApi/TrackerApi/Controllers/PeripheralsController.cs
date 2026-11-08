using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PeripheralsController : ControllerBase
{
    private readonly TrackerDbContext _db;
    private readonly AuditService _audit;
    private readonly TimeProvider _clock;

    public PeripheralsController(TrackerDbContext db, AuditService audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    // GET api/peripherals  (optional filters: ?computerId=1  ?inUse=true)
    [HttpGet]
    public async Task<List<PeripheralDto>> GetAll([FromQuery] int? computerId, [FromQuery] bool? inUse)
    {
        var query = _db.Peripherals.AsQueryable();

        if (computerId is not null) query = query.Where(p => p.ComputerId == computerId);
        if (inUse is not null) query = query.Where(p => p.IsInUse == inUse);

        return await query
            .OrderBy(p => p.Id)
            .Select(p => new PeripheralDto(p.Id, p.Name, p.Type, p.ComputerId, p.IsInUse, p.InUseSince))
            .ToListAsync();
    }

    // POST api/peripherals   body: { "name": "Headset B", "type": "Headset", "computerId": 2 }
    [HttpPost]
    public async Task<PeripheralDto> Create(CreatePeripheralRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) throw TrackerException.BadRequest("Name is required.");
        if (name.Length > 100) throw TrackerException.BadRequest("Name must be 100 characters or fewer.");
        await EnsureComputerExistsAsync(request.ComputerId);

        var peripheral = new Peripheral { Name = name, Type = request.Type, ComputerId = request.ComputerId };
        _db.Peripherals.Add(peripheral);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("peripheral.create", "Peripheral", peripheral.Id, peripheral.Name);
        return ToDto(peripheral);
    }

    // PUT api/peripherals/3/assign   body: { "computerId": 2 }  (null = unplug)
    [HttpPut("{id:int}/assign")]
    public async Task<PeripheralDto> Assign(int id, AssignPeripheralRequest request)
    {
        var peripheral = await FindAsync(id);
        await EnsureComputerExistsAsync(request.ComputerId);

        peripheral.ComputerId = request.ComputerId;
        peripheral.IsInUse = false; // moving or unplugging it ends any "in use" state
        peripheral.InUseSince = null;

        await _db.SaveChangesAsync();
        await _audit.RecordAsync("peripheral.assign", "Peripheral", id,
            request.ComputerId is null ? "unplugged" : $"to computer {request.ComputerId}");
        return ToDto(peripheral);
    }

    // PUT api/peripherals/3/use   body: { "inUse": true }
    [HttpPut("{id:int}/use")]
    public async Task<PeripheralDto> SetInUse(int id, UsePeripheralRequest request)
    {
        var peripheral = await FindAsync(id);
        if (request.InUse && peripheral.ComputerId is null)
            throw TrackerException.BadRequest("Plug the peripheral into a computer first.");

        peripheral.IsInUse = request.InUse;
        peripheral.InUseSince = request.InUse ? _clock.UtcNow() : null;

        await _db.SaveChangesAsync();
        return ToDto(peripheral);
    }

    private async Task<Peripheral> FindAsync(int id) =>
        await _db.Peripherals.FindAsync(id) ?? throw TrackerException.NotFound("Peripheral not found.");

    private async Task EnsureComputerExistsAsync(int? computerId)
    {
        if (computerId is not null && !await _db.Computers.AnyAsync(c => c.Id == computerId))
            throw TrackerException.NotFound("Computer not found.");
    }

    private static PeripheralDto ToDto(Peripheral p) =>
        new(p.Id, p.Name, p.Type, p.ComputerId, p.IsInUse, p.InUseSince);
}
