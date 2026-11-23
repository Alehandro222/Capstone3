using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The "Gemini" section of appsettings.json. The API key never goes in a file that is
// committed: set it with `dotnet user-secrets set "Gemini:ApiKey" "..."` while developing,
// or the environment variable Gemini__ApiKey on the server.
public class GeminiOptions
{
    public const string Section = "Gemini";

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.5-flash";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public int TimeoutSeconds { get; set; } = 90;
    public int RetryDelaySeconds { get; set; } = 4; // wait before a retry after a 500/503/504 (times the attempt)
}

// Drafts the monthly report with Google Gemini (DTC's partner asked for Gemini, free tier).
//
// PRIVACY: Google may use what is sent to the free tier. So the only thing sent is the
// report summary: aggregated counts (ReportDto), turned into labelled numbers below.
// No names, no individual visits or sessions, and no free text typed by staff ever
// leave the server. Staff see exactly what was sent next to the draft.
public class ReportAssistant
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public ReportAssistant(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string Model => _options.Model;

    // The standing instructions. `kind` says which period the report covers.
    public static string Instructions(ReportKind kind)
    {
        var period = kind == ReportKind.Week ? "weekly" : "monthly";
        return
            $"You write the narrative part of the {period} activity report of the DICT Digital Transformation Center (DTC) in Baguio City, " +
            "Philippines: a public center with computers, Wi-Fi, a co-working space and eGov help. The reader is " +
            "DICT-CAR management. You receive only aggregated counts as JSON. The report's tables are added separately, so do not make tables.\n\n" +
            "Rules:\n" +
            "- Use only the numbers given, written as digits exactly as given (12.5 stays 12.5; do not round). Never calculate, estimate or extrapolate: " +
            "do not add, subtract or average anything yourself. Percentages, changes and busiest times are already given: quote them. " +
            "Never list days, hours, groups or computers that had a count of 0, and never write phrases like 'zero visits on' followed by dates; mention zeros only when they matter (for example no visits at all), in one short sentence.\n" +
            "- Write like a person: short plain sentences, no filler, no repeating the same figure twice. Use correct singular and plural (1 session, 2 sessions). " +
            "Group related facts in one bullet instead of one bullet per number, and say what stands out (busiest day or hour, biggest change) before the details.\n" +
            "- Leave out any fact you cannot state with the given numbers. Do not use number words (write 3, not three) and do not number your list items.\n" +
            "- Do not guess causes. You may add a short 'Suggestions' section; label everything there as a suggestion.\n" +
            "- A visitor can tick several sectors and services, so those totals can exceed the number of visits; say so if you compare them.\n" +
            "- Times are Philippine time. Write hours as given, e.g. 13:00, and only hours that are listed. Write dates as given (2026-11-01), never as 'November 1'; a month and year such as 'November 2026' is fine. " +
            "Write age groups, sectors and computer names exactly as labelled in the counts.\n" +
            "- This text is the short overview at the top of an email. Code adds the tables below it (comparison with the previous period, who came and what they used, computers used) " +
            "with every count, so do not repeat them and never list every day, hour, group or computer. Write about 60 to 120 words, in clear, friendly English, no jargon. " +
            "Mention the waitlist only if it was used.\n" +
            "- Format: Markdown with '## ' headings and '- ' bullet points only, one short line per bullet. No tables, no other formatting.\n" +
            "- Sections, in this order: '## Overview' (at most four bullets: the main picture of visits and computer use, what stands out such as the busiest day or hour or the largest group, " +
            "and the main change from the previous period); '## Suggestions' (one or two bullets, each starting with 'Suggestion:', only about running the center, such as opening hours, staffing, outreach or equipment, and only when the numbers clearly support it; " +
            "never about security or the reason for a count; leave the section out if there is nothing useful to suggest).";
    }

    // The anonymous facts for a report period, exactly as they are sent to Gemini.
    // `before` and `comparison` (the previous period and the code-made differences) are optional.
    public static string Facts(ReportDto r, ReportDto? before = null, IReadOnlyList<ComparisonRowDto>? comparison = null)
    {
        static Dictionary<string, int> Labeled<T>(IEnumerable<CountDto> counts, Func<T, string> label) where T : struct, Enum =>
            counts.ToDictionary(c => label(Enum.Parse<T>(c.Key)), c => c.Count);
        static Dictionary<string, int> Hours(IEnumerable<CountDto> counts) =>
            counts.Where(c => c.Count > 0).ToDictionary(c => $"{int.Parse(c.Key, CultureInfo.InvariantCulture):00}:00", c => c.Count);

        var facts = new
        {
            center = FactsGuard.Center,
            period = new { from = r.From.ToString("yyyy-MM-dd"), to = r.To.ToString("yyyy-MM-dd"), days = r.ByDay.Count, timeZone = r.TimeZone },
            totals = new
            {
                visits = r.Visits,
                visitorsWhoUsedAComputer = r.VisitsWithComputer,
                computerSessions = r.Sessions,
                computerHours = r.SessionHours,
                averageSessionMinutes = r.AverageSessionMinutes,
                sessionExtensions = r.Extensions,
                sessionsStillRunning = r.RunningSessions
            },
            visitsPerDay = r.ByDay.ToDictionary(d => d.Day.ToString("yyyy-MM-dd"), d => d.Visits),
            computerSessionsPerDay = r.ByDay.ToDictionary(d => d.Day.ToString("yyyy-MM-dd"), d => d.Sessions),
            busiestDay = ReportMath.BusiestDay(r) is { } bd ? new { date = bd.Day.ToString("yyyy-MM-dd"), visits = bd.Visits } : null,
            busiestHourOfDay = ReportMath.BusiestHour(r) is { } bh ? new { hour = bh.Hour, visits = bh.Visits } : null,
            visitsByHourOfDay = Hours(r.VisitsByHour),
            computerSessionsByHourOfDay = Hours(r.SessionsByHour),
            visitorsByAgeGroup = Labeled<AgeGroup>(r.ByAgeGroup, Labels.Of),
            visitorsBySex = Labeled<Sex>(r.BySex, Labels.Of),
            visitorsBySector = Labeled<Sector>(r.BySector, Labels.Of),
            servicesAvailed = Labeled<ServiceAvailed>(r.ByService, Labels.Of),
            registeredBy = Labeled<VisitSource>(r.BySource, Labels.Of),
            howComputerSessionsEnded = Labeled<SessionEndReason>(r.SessionEndReasons, Labels.Of),
            percentOfVisits = new
            {
                ageGroup = ReportMath.Percents(r.ByAgeGroup, r.Visits, k => Labels.Of(Enum.Parse<AgeGroup>(k))),
                sex = ReportMath.Percents(r.BySex, r.Visits, k => Labels.Of(Enum.Parse<Sex>(k))),
                sector = ReportMath.Percents(r.BySector, r.Visits, k => Labels.Of(Enum.Parse<Sector>(k))),
                service = ReportMath.Percents(r.ByService, r.Visits, k => Labels.Of(Enum.Parse<ServiceAvailed>(k)))
            },
            computers = r.ByComputer.Select(c => new { computer = FactsGuard.NameFor(c.ComputerId, c.Name), sessions = c.Sessions, hours = c.Hours }),
            waitlist = new
            {
                joined = r.Waitlist.Joined,
                gotAComputer = r.Waitlist.GotAComputer,
                missedTheirTurn = r.Waitlist.MissedTurn,
                waitedTooLong = r.Waitlist.Expired,
                leftTheLine = r.Waitlist.Left
            },
            comparedWithPreviousPeriod = before is null || comparison is null ? null : new
            {
                from = before.From.ToString("yyyy-MM-dd"),
                to = before.To.ToString("yyyy-MM-dd"),
                days = before.ByDay.Count,
                rows = comparison.Select(c => new { measure = c.Measure, now = c.Now, before = c.Before, change = c.Change, changePercent = c.ChangePercent })
            }
        };
        return JsonSerializer.Serialize(facts, Json);
    }

    // Asks Gemini for a draft. Throws TrackerException with a message staff can act on.
    // `unknownNumbers`: on a second try, the figures the first draft used that are not in the data.
    public async Task<string> DraftAsync(ReportKind kind, string facts, IReadOnlyList<string>? unknownNumbers = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new TrackerException(503,
                "The report assistant is not set up: an admin needs to add a Gemini API key (see the API README).");

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = Instructions(kind) } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = Retry(unknownNumbers) + "Write the report for this period from these counts:\n\n" + facts } }
                }
            },
            generationConfig = new { temperature = 0.3 }
        };

        var payload = JsonSerializer.Serialize(body);
        HttpRequestMessage NewRequest()
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_options.Endpoint.TrimEnd('/')}/models/{Uri.EscapeDataString(_options.Model)}:generateContent")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-goog-api-key", _options.ApiKey); // a header, so the key never appears in logged URLs
            return request;
        }

        // Google's free tier often answers 500/503/504 for a moment ("high demand"): try up to 3 times.
        HttpResponseMessage response;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = NewRequest();
                response = await _http.SendAsync(request, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new TrackerException(502, "Cannot reach Gemini. Check the server's internet connection and try again.");
            }

            var busy = response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!busy || attempt == 3) break;
            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds * attempt), ct);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => new TrackerException(429,
                        "Gemini's free limit was reached. Try again in a minute, or tomorrow if the daily limit is used up."),
                    HttpStatusCode.BadRequest when text.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) =>
                        new TrackerException(503, "Gemini refused the API key. An admin needs to check it."),
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new TrackerException(503,
                        "Gemini refused the API key. An admin needs to check it."),
                    HttpStatusCode.NotFound => new TrackerException(503,
                        $"Gemini does not know the model \"{_options.Model}\". An admin needs to set Gemini:Model."),
                    _ => new TrackerException(502, $"Gemini returned an error ({(int)response.StatusCode}{Reason(text)}). Try again later.")
                };
            }

            var draft = ReadText(text);
            if (string.IsNullOrWhiteSpace(draft))
                throw new TrackerException(502, "Gemini returned no text. Try again.");
            return draft.Trim();
        }
    }

    // Google's own short explanation of an error ("The model is overloaded..."), so staff can see
    // why. It never holds the API key (that is sent in a header, not in the request body).
    public static string Reason(string errorJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(errorJson);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                && message.GetString() is { Length: > 0 } text)
            {
                text = string.Join(' ', text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
                return ": " + (text.Length > 200 ? text[..200] + "…" : text);
            }
        }
        catch (JsonException)
        {
        }
        return "";
    }

    private static string Retry(IReadOnlyList<string>? unknown) =>
        unknown is { Count: > 0 }
            ? "Your previous draft used these numbers, which are not in the data: " + string.Join(", ", unknown) +
              ". Write the report again and use only numbers that appear in the counts below.\n\n"
            : "";

    // candidates[0].content.parts[*].text
    private static string ReadText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return "";
            if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
                return "";
            return string.Concat(parts.EnumerateArray()
                .Where(p => p.TryGetProperty("text", out _) && !(p.TryGetProperty("thought", out var t) && t.ValueKind == JsonValueKind.True))
                .Select(p => p.GetProperty("text").GetString()));
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
