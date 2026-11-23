using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The parts of a generated report that must be exactly right, as plain functions of the data:
// which days a day / week / month covers, the comparison with the previous period, the text
// written without Gemini, and the check that a draft uses no number that is not in the data.
public static class ReportMath
{
    // The whole period (Manila calendar days) that contains `anchor`.
    // A week runs Monday to Sunday (a Philippine office week).
    public static (DateOnly From, DateOnly To) Of(ReportKind kind, DateOnly anchor)
    {
        switch (kind)
        {
            case ReportKind.Day:
                return (anchor, anchor);
            case ReportKind.Week:
                var monday = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7));
                return (monday, monday.AddDays(6));
            default:
                var first = new DateOnly(anchor.Year, anchor.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));
        }
    }

    // The last day of the period before the one that contains `anchor` (last month, for a monthly report).
    public static DateOnly DayBefore(ReportKind kind, DateOnly anchor) => Of(kind, anchor).From.AddDays(-1);

    // The period to compare with: the whole one before, or cut to the same number of days when this one
    // is not over yet (so "Nov 1-10" is compared with "Oct 1-10", not with all of October).
    public static (DateOnly From, DateOnly To) Previous(ReportKind kind, DateOnly from, DateOnly to)
    {
        var whole = Of(kind, from);
        var (prevFrom, prevEnd) = Of(kind, whole.From.AddDays(-1));
        if (to >= whole.To) return (prevFrom, prevEnd); // this one is complete: compare with the whole previous period
        var prevTo = prevFrom.AddDays(to.DayNumber - from.DayNumber);
        return (prevFrom, prevTo < prevEnd ? prevTo : prevEnd);
    }

    // ---- Comparison ------------------------------------------------------------

    // The names of the comparison rows (the checker lets exactly these through as "measure").
    public static readonly string[] Measures =
    {
        "Visits", "Visitors who used a PC", "PC sessions", "PC hours", "Average session (minutes)", "Waitlist: joined"
    };

    public static List<ComparisonRowDto> Compare(ReportDto now, ReportDto before)
    {
        static ComparisonRowDto Row(string measure, double a, double b) => new(
            measure, a, b, Math.Round(a - b, 1), b == 0 ? null : Math.Round((a - b) * 100 / b, 1));
        return new List<ComparisonRowDto>
        {
            Row(Measures[0], now.Visits, before.Visits),
            Row(Measures[1], now.VisitsWithComputer, before.VisitsWithComputer),
            Row(Measures[2], now.Sessions, before.Sessions),
            Row(Measures[3], now.SessionHours, before.SessionHours),
            Row(Measures[4], now.AverageSessionMinutes, before.AverageSessionMinutes),
            Row(Measures[5], now.Waitlist.Joined, before.Waitlist.Joined),
        };
    }

    // Each answer as a share of all visits, one decimal. (Sectors and services can add up to
    // more than 100%: a visitor can tick several.)
    public static Dictionary<string, double> Percents(IEnumerable<CountDto> counts, int visits, Func<string, string> label) =>
        visits == 0
            ? new()
            : counts.ToDictionary(c => label(c.Key), c => Math.Round(c.Count * 100.0 / visits, 1));

    public static (DateOnly Day, int Visits)? BusiestDay(ReportDto r)
    {
        var best = r.ByDay.OrderByDescending(d => d.Visits).ThenBy(d => d.Day).FirstOrDefault();
        return best is null || best.Visits == 0 ? null : (best.Day, best.Visits);
    }

    public static (string Hour, int Visits)? BusiestHour(ReportDto r)
    {
        var best = r.VisitsByHour.OrderByDescending(h => h.Count).ThenBy(h => int.Parse(h.Key, CultureInfo.InvariantCulture)).FirstOrDefault();
        return best is null || best.Count == 0 ? null : ($"{int.Parse(best.Key, CultureInfo.InvariantCulture):00}:00", best.Count);
    }

    // ---- Text written by code (daily reports, and the fallback when Gemini is not used) ------

    public static string PlainNarrative(ReportDto r, IReadOnlyList<ComparisonRowDto>? comparison)
    {
        var period = r.From == r.To ? FormattableString.Invariant($"{r.From:MMMM d, yyyy}") : FormattableString.Invariant($"{r.From:MMMM d, yyyy} to {r.To:MMMM d, yyyy}");
        var lines = new List<string> { "## Summary" };
        lines.Add(r.Visits == 0
            ? FormattableString.Invariant($"- No visits were logged for {period}.")
            : FormattableString.Invariant($"- {r.Visits} visit(s) were logged for {period}; {r.VisitsWithComputer} of the visitors used a computer, in {r.Sessions} session(s) totalling {r.SessionHours} hour(s)."));
        if (BusiestHour(r) is { } hour) lines.Add(FormattableString.Invariant($"- The busiest hour was {hour.Hour}, with {hour.Visits} visit(s)."));
        if (r.Waitlist.Joined > 0)
            lines.Add(FormattableString.Invariant($"- {r.Waitlist.Joined} people joined the waitlist; {r.Waitlist.GotAComputer} got a computer."));
        if (comparison?.FirstOrDefault(c => c.Measure == "Visits") is { } v)
            lines.Add(FormattableString.Invariant($"- Visits were {v.Now} compared with {v.Before} in the previous period."));
        return string.Join("\n", lines);
    }

    // ---- Number check ------------------------------------------------------------

    private static readonly Regex Number = new(@"\d+(?:,\d{3})*(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex ListMarker = new(@"^\s*\d+[.)]\s",RegexOptions.Multiline | RegexOptions.Compiled);

    private static string Canonical(decimal d) => Math.Abs(d).ToString("0.############", CultureInfo.InvariantCulture);

    private static IEnumerable<string> Numbers(string text) =>
        Number.Matches(text).Select(m => Canonical(decimal.Parse(m.Value.Replace(",", ""), CultureInfo.InvariantCulture)));

    // The numbers in `draft` that are not figures in `facts` (the exact JSON sent to Gemini).
    // Empty = every figure in the draft is one of the data's numbers.
    //  - A figure must be a number value in the facts (a count, hours, a percentage ...), written
    //    as digits exactly as given: 12.5 matches 12.5, 13 does not. A minus sign is ignored.
    //  - Dates, hours of the day, answer labels and PC names that contain digits ("2026-11-01",
    //    "09:00", "18-30 years old", "PC-01") are checked as whole strings that appear in the facts,
    //    so the "11" of a date can't pass as 11 visits. The year of a date is allowed.
    // This proves a figure exists in the data; it cannot tell whether it is used for the right
    // thing (visits swapped with sessions): staff still read the report.
    public static List<string> UnknownNumbers(string draft, string facts)
    {
        var figures = new HashSet<string>();
        var literals = new HashSet<string>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(facts))
            Collect(doc.RootElement, figures, literals);
        foreach (var l in literals.ToList())
            foreach (Match year in Regex.Matches(l, @"(?<!\d)\d{4}(?!\d)")) figures.Add(year.Value);

        // Take the quoted literals out of the draft (longest first), then check what is left.
        var rest = ListMarker.Replace(draft, "");
        foreach (var l in literals.OrderByDescending(l => l.Length)) rest = rest.Replace(l, " ");
        return Numbers(rest).Where(n => !figures.Contains(n)).Distinct()
            .OrderBy(n => decimal.Parse(n, CultureInfo.InvariantCulture)).ToList();
    }

    private static void Collect(JsonElement e, HashSet<string> figures, HashSet<string> literals)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number:
                figures.Add(Canonical(e.GetDecimal()));
                break;
            case JsonValueKind.String:
                if (e.GetString() is { } s && s.Any(char.IsDigit)) literals.Add(s);
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Collect(item, figures, literals);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name.Any(char.IsDigit)) literals.Add(p.Name);
                    Collect(p.Value, figures, literals);
                }
                break;
        }
    }
}
