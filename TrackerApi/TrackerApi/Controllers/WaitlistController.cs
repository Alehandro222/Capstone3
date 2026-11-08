using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// Staff view of the waitlist. Visitors join and leave through /api/visits/{token}/waitlist.
[ApiController]
[Route("api/[controller]")]
public class WaitlistController : ControllerBase
{
    private readonly WaitlistService _waitlist;

    public WaitlistController(WaitlistService waitlist)
    {
        _waitlist = waitlist;
    }

    // GET api/waitlist -> everyone waiting, first in line first
    [HttpGet]
    public Task<List<WaitlistEntryDto>> List() => _waitlist.GetWaitingAsync();
}
