using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrackerApi.Auth;
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
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.WaitlistJoin)]
    [HttpPost]
    public Task<WaitlistJoinedDto> Join(JoinWaitlistRequest request) => _waitlist.JoinAsync(request);

    // GET api/waitlist/{token} -> place in line, and how many PCs are free now
    [AllowAnonymous]
    [HttpGet("{token}")]
    public Task<WaitlistStatusDto> Get(string token) => _waitlist.GetAsync(token);

    // DELETE api/waitlist/{token} -> leave the line
    [AllowAnonymous]
    [HttpDelete("{token}")]
    public Task<WaitlistStatusDto> Leave(string token) => _waitlist.LeaveAsync(token);

    // GET api/waitlist -> staff: everyone waiting, first in line first
    [HttpGet]
    public Task<List<WaitlistEntryDto>> List() => _waitlist.GetWaitingAsync();
}
