using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrackerApi.Data;
using TrackerApi.Models;

namespace TrackerApi.Services;

// Reports for DTC: visits and PC use over a range of days, counted in the center's
// time zone (a "day" in Baguio runs from midnight to midnight Philippine time).
//
// The summary is counts only. The visitor and session lists (with names) are only in
// the exports, which admins download (see ReportsController).
public class ReportService
{
    public const int MaxDays = 366;

    private readonly TrackerDbContext _db;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private readonly string _zoneName;

    public ReportService(TrackerDbContext db, TimeProvider clock, IOptions<TrackerOptions> options)
    {
        _db = db;
        _clock = clock;
        _zoneName = options.Value.TimeZone;
        _zone = FindZone(_zoneName);
    }

    // "Asia/Manila" works on Windows 10+ and Linux. If a PC cannot find it, use a fixed UTC+8
    // (the Philippines has no daylight saving time, so the result is the same).
    public static TimeZoneInfo FindZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+08", TimeSpan.FromHours(8), "UTC+08", "UTC+08");
        }
    }

    // Today in the center's time zone.
    public DateOnly Today => DateOnly.FromDateTime(ToLocal(_clock.UtcNow()));

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    // Checks a from/to pair (both days included) and fills in defaults: today, or from = to.
    public (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? from ?? Today;
        var start = from ?? end;
        if (start > end) throw TrackerException.BadRequest("The start date must be on or before the end date.");
        if (end.DayNumber - start.DayNumber + 1 > MaxDays)
            throw TrackerException.BadRequest($"Choose at most {MaxDays} days.");
        return (start, end);
    }

    // The UTC instants where a range of local days starts and ends (end exclusive).
    private (DateTime FromUtc, DateTime ToUtc) Bounds(DateOnly from, DateOnly to) =>
        (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));

    private DateTime StartOfDayUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone);

    // ---- The summary ---------------------------------------------------------

    public async Task<ReportDto> SummaryAsync(DateOnly from, DateOnly to)
    {
        var (fromUtc, toUtc) = Bounds(from, to);
        var now = _clock.UtcNow();

        var visits = await VisitsAsync(fromUtc, toUtc);
        var sessions = await SessionsAsync(fromUtc, toUtc);
        var computers = await _db.Computers.OrderBy(c => c.Id).Select(c => new { c.Id, c.Name }).ToListAsync();
        var waitlist = await _db.WaitlistEntries
            .Where(w => w.CreatedAt >= fromUtc && w.CreatedAt < toUtc)
            .Select(w => w.Status)
            .ToListAsync();

        // A running session counts until now, but never past its time limit (the cleanup job
        // ends it there; this keeps the numbers right even before it has run).
        double Hours(UsageSession s) => (End(s, now) - s.StartedAt).TotalHours;
        var totalHours = sessions.Sum(Hours);

        var days = new List<ReportDayDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var daySessions = sessions.Where(s => DayOf(s.StartedAt) == day).ToList();
            days.Add(new ReportDayDto(day,
                visits.Count(v => DayOf(v.CreatedAt) == day),
                daySessions.Count,
                Round(daySessions.Sum(Hours))));
        }

        return new ReportDto(
            from, to, _zoneName,
            Visits: visits.Count,
            VisitsWithComputer: sessions.Select(s => s.VisitId).Distinct().Count(),
            Sessions: sessions.Count,
            RunningSessions: sessions.Count(s => s.EndedAt is null),
            SessionHours: Round(totalHours),
            AverageSessionMinutes: sessions.Count == 0 ? 0 : Round(totalHours * 60 / sessions.Count),
            Extensions: sessions.Sum(s => s.ExtensionCount),
            ByDay: days,
            VisitsByHour: ByHour(visits.Select(v => v.CreatedAt)),
            SessionsByHour: ByHour(sessions.Select(s => s.StartedAt)),
            ByAgeGroup: Count(visits.Select(v => v.AgeGroup)),
            BySex: Count(visits.Select(v => v.Sex)),
            BySector: Count(visits.SelectMany(v => v.Sectors.Select(s => s.Sector))),
            ByService: Count(visits.SelectMany(v => v.Services.Select(s => s.Service))),
            BySource: Count(visits.Select(v => v.Source)),
            SessionEndReasons: Count(sessions.Where(s => s.EndReason is not null).Select(s => s.EndReason!.Value)),
            ByComputer: computers.Select(c =>
            {
                var used = sessions.Where(s => s.ComputerId == c.Id).ToList();
                return new ReportComputerDto(c.Id, c.Name, used.Count, Round(used.Sum(Hours)));
            }).ToList(),
            Waitlist: new ReportWaitlistDto(
                Joined: waitlist.Count,
                GotAComputer: waitlist.Count(s => s == WaitlistStatus.Served),
                MissedTurn: waitlist.Count(s => s == WaitlistStatus.Missed),
                Expired: waitlist.Count(s => s == WaitlistStatus.Expired),
                Left: waitlist.Count(s => s == WaitlistStatus.Left)));
    }

    private DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    private static DateTime End(UsageSession s, DateTime now) =>
        s.EndedAt ?? (now < s.ScheduledEndAt ? now : s.ScheduledEndAt);

    private List<CountDto> ByHour(IEnumerable<DateTime> times)
    {
        var counts = new int[24];
        foreach (var t in times) counts[ToLocal(t).Hour]++;
        return counts.Select((n, hour) => new CountDto(hour.ToString(CultureInfo.InvariantCulture), n)).ToList();
    }

    // Every value of the enum, in declaration order (the form's order), with its count.
    private static List<CountDto> Count<T>(IEnumerable<T> values) where T : struct, Enum
    {
        var counts = values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        return Enum.GetValues<T>().Select(v => new CountDto(v.ToString(), counts.GetValueOrDefault(v))).ToList();
    }

    private static double Round(double value) => Math.Round(value, 1);

    private Task<List<Visit>> VisitsAsync(DateTime fromUtc, DateTime toUtc) =>
        _db.Visits.AsNoTracking()
            .Include(v => v.Sectors).Include(v => v.Services)
            .Where(v => v.CreatedAt >= fromUtc && v.CreatedAt < toUtc)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync();

    // Sessions are counted on the day they started.
    private Task<List<UsageSession>> SessionsAsync(DateTime fromUtc, DateTime toUtc) =>
        _db.Sessions.AsNoTracking()
            .Include(s => s.Computer).Include(s => s.Visit)
            .Where(s => s.StartedAt >= fromUtc && s.StartedAt < toUtc)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();

    // ---- Exports ---------------------------------------------------------------

    // The tables an export can hold. Visits and Sessions list people by name.
    public static readonly string[] Tables = { "summary", "days", "visits", "sessions" };
    public static bool HasNames(string table) => table is "visits" or "sessions";

    private sealed record Table(string Name, string[] Headers, List<object?[]> Rows);

    // One CSV table. UTF-8 with a BOM so Excel shows "ñ" and other accents correctly.
    public async Task<byte[]> CsvAsync(DateOnly from, DateOnly to, string table)
    {
        var t = (await TablesAsync(from, to, new[] { table }))[0];
        var sb = new StringBuilder();
        const string newLine = "\r\n"; // CSV uses CRLF on every system
        sb.Append(string.Join(",", t.Headers.Select(CsvCell))).Append(newLine);
        foreach (var row in t.Rows) sb.Append(string.Join(",", row.Select(CsvCell))).Append(newLine);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    // An Excel workbook with one sheet per table.
    public async Task<byte[]> ExcelAsync(DateOnly from, DateOnly to, bool withNames)
    {
        var names = withNames ? Tables : Tables.Where(t => !HasNames(t)).ToArray();
        var tables = await TablesAsync(from, to, names);

        using var book = new XLWorkbook();
        foreach (var t in tables)
        {
            var sheet = book.Worksheets.Add(t.Name);
            for (var c = 0; c < t.Headers.Length; c++) sheet.Cell(1, c + 1).Value = t.Headers[c];
            sheet.Row(1).Style.Font.Bold = true;

            for (var r = 0; r < t.Rows.Count; r++)
            {
                for (var c = 0; c < t.Rows[r].Length; c++)
                {
                    var cell = sheet.Cell(r + 2, c + 1);
                    switch (t.Rows[r][c])
                    {
                        case null: break;
                        case int i: cell.Value = i; break;
                        case double d: cell.Value = d; break;
                        case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; break;
                        case DateOnly day: cell.Value = day.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                        case var other: cell.Value = other.ToString(); break; // text, never a formula
                    }
                }
            }

            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(1, Math.Min(t.Rows.Count + 1, 200), 8, 60);
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<List<Table>> TablesAsync(DateOnly from, DateOnly to, IEnumerable<string> names)
    {
        var (fromUtc, toUtc) = Bounds(from, to);
        var summary = await SummaryAsync(from, to);
        var result = new List<Table>();

        foreach (var name in names)
        {
            switch (name)
            {
                case "summary":
                    result.Add(SummaryTable(summary));
                    break;

                case "days":
                    result.Add(new Table("By day", new[] { "Date", "Visits", "PC sessions", "PC hours" },
                        summary.ByDay.Select(d => new object?[] { d.Day, d.Visits, d.Sessions, d.SessionHours }).ToList()));
                    break;

                case "visits":
                    var visits = await VisitsAsync(fromUtc, toUtc);
                    result.Add(new Table("Visits",
                        new[] { "Date and time", "First name", "Last name", "Age", "Sex", "Sector", "Purpose / services availed", "Registered by" },
                        visits.Select(v => new object?[]
                        {
                            ToLocal(v.CreatedAt), v.FirstName, v.LastName, Labels.Of(v.AgeGroup), Labels.Of(v.Sex),
                            string.Join(", ", v.Sectors.Select(s => s.Sector).OrderBy(s => s).Select(Labels.Of)),
                            string.Join(", ", v.Services.Select(s => s.Service).OrderBy(s => s).Select(Labels.Of)),
                            Labels.Of(v.Source)
                        }).ToList()));
                    break;

                case "sessions":
                    var now = _clock.UtcNow();
                    var sessions = await SessionsAsync(fromUtc, toUtc);
                    result.Add(new Table("PC sessions",
                        new[] { "PC", "Visitor", "Started", "Ended", "Minutes", "Extensions", "How it ended" },
                        sessions.Select(s => new object?[]
                        {
                            s.Computer?.Name, s.Visit?.FullName, ToLocal(s.StartedAt),
                            s.EndedAt is null ? null : ToLocal(s.EndedAt.Value),
                            (int)Math.Round((End(s, now) - s.StartedAt).TotalMinutes),
                            s.ExtensionCount,
                            s.EndReason is null ? "Still running" : Labels.Of(s.EndReason.Value)
                        }).ToList()));
                    break;

                default:
                    throw TrackerException.BadRequest($"Unknown table \"{name}\". Use one of: {string.Join(", ", Tables)}.");
            }
        }
        return result;
    }

    // The summary as one long table: section, item, value.
    private static Table SummaryTable(ReportDto r)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "Period", "From", r.From.ToString("yyyy-MM-dd") },
            new object?[] { "Period", "To", r.To.ToString("yyyy-MM-dd") },
            new object?[] { "Period", "Time zone", r.TimeZone },
            new object?[] { "Totals", "Visits", r.Visits },
            new object?[] { "Totals", "Visitors who used a PC", r.VisitsWithComputer },
            new object?[] { "Totals", "PC sessions", r.Sessions },
            new object?[] { "Totals", "PC hours", r.SessionHours },
            new object?[] { "Totals", "Average session (minutes)", r.AverageSessionMinutes },
            new object?[] { "Totals", "Extensions", r.Extensions },
        };
        void Add(string section, IEnumerable<CountDto> counts, Func<string, string> label)
        {
            foreach (var c in counts) rows.Add(new object?[] { section, label(c.Key), c.Count });
        }
        Add("Age", r.ByAgeGroup, k => Labels.Of(Enum.Parse<AgeGroup>(k)));
        Add("Sex", r.BySex, k => Labels.Of(Enum.Parse<Sex>(k)));
        Add("Sector", r.BySector, k => Labels.Of(Enum.Parse<Sector>(k)));
        Add("Purpose / services availed", r.ByService, k => Labels.Of(Enum.Parse<ServiceAvailed>(k)));
        Add("Registered by", r.BySource, k => Labels.Of(Enum.Parse<VisitSource>(k)));
        Add("Visits by hour", r.VisitsByHour, HourLabel);
        Add("PC sessions by hour", r.SessionsByHour, HourLabel);
        Add("How PC sessions ended", r.SessionEndReasons, k => Labels.Of(Enum.Parse<SessionEndReason>(k)));
        foreach (var pc in r.ByComputer)
        {
            rows.Add(new object?[] { "PC sessions", pc.Name, pc.Sessions });
            rows.Add(new object?[] { "PC hours", pc.Name, pc.Hours });
        }
        rows.Add(new object?[] { "Waitlist", "Joined", r.Waitlist.Joined });
        rows.Add(new object?[] { "Waitlist", "Got a PC", r.Waitlist.GotAComputer });
        rows.Add(new object?[] { "Waitlist", "Missed their turn", r.Waitlist.MissedTurn });
        rows.Add(new object?[] { "Waitlist", "Waited too long", r.Waitlist.Expired });
        rows.Add(new object?[] { "Waitlist", "Left the line", r.Waitlist.Left });
        return new Table("Summary", new[] { "Section", "Item", "Value" }, rows);
    }

    private static string HourLabel(string hour)
    {
        var h = int.Parse(hour, CultureInfo.InvariantCulture);
        return $"{h:00}:00-{h:00}:59";
    }

    // Quotes a CSV cell when needed. Text starting with = + - @ gets a leading ' so a
    // spreadsheet never runs it as a formula (a visitor could type one as their name).
    private static string CsvCell(object? value)
    {
        var text = value switch
        {
            null => "",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}

// The form's wording for each answer (matches the website's registration.js).
public static class Labels
{
    public static string Of(AgeGroup v) => v switch
    {
        AgeGroup.Age17AndUnder => "17 years old & under",
        AgeGroup.Age18To30 => "18-30 years old",
        AgeGroup.Age31To59 => "31-59 years old",
        AgeGroup.Age60AndAbove => "60 years old & above",
        _ => v.ToString()
    };

    public static string Of(Sex v) => v.ToString();

    public static string Of(Sector v) => v switch
    {
        Sector.Nga => "NGA",
        Sector.Lgu => "LGU",
        Sector.SeniorCitizen => "Senior Citizen",
        Sector.Msme => "MSMEs",
        Sector.IndigenousPeople => "Indigenous People",
        Sector.Pwd => "PWD",
        Sector.InformalSector => "Informal Sector",
        _ => v.ToString()
    };

    public static string Of(ServiceAvailed v) => v switch
    {
        ServiceAvailed.IctEquipment => "Use of ICT Equipment",
        ServiceAvailed.CoWorkingSpace => "Co-Working Space",
        ServiceAvailed.EgovServices => "eGov Services",
        _ => v.ToString()
    };

    public static string Of(VisitSource v) => v switch
    {
        VisitSource.Phone => "Own phone",
        VisitSource.Desk => "Help desk",
        VisitSource.Kiosk => "Staff, on the PC",
        _ => v.ToString()
    };

    public static string Of(SessionEndReason v) => v switch
    {
        SessionEndReason.Visitor => "Visitor ended it",
        SessionEndReason.Staff => "Ended by staff",
        SessionEndReason.Idle => "Idle logout",
        SessionEndReason.TimeLimit => "Time limit",
        _ => v.ToString()
    };
}
