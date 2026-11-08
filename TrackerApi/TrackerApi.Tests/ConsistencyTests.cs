using System.Net;
using System.Net.Http.Json;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// Rules that keep the API's answers and data consistent.
public class ConsistencyTests : IAsyncLifetime
{
    private readonly TrackerFactory _factory = new();
    private HttpClient _staff = null!;

    public async Task InitializeAsync() => _staff = await _factory.SignedInAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Not_found_answers_carry_a_message()
    {
        var computer = await _staff.GetAsync("/api/computers/99");
        Assert.Equal(HttpStatusCode.NotFound, computer.StatusCode);
        Assert.Equal("Computer not found.", await computer.Message());

        var peripheral = await _staff.PutAsJsonAsync("/api/peripherals/99/use", new { inUse = true });
        Assert.Equal("Peripheral not found.", await peripheral.Message());

        var help = await _staff.PutAsJsonAsync("/api/assist/99/status", new { status = "Resolved" });
        Assert.Equal("Help request not found.", await help.Message());
    }

    [Fact]
    public async Task Unreadable_requests_name_the_bad_field()
    {
        await _factory.Heartbeat(1);
        var response = await _factory.Anonymous().Post("/api/visits", new
        {
            firstName = "Juan", lastName = "Dela Cruz", ageGroup = "Age18To30", sex = "Male",
            sectors = new[] { "Wizards" }, // not one of the 10 sectors
            services = new[] { "Connectivity" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The value for 'sectors' is not valid.", await response.Message());
    }

    [Fact]
    public async Task Staff_can_only_end_a_session_as_staff()
    {
        await _factory.Heartbeat(1);
        var session = (await _staff.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(1))).Session!;

        var faked = await _staff.Post($"/api/sessions/{session.Id}/end", new { reason = "TimeLimit" });
        Assert.Equal(HttpStatusCode.BadRequest, faked.StatusCode);

        var ended = await _staff.PostJson<SessionDto>($"/api/sessions/{session.Id}/end", new { reason = "Staff" });
        Assert.Equal(SessionEndReason.Staff, ended.EndReason);
    }

    [Fact]
    public async Task Help_requests_have_the_same_limit_from_the_dashboard_and_the_kiosk()
    {
        var tooLong = new string('x', 501);

        var fromDashboard = await _staff.Post("/api/assist", new { computerId = 1, message = tooLong });
        Assert.Equal(HttpStatusCode.BadRequest, fromDashboard.StatusCode);

        var kiosk = await _factory.KioskAsync(1);
        var fromKiosk = await kiosk.Post("/api/kiosk/help", new { message = tooLong });
        Assert.Equal(HttpStatusCode.BadRequest, fromKiosk.StatusCode);
        Assert.Equal(await fromDashboard.Message(), await fromKiosk.Message());

        var ok = await kiosk.PostJson<AssistDto>("/api/kiosk/help", new { message = "Printer jam" });
        Assert.Equal(_factory.Clock.GetUtcNow().UtcDateTime, ok.CreatedAt); // the server clock
    }
}
