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

    public const string Instructions =
        "You write the monthly activity report of the DICT Digital Transformation Center (DTC) in Baguio City, " +
        "Philippines: a public center with computers, Wi-Fi, a co-working space and eGov help. The reader is " +
        "DICT-CAR management. You receive only aggregated counts as JSON.\n\n" +
        "Rules:\n" +
        "- Use only the numbers given. Never invent, estimate or extrapolate a number. If a figure is 0, say so plainly.\n" +
        "- Do not guess causes. You may add a short 'Suggestions' section; label everything there as a suggestion.\n" +
        "- A visitor can tick several sectors and services, so those totals can exceed the number of visits; say so if you compare them.\n" +
        "- Times are Philippine time. Hours of the day are 24-hour (13 = 1 PM).\n" +
        "- Formal, clear English for a government report. About 350 to 600 words.\n" +
        "- Format: Markdown with '## ' headings and '- ' bullet points only. No tables, no other formatting.\n" +
        "- Sections, in this order: Summary; Visitors; Services availed; Computer use; Waitlist; Suggestions.";

    // The anonymous facts for a report period, exactly as they are sent to Gemini.
    public static string Facts(ReportDto r)
    {
        static Dictionary<string, int> Labeled<T>(IEnumerable<CountDto> counts, Func<T, string> label) where T : struct, Enum =>
            counts.ToDictionary(c => label(Enum.Parse<T>(c.Key)), c => c.Count);
        static Dictionary<string, int> Hours(IEnumerable<CountDto> counts) =>
            counts.Where(c => c.Count > 0).ToDictionary(c => $"{int.Parse(c.Key, CultureInfo.InvariantCulture):00}:00", c => c.Count);

        var facts = new
        {
            center = "DICT Digital Transformation Center, Baguio City",
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
            visitsByHourOfDay = Hours(r.VisitsByHour),
            computerSessionsByHourOfDay = Hours(r.SessionsByHour),
            visitorsByAgeGroup = Labeled<AgeGroup>(r.ByAgeGroup, Labels.Of),
            visitorsBySex = Labeled<Sex>(r.BySex, Labels.Of),
            visitorsBySector = Labeled<Sector>(r.BySector, Labels.Of),
            servicesAvailed = Labeled<ServiceAvailed>(r.ByService, Labels.Of),
            registeredBy = Labeled<VisitSource>(r.BySource, Labels.Of),
            howComputerSessionsEnded = Labeled<SessionEndReason>(r.SessionEndReasons, Labels.Of),
            computers = r.ByComputer.Select(c => new { computer = c.Name, sessions = c.Sessions, hours = c.Hours }),
            waitlist = new
            {
                joined = r.Waitlist.Joined,
                gotAComputer = r.Waitlist.GotAComputer,
                missedTheirTurn = r.Waitlist.MissedTurn,
                waitedTooLong = r.Waitlist.Expired,
                leftTheLine = r.Waitlist.Left
            }
        };
        return JsonSerializer.Serialize(facts, Json);
    }

    // Asks Gemini for a draft. Throws TrackerException with a message staff can act on.
    public async Task<string> DraftAsync(string facts, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new TrackerException(503,
                "The report assistant is not set up: an admin needs to add a Gemini API key (see the API README).");

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = Instructions } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = "Write the report for this period from these counts:\n\n" + facts } }
                }
            },
            generationConfig = new { temperature = 0.3 }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.Endpoint.TrimEnd('/')}/models/{Uri.EscapeDataString(_options.Model)}:generateContent")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", _options.ApiKey); // a header, so the key never appears in logged URLs

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TrackerException(502, "Cannot reach Gemini. Check the server's internet connection and try again.");
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
                    _ => new TrackerException(502, $"Gemini returned an error ({(int)response.StatusCode}). Try again later.")
                };
            }

            var draft = ReadText(text);
            if (string.IsNullOrWhiteSpace(draft))
                throw new TrackerException(502, "Gemini returned no text. Try again.");
            return draft.Trim();
        }
    }

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
