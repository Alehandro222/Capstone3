using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WaitlistController : ControllerBase
{
    private readonly WaitlistService _waitlist;

    public WaitlistController(WaitlistService waitlist)
    {
        _waitlist = waitlist;
    }

    // POST api/waitlist   body: { "firstName": "Ana" } (optional) -> only when no PC is free
    [HttpPost]
    public Task<WaitlistJoinedDto> Join(JoinWaitlistRequest request) => _waitlist.JoinAsync(request);

    // GET api/waitlist/{token} -> place in line, and how many PCs are free now
    [HttpGet("{token}")]
    public Task<WaitlistStatusDto> Get(string token) => _waitlist.GetAsync(token);

    // DELETE api/waitlist/{token} -> leave the line
    [HttpDelete("{token}")]
    public Task<WaitlistStatusDto> Leave(string token) => _waitlist.LeaveAsync(token);

    // GET api/waitlist -> staff: everyone waiting, first in line first
    [HttpGet]
    public Task<List<WaitlistEntryDto>> List() => _waitlist.GetWaitingAsync();
}
