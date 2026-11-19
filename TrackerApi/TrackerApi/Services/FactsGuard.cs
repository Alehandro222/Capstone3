using System.Text.Json;
using System.Text.RegularExpressions;
using TrackerApi.Models;

namespace TrackerApi.Services;

// The checker that runs BEFORE anything goes to Gemini, and again on what comes back, so that
// text cannot steer the model (prompt injection).
//
// The only free text in the facts is a computer's name, which an admin can type. So:
//  - ReportAssistant.Facts only passes a name that is one short word (PC-01); any other name is
//    sent as "Computer 7";
//  - Check() then walks the finished facts and refuses them if ANY text is not one of the fixed
//    labels, a date, an hour, the time zone, the centre's name or such a short computer name.
//    Gemini is not called for refused facts: the report is made without the written text.
//  - UnsafeDraft() refuses a draft that holds links, markup or code (a model that was steered
//    might add them); the website only shows the text as plain text, never as HTML.
public static class FactsGuard
{
    public const string Center = "DICT Digital Transformation Center, Baguio City";

    // A computer name Gemini may see: one word, 12 characters at most.
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9-]{1,12}$", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"^[a-z][A-Za-z]{0,40}$", RegexOptions.Compiled);
    private static readonly Regex Date = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);
    private static readonly Regex Hour = new(@"^\d{2}:\d{2}$", RegexOptions.Compiled);
    private static readonly Regex Zone = new(@"^[A-Za-z_]+(/[A-Za-z_]+)*$|^UTC[+-]\d{2}$", RegexOptions.Compiled);
    private static readonly Regex Unsafe = new(@"https?:|www\.|<|>|```|\[[^\]]*\]\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Every label the facts can carry (the form's wording for each answer).
    private static readonly HashSet<string> Labels_ = new(
        Enum.GetValues<AgeGroup>().Select(Labels.Of)
            .Concat(Enum.GetValues<Sex>().Select(Labels.Of))
            .Concat(Enum.GetValues<Sector>().Select(Labels.Of))
            .Concat(Enum.GetValues<ServiceAvailed>().Select(Labels.Of))
            .Concat(Enum.GetValues<VisitSource>().Select(Labels.Of))
            .Concat(Enum.GetValues<SessionEndReason>().Select(Labels.Of)),
        StringComparer.Ordinal);

    // The name that goes to Gemini for a computer.
    public static string NameFor(int computerId, string name) =>
        SafeName.IsMatch(name) ? name : $"Computer {computerId}";

    // null = the facts are fine. Else why not. (The text found is not repeated: it may be the attack.)
    public static string? Check(string factsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(factsJson);
            return Walk(doc.RootElement, null);
        }
        catch (JsonException)
        {
            return "the facts are not valid";
        }
    }

    private static string? Walk(JsonElement e, string? property)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (!KeyOk(p.Name)) return "a field name that is not expected";
                    if (Walk(p.Value, p.Name) is { } why) return why;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (Walk(item, property) is { } why) return why;
                return null;
            case JsonValueKind.String:
                return StringOk(e.GetString() ?? "", property) ? null : $"text that is not expected in \"{property}\"";
            default:
                return null; // numbers, true, false, null
        }
    }

    private static bool KeyOk(string key) =>
        Identifier.IsMatch(key) || Date.IsMatch(key) || Hour.IsMatch(key) || Labels_.Contains(key);

    private static bool StringOk(string s, string? property) =>
        property == "computer"
            ? SafeName.IsMatch(s) || Regex.IsMatch(s, @"^Computer \d{1,6}$")
            : property == "measure" ? ReportMath.Measures.Contains(s)
            : s == Center || Date.IsMatch(s) || Hour.IsMatch(s) || Labels_.Contains(s)
              || (property == "timeZone" && Zone.IsMatch(s));

    // null = the draft is fine. Else why not.
    public static string? UnsafeDraft(string draft) =>
        draft.Length > 8000 ? "the draft is far longer than a report"
        : Unsafe.IsMatch(draft) ? "the draft holds a link, markup or code"
        : null;
}
