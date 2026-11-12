using System.Net;
using System.Net.Http.Json;
using TrackerApi.Models;

namespace TrackerApi.Tests;

// Admins match the PC list to the real machines: rename, add, and remove unused ones.
public class ComputerAdminTests : IAsyncLifetime
{
    private readonly TrackerFactory _factory = new();
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await _factory.SignedInAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Admins_rename_a_pc_but_names_stay_unique()
    {
        var renamed = await (await _admin.PutAsJsonAsync("/api/computers/1", new { name = "  Lab A - 01 " })).Content
            .ReadFromJsonAsync<ComputerDto>(TrackerFactory.Json);
        Assert.Equal("Lab A - 01", renamed!.Name);

        var taken = await _admin.PutAsJsonAsync("/api/computers/2", new { name = "Lab A - 01" });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _admin.Post("/api/computers", new { name = "PC-05" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PutAsJsonAsync("/api/computers/2", new { name = " " })).StatusCode);

        Assert.Contains("PC-01 -> Lab A - 01",
            Assert.Single(await _admin.GetJson<List<AuditEntryDto>>("/api/audit?action=computer.rename")).Details);
    }

    [Fact]
    public async Task An_unused_pc_can_be_removed_with_its_reports_and_help_requests()
    {
        // PC-03 has the sample problem report; PC-01 has peripherals.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync("/api/computers/3")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync("/api/computers/1")).StatusCode);

        var computers = await _admin.GetJson<List<ComputerDto>>("/api/computers");
        Assert.Equal(10, computers.Count);
        Assert.DoesNotContain(computers, c => c.Id is 1 or 3);
        Assert.Empty(await _admin.GetJson<List<ProblemReportDto>>("/api/problems?computerId=3"));
        Assert.All(await _admin.GetJson<List<PeripheralDto>>("/api/peripherals"), p => Assert.NotEqual(1, p.ComputerId));

        // A new PC still gets a fresh id after the sample ones.
        var added = await _admin.PostJson<ComputerDto>("/api/computers", new { name = "PC-13" });
        Assert.True(added.Id > 12);
    }

    [Fact]
    public async Task A_pc_with_visitor_history_cannot_be_removed()
    {
        await _factory.Heartbeat(4);
        await _admin.PostJson<WalkInResultDto>("/api/visits/walk-in", ApiExtensions.WalkIn(4));

        var refused = await _admin.DeleteAsync("/api/computers/4");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Rename it instead", await refused.Message());
    }

    [Fact]
    public async Task Staff_cannot_rename_or_remove_pcs()
    {
        var staff = await _factory.SignedInAsync("desk", StaffRole.Staff);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/computers/1", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.DeleteAsync("/api/computers/2")).StatusCode);
    }
}
