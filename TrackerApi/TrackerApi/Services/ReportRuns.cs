using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Report runs: a day / week / month report is made by a background job, because the steps
// (read the counts, ask Gemini, check the draft, maybe ask again) take longer than the
// website's host allows for one request (Netlify gives up after 26 s). The page starts a run
// and then checks on it now and then.
//
// Every number in the finished report comes from the data: the tables and comparison are
// computed here, and Gemini's text is kept only if every figure in it is in the facts that
// were sent (ReportMath.UnknownNumbers).

// The ids of runs waiting for the worker.
public sealed class ReportRunQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    private int _pending;

    public ChannelReader<int> Reader => _channel.Reader;

    public void Enqueue(int id)
    {
        Interlocked.Increment(ref _pending);
        _channel.Writer.TryWrite(id);
    }

    public void Finished() => Interlocked.Decrement(ref _pending);

    // Waits until no run is waiting or running (the tests use this instead of polling).
    public async Task DrainAsync()
    {
        while (Volatile.Read(ref _pending) > 0) await Task.Delay(10);
    }
}

// Runs the queued reports one at a time (the free Gemini plan has a small per-minute limit).
public class ReportRunWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ReportRunQueue _queue;
    private readonly ILogger<ReportRunWorker> _log;

    public ReportRunWorker(IServiceScopeFactory scopes, ReportRunQueue queue, ILogger<ReportRunWorker> log)
    {
        _scopes = scopes;
        _queue = queue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Runs that were waiting or running when the server last stopped (Render restarts
            // free services often) are lost: say so instead of leaving them "running".
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ReportRunner>().FailInterruptedAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not clean up interrupted report runs.");
        }

        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReportRunner>().RunAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Report run {Id} failed.", id);
            }
            finally
            {
                _queue.Finished();
            }
        }
    }
}

public class ReportRunner
{
    public static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly TrackerDbContext _db;
    private readonly ReportService _reports;
    private readonly ReportAssistant _assistant;
    private readonly TimeProvider _clock;
    private readonly TrackerOptions _options;
    private readonly ILogger<ReportRunner> _log;

