using System.Globalization;
using System.Net;
using System.Text;
using TrackerApi.Models;

namespace TrackerApi.Services;

// A finished report as an HTML email: the checked text (if any) first, then tables built here
// from the counts, never by Gemini. Inline styles only: email clients ignore stylesheets.
// Rows with a count of 0 are left out; the dashboard has every day, hour and computer.
public static class ReportEmail
{
    private const string Td = "padding:6px 8px;border-bottom:1px solid #e6e9ee";
    private const string Th = "padding:6px 8px;border-bottom:1px solid #c9ced6;text-align:left;font-size:12px;color:#5b6470";

    private static string E(string s) => WebUtility.HtmlEncode(s);
    private static string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Html(ReportDocumentDto doc)
    {
        var r = doc.Current;
        var kind = doc.Kind switch { ReportKind.Day => "daily", ReportKind.Week => "weekly", _ => "monthly" };
        var range = r.From == r.To ? $"{r.From:yyyy-MM-dd}" : $"{r.From:yyyy-MM-dd} to {r.To:yyyy-MM-dd}";

        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.5;color:#1b1f24;max-width:640px\">");
        sb.Append($"<h2 style=\"font-size:18px;margin:0 0 2px\">MISA {kind} report: {E(range)}</h2>");
        sb.Append("<p style=\"margin:0 0 12px;color:#5b6470;font-size:12px\">DICT Digital Transformation Center, Baguio City");
        if (doc.Partial) sb.Append(". The period is not over yet: counts are up to this day");
        sb.Append("</p>");

        if (!string.IsNullOrWhiteSpace(doc.Narrative))
            sb.Append(Markdown(doc.Narrative));

        if (r.Visits == 0)
        {
            sb.Append("<p>No visits were logged for this period.</p>");
        }
        else
        {
            sb.Append(Comparison(doc));
            sb.Append(Groups(r));
            sb.Append(Computers(r));
        }

        sb.Append("<p style=\"margin:14px 0 0;color:#5b6470;font-size:12px\">Rows with a count of 0 are left out. " +
                  "Every day, hour and computer is in the MISA dashboard, Reports tab.</p></div>");
        return sb.ToString();
    }

    // The text uses only "## " headings and "- " bullets (see ReportAssistant.Instructions).
    private static string Markdown(string md)
    {
        var sb = new StringBuilder();
        var inList = false;
        foreach (var raw in md.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList) { sb.Append("<ul style=\"margin:0 0 10px;padding-left:20px\">"); inList = true; }
                sb.Append($"<li>{E(line[2..])}</li>");
                continue;
            }
            if (inList) { sb.Append("</ul>"); inList = false; }
            sb.Append(line.StartsWith("## ", StringComparison.Ordinal)
                ? $"<h3 style=\"font-size:15px;margin:14px 0 4px\">{E(line[3..])}</h3>"
                : $"<p style=\"margin:0 0 8px\">{E(line)}</p>");
        }
        if (inList) sb.Append("</ul>");
        return sb.ToString();
    }

    private static string Table(string title, string[] heads, IEnumerable<string[]> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append($"<h3 style=\"font-size:15px;margin:14px 0 4px\">{E(title)}</h3>");
        sb.Append("<table style=\"width:100%;border-collapse:collapse\"><tr>");
        for (var i = 0; i < heads.Length; i++)
            sb.Append($"<th style=\"{Th}{(IsText(heads[i]) ? "" : ";text-align:right")}\">{E(heads[i])}</th>");
        sb.Append("</tr>");
        foreach (var row in list)
        {
            sb.Append("<tr>");
            for (var i = 0; i < row.Length; i++)
                sb.Append($"<td style=\"{Td}{(IsText(heads[i]) ? "" : ";text-align:right")}\">{E(row[i])}</td>");
            sb.Append("</tr>");
        }
        return sb.Append("</table>").ToString();
    }

    // Columns that hold words, not numbers, stay left-aligned.
    private static bool IsText(string head) => head is "Measure" or "Group" or "Answer" or "Computer";

    private static string Comparison(ReportDocumentDto doc)
    {
        var rows = doc.Comparison.Where(c => c.Now != 0 || c.Before != 0).Select(c => new[]
        {
            c.Measure, N(c.Now), N(c.Before),
            (c.Change > 0 ? "+" : "") + N(c.Change) + (c.ChangePercent is { } p ? $" ({N(p)}%)" : "")
        });
        if (doc.Previous is { } prev && doc.Comparison.Count > 0)
            return Table($"Compared with the previous period ({prev.From:yyyy-MM-dd} to {prev.To:yyyy-MM-dd})",
                new[] { "Measure", "Now", "Before", "Change" }, rows);

        var r = doc.Current;
        return Table("Totals", new[] { "Measure", "Value" }, new[]
        {
            new[] { "Visits", N(r.Visits) },
            new[] { "Visitors who used a PC", N(r.VisitsWithComputer) },
            new[] { "PC sessions", N(r.Sessions) },
            new[] { "PC hours", N(r.SessionHours) },
        }.Where(x => x[1] != "0"));
    }

    private static string Groups(ReportDto r)
    {
        IEnumerable<string[]> Rows<T>(string group, IEnumerable<CountDto> counts, Func<T, string> label) where T : struct, Enum =>
            counts.Where(c => c.Count > 0).Select(c => new[]
            {
                group, label(Enum.Parse<T>(c.Key)), N(c.Count), N(Math.Round(c.Count * 100.0 / r.Visits, 1)) + "%"
            });

        var rows = Rows<AgeGroup>("Age", r.ByAgeGroup, Labels.Of)
            .Concat(Rows<Sex>("Sex", r.BySex, Labels.Of))
            .Concat(Rows<Sector>("Sector", r.BySector, Labels.Of))
            .Concat(Rows<ServiceAvailed>("Service", r.ByService, Labels.Of))
            .Concat(Rows<VisitSource>("Registered", r.BySource, Labels.Of));
        return Table("Who came and what they used", new[] { "Group", "Answer", "Visits", "Share" }, rows) +
               "<p style=\"margin:4px 0 0;color:#5b6470;font-size:12px\">A visitor can tick several sectors and services, so those shares can add up to more than 100%.</p>";
    }

    private static string Computers(ReportDto r) =>
        Table("Computers used", new[] { "Computer", "Sessions", "Hours" },
            r.ByComputer.Where(c => c.Sessions > 0).Select(c => new[] { c.Name, N(c.Sessions), N(c.Hours) }));
}
