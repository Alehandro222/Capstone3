using System.Net;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// GET /api/board: the help desk's refresh in one request.
public class BoardTests : IDisposable
{
    private readonly TrackerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_board_has_everything_the_help_desk_shows()
    {
        await _factory.Heartbeat(1, 2);
        var staff = await _factory.SignedInAsync("desk", StaffRole.Staff);
        var phone = _factory.Anonymous();
        await phone.RegisterAndPick(1, "Ana", "Reyes"); // a pending pass
        await staff.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(2, "Ben", "Cruz")); // a session

        var board = await staff.GetJson<BoardDto>("/api/board");

        // The same answers as the separate routes.
        Assert.Equal((await staff.GetJson<List<ComputerDto>>("/api/computers")).Select(c => c.Id), board.Computers.Select(c => c.Id));
        Assert.Equal((await staff.GetJson<List<SessionDto>>("/api/sessions/active")).Select(s => s.Id), board.Sessions.Select(s => s.Id));
        Assert.Equal((await staff.GetJson<List<StaffPassDto>>("/api/passes?status=Pending")).Select(p => p.Id), board.Passes.Select(p => p.Id));
        Assert.Equal((await staff.GetJson<List<ProblemReportDto>>("/api/problems?status=Open")).Select(p => p.Id), board.Problems.Select(p => p.Id));
        Assert.Equal((await staff.GetJson<List<AssistDto>>("/api/assist")).Select(a => a.Id), board.Tickets.Select(a => a.Id));
        Assert.Equal((await staff.GetJson<List<WaitlistEntryDto>>("/api/waitlist")).Select(w => w.Id), board.Waitlist.Select(w => w.Id));

        Assert.Single(board.Sessions);
        Assert.Single(board.Passes);
        Assert.Contains(board.Problems, p => p.ComputerId == 3); // the sample open problem on PC-03
        Assert.All(board.Problems, p => Assert.Equal(ProblemStatus.Open, p.Status));
    }

    [Fact]
    public async Task Only_staff_see_the_board()
    {
        var response = await _factory.Anonymous().GetAsync("/api/board");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