    public ReportRunner(TrackerDbContext db, ReportService reports, ReportAssistant assistant, TimeProvider clock,
        IOptions<TrackerOptions> options, ILogger<ReportRunner> log)
    {
        _db = db;
        _reports = reports;
        _assistant = assistant;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    // The oldest day whose visits are all still kept (older visits are deleted after a year).
    public DateOnly OldestCompleteDay => _reports.Today.AddDays(-_options.KeepVisitsDays);

    // ---- Starting a run (called by the controller) ----------------------------------------

    public async Task<ReportRun> CreateAsync(ReportKind kind, DateOnly? date)
    {
        var today = _reports.Today;
        var whole = ReportMath.Of(kind, date ?? today);
        if (whole.From > today)
            throw TrackerException.BadRequest("That period has not started yet.");
        if (whole.From < OldestCompleteDay)
            throw TrackerException.BadRequest(
                $"Visit records are kept for {_options.KeepVisitsDays} days, so the counts for that period are no longer complete.");

        var now = _clock.UtcNow();
        // One at a time. A run stuck for over 10 minutes (e.g. the server was restarted) doesn't count.
        var busyAfter = now.AddMinutes(-10);
        if (await _db.ReportRuns.AnyAsync(r => (r.Status == RunStatus.Queued || r.Status == RunStatus.Running) && r.CreatedAt > busyAfter))
            throw TrackerException.Conflict("A report is still being made. Wait for it to finish.");

        var run = new ReportRun
        {
            Kind = kind,
            From = whole.From,
            To = whole.To < today ? whole.To : today, // a period that is not over yet is reported up to today
            Status = RunStatus.Queued,
            CreatedAt = now
        };
        _db.ReportRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    public async Task FailInterruptedAsync()
    {
        var stuck = await _db.ReportRuns.Where(r => r.Status == RunStatus.Queued || r.Status == RunStatus.Running).ToListAsync();
        foreach (var run in stuck)
        {
            run.Status = RunStatus.Failed;
            run.Step = "Stopped";
            run.Note = "The server restarted while this report was being made. Start it again.";
            run.FinishedAt = _clock.UtcNow();
        }
        if (stuck.Count > 0) await _db.SaveChangesAsync();
    }

    // ---- The run itself -------------------------------------------------------------------

    public async Task RunAsync(int id, CancellationToken ct)
    {
        var run = await _db.ReportRuns.FindAsync(new object[] { id }, ct);
        if (run is null || run.Status != RunStatus.Queued) return;

        try
        {
            await StepAsync(run, RunStatus.Running, "Reading the counts", ct);
            var current = await _reports.SummaryAsync(run.From, run.To);

            // The previous period, cut to the same number of days when this one is not over.
            // Skipped when it reaches into days whose visits may already be deleted.
            var (prevFrom, prevTo) = ReportMath.Previous(run.Kind, run.From, run.To);
            ReportDto? before = null;
            var comparison = new List<ComparisonRowDto>();
            if (prevFrom >= OldestCompleteDay)
            {
                before = await _reports.SummaryAsync(prevFrom, prevTo);
                comparison = ReportMath.Compare(current, before);
            }

            string narrative = "";
            var checkedOk = false;
            if (run.Kind == ReportKind.Day)
            {
                // A day's text is written by code: nothing is sent to Gemini, nothing can be wrong.
                narrative = ReportMath.PlainNarrative(current, comparison);
                checkedOk = true;
            }
            else if (!_assistant.IsConfigured)
            {
                run.Note = "The written summary is not included: Gemini is not set up (an admin needs to add its API key).";
            }
            else
            {
                var facts = ReportAssistant.Facts(current, before, comparison);
                run.Facts = facts;
                run.Model = _assistant.Model;
                try
                {
                    await StepAsync(run, RunStatus.Running, "Asking Gemini for a draft", ct);
                    var draft = await _assistant.DraftAsync(run.Kind, facts, null, ct);

                    await StepAsync(run, RunStatus.Running, "Checking the numbers", ct);
                    var unknown = ReportMath.UnknownNumbers(draft, facts);
                    if (unknown.Count > 0)
                    {
                        await StepAsync(run, RunStatus.Running, "Asking Gemini again (a number was not in the data)", ct);
                        draft = await _assistant.DraftAsync(run.Kind, facts, unknown, ct);
                        await StepAsync(run, RunStatus.Running, "Checking the numbers again", ct);
                        unknown = ReportMath.UnknownNumbers(draft, facts);
                    }

                    if (unknown.Count == 0)
                    {
                        narrative = draft;
                        checkedOk = true;
                    }
                    else
                    {
                        run.Note = "The written summary was left out: Gemini's draft used numbers that are not in the data (" +
                                   string.Join(", ", unknown) + "). Start the report again.";
                    }
                }
                catch (TrackerException ex)
                {
                    run.Note = "The written summary is not included: " + ex.Message;
                }
            }

            var partial = run.To < ReportMath.Of(run.Kind, run.From).To;
            run.Document = JsonSerializer.Serialize(
                new ReportDocumentDto(run.Kind, partial, current, before, comparison, narrative, checkedOk), DocumentJson);
            run.Step = "Done";
            run.Status = RunStatus.Done;
            run.FinishedAt = _clock.UtcNow();
            await _db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw; // the server is stopping; FailInterruptedAsync marks the run next time
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Report run {Id} failed.", id);
            run.Status = RunStatus.Failed;
            run.Step = "Stopped";
            run.Note = "The report could not be made. Try again.";
            run.FinishedAt = _clock.UtcNow();
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task StepAsync(ReportRun run, RunStatus status, string step, CancellationToken ct)
    {
        run.Status = status;
        run.Step = step;
        await _db.SaveChangesAsync(ct);
    }

    // ---- Reading ----------------------------------------------------------------------------

    public static ReportRunDto ToDto(ReportRun r) => new(
        r.Id, r.Kind, r.From, r.To, r.Status, r.Step, r.Note, r.Model, r.Facts,
        r.Document is null ? null : JsonSerializer.Deserialize<ReportDocumentDto>(r.Document, DocumentJson),
        r.CreatedAt, r.FinishedAt);
}
