using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// The HTML email is built from the counts by code; these check what a reader sees and that
// nothing typed into the data can become markup.
public class ReportEmailTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static ReportDocumentDto Doc(int visits, string narrative = "", string computerName = "PC-01", int sessions = 1)
    {
        var report = new ReportDto(D(2026, 11, 16), D(2026, 11, 22), "Asia/Manila", visits, 1, sessions, 0, 0.1, 2.6, 0,
            new(), new(), new(),
            new() { new("Age18To30", visits), new("Age60AndAbove", 0) },
            new() { new("Male", visits), new("Female", 0) },
            new() { new("Academe", 2), new("Youth", 0) },
            new() { new("IctEquipment", visits) },
            new() { new("Phone", visits) },
            new(),
            new() { new ReportComputerDto(1, computerName, sessions, 0.1), new ReportComputerDto(2, "PC-02", 0, 0) },
            new ReportWaitlistDto(0, 0, 0, 0, 0));
        var comparison = new List<ComparisonRowDto> { new("Visits", visits, 1, visits - 1, 300), new("Waitlist: joined", 0, 0, 0, null) };
        return new ReportDocumentDto(ReportKind.Week, false, report, report, comparison, narrative, true);
    }

    [Fact]
    public void Tables_list_only_rows_with_a_count()
    {
        var html = ReportEmail.Html(Doc(4));
        Assert.Contains("18-30 years old", html);
        Assert.DoesNotContain("60 years old", html); // count 0: left out
        Assert.DoesNotContain("Female", html);
        Assert.DoesNotContain("PC-02", html);        // no sessions
        Assert.DoesNotContain("Waitlist: joined", html);
        Assert.Contains("+3 (300%)", html);
    }

    [Fact]
    public void A_period_without_visits_says_so_and_has_no_tables()
    {
        var html = ReportEmail.Html(Doc(0));
        Assert.Contains("No visits were logged", html);
        Assert.DoesNotContain("<table", html);
    }

    [Fact]
    public void The_text_becomes_headings_and_bullets()
    {
        var html = ReportEmail.Html(Doc(4, "## Overview\n- First point\n- Second point"));
        Assert.Contains("<h3 style=\"font-size:15px;margin:14px 0 4px\">Overview</h3>", html);
        Assert.Contains("<li>First point</li><li>Second point</li>", html);
    }

    [Fact]
    public void Markup_in_the_text_or_a_computer_name_is_escaped()
    {
        var html = ReportEmail.Html(Doc(4, "- <script>alert(1)</script>", "<b>PC</b>"));
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>PC</b>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;PC&lt;/b&gt;", html);
    }
}
