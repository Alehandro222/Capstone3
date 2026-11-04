using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PeripheralsController : ControllerBase
{
    private readonly TrackerDbContext _db;

    public PeripheralsController(TrackerDbContext db)
    {
        _db = db;
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
    public async Task<ActionResult<PeripheralDto>> Create(CreatePeripheralRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Name is required." });

        if (request.ComputerId is not null && !await _db.Computers.AnyAsync(c => c.Id == request.ComputerId))
            return NotFound(new { message = "Computer not found." });

        var peripheral = new Peripheral
        {
            Name = request.Name.Trim(),
            Type = request.Type,
            ComputerId = request.ComputerId
        };

        _db.Peripherals.Add(peripheral);
        await _db.SaveChangesAsync();
        return Ok(ToDto(peripheral));
    }

    // PUT api/peripherals/3/assign   body: { "computerId": 2 }  (null = unplug)
    [HttpPut("{id:int}/assign")]
    public async Task<ActionResult<PeripheralDto>> Assign(int id, AssignPeripheralRequest request)
    {
        var peripheral = await _db.Peripherals.FindAsync(id);
        if (peripheral is null) return NotFound();

        if (request.ComputerId is not null && !await _db.Computers.AnyAsync(c => c.Id == request.ComputerId))
            return NotFound(new { message = "Computer not found." });

        peripheral.ComputerId = request.ComputerId;
        peripheral.IsInUse = false; // moving or unplugging it ends any "in use" state
        peripheral.InUseSince = null;

        await _db.SaveChangesAsync();
        return Ok(ToDto(peripheral));
    }

    // PUT api/peripherals/3/use   body: { "inUse": true }
    [HttpPut("{id:int}/use")]
    public async Task<ActionResult<PeripheralDto>> SetInUse(int id, UsePeripheralRequest request)
    {
        var peripheral = await _db.Peripherals.FindAsync(id);
        if (peripheral is null) return NotFound();

        if (request.InUse && peripheral.ComputerId is null)
            return BadRequest(new { message = "Plug the peripheral into a computer first." });

        peripheral.IsInUse = request.InUse;
        peripheral.InUseSince = request.InUse ? DateTime.UtcNow : null;

        await _db.SaveChangesAsync();
        return Ok(ToDto(peripheral));
    }

    private static PeripheralDto ToDto(Peripheral p) =>
        new(p.Id, p.Name, p.Type, p.ComputerId, p.IsInUse, p.InUseSince);
}
