using Microsoft.AspNetCore.Mvc;
using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Controllers;

// The help desk's refresh (staff only, like every route without [AllowAnonymous]).
// The same lists as GET /api/computers, /api/sessions/active, /api/assist, /api/waitlist,
// /api/problems?status=Open and /api/passes?status=Pending, in one request.
[ApiController]
[Route("api/[controller]")]
public class BoardController : ControllerBase
{
    private readonly ComputerStatusService _computers;
    private readonly SessionService _sessions;
    private readonly AssistService _assist;
    private readonly WaitlistService _waitlist;
    private readonly ProblemService _problems;
    private readonly PassService _passes;

    public BoardController(ComputerStatusService computers, SessionService sessions, AssistService assist,
        WaitlistService waitlist, ProblemService problems, PassService passes)
    {
        _computers = computers;
        _sessions = sessions;
        _assist = assist;
        _waitlist = waitlist;
        _problems = problems;
        _passes = passes;
    }

    // GET api/board
    // One after the other: they share this request's database connection.
    [HttpGet]
    public async Task<BoardDto> Get() => new(
        await _computers.GetStaffViewAsync(null),
        await _sessions.GetActiveAsync(),
        await _assist.ListAsync(null),
        await _waitlist.GetWaitingAsync(),
        await _problems.ListAsync(ProblemStatus.Open, null),
        await _passes.ListAsync(PassStatus.Pending, 100));
}
