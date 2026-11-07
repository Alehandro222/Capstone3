using Microsoft.Extensions.Options;

namespace TrackerApi.Services;

// Runs in the background for as long as the API runs. Every few seconds it:
//  - marks unscanned passes past their expiry as Expired (frees the PC),
//  - ends sessions past their time limit,
//  - drops people who have been on the waitlist too long.
// The kiosk app sees the ended session on its next refresh and locks the PC.
public class CleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CleanupService> _log;
    private readonly TrackerOptions _options;

    public CleanupService(IServiceScopeFactory scopes, ILogger<CleanupService> log, IOptions<TrackerOptions> options)
    {
        _scopes = scopes;
        _log = log;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CleanupIntervalSeconds <= 0) return; // turned off (the tests do this)

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CleanupIntervalSeconds));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await RunOnceAsync(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                // One failed run must never stop the job for good.
                _log.LogError(ex, "Cleanup failed; will try again.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task RunOnceAsync(IServiceProvider services)
    {
        var expiredPasses = await services.GetRequiredService<PassService>().ExpireOldAsync();
        var endedSessions = await services.GetRequiredService<SessionService>().EndOverdueAsync();
        var expiredWaits = await services.GetRequiredService<WaitlistService>().ExpireOldAsync();

        if (expiredPasses + endedSessions + expiredWaits > 0)
        {
            services.GetRequiredService<ILogger<CleanupService>>().LogInformation(
                "Cleanup: {Passes} passes expired, {Sessions} sessions ended at the time limit, {Waits} waitlist entries expired.",
                expiredPasses, endedSessions, expiredWaits);
        }
    }
}
