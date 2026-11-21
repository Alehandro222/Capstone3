using TrackerApi.Models;
using TrackerApi.Services;

namespace TrackerApi.Tests;

// Periods (in Manila calendar days), the comparison, and the check that a draft's numbers
// are in the data.
public class ReportMathTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void A_week_runs_monday_to_sunday()
    {
        // 2026-11-07 is a Saturday; 2026-11-08 a Sunday; 2026-11-09 a Monday.
        Assert.Equal((D(2026, 11, 2), D(2026, 11, 8)), ReportMath.Of(ReportKind.Week, D(2026, 11, 7)));
        Assert.Equal((D(2026, 11, 2), D(2026, 11, 8)), ReportMath.Of(ReportKind.Week, D(2026, 11, 8)));
        Assert.Equal((D(2026, 11, 9), D(2026, 11, 15)), ReportMath.Of(ReportKind.Week, D(2026, 11, 9)));
        Assert.Equal((D(2026, 12, 28), D(2027, 1, 3)), ReportMath.Of(ReportKind.Week, D(2027, 1, 1))); // across a year
    }

    [Fact]
    public void A_month_runs_from_the_first_to_the_last_day()
    {
        Assert.Equal((D(2026, 11, 1), D(2026, 11, 30)), ReportMath.Of(ReportKind.Month, D(2026, 11, 14)));
        Assert.Equal((D(2028, 2, 1), D(2028, 2, 29)), ReportMath.Of(ReportKind.Month, D(2028, 2, 3))); // leap year
        Assert.Equal((D(2026, 12, 5), D(2026, 12, 5)), ReportMath.Of(ReportKind.Day, D(2026, 12, 5)));
    }

    [Fact]
    public void Previous_means_the_last_day_of_the_period_before()
    {
        // The flow runs on the 1st and asks for "last month": the day before the 1st is in last month.
        Assert.Equal(D(2026, 10, 31), ReportMath.DayBefore(ReportKind.Month, D(2026, 11, 1)));
        Assert.Equal(D(2026, 10, 31), ReportMath.DayBefore(ReportKind.Month, D(2026, 11, 19)));
        Assert.Equal(D(2026, 2, 28), ReportMath.DayBefore(ReportKind.Month, D(2026, 3, 9)));
        Assert.Equal(D(2025, 12, 31), ReportMath.DayBefore(ReportKind.Month, D(2026, 1, 15)));
        Assert.Equal(D(2026, 11, 8), ReportMath.DayBefore(ReportKind.Week, D(2026, 11, 12))); // the Sunday before
        Assert.Equal(D(2026, 11, 6), ReportMath.DayBefore(ReportKind.Day, D(2026, 11, 7)));
        // ... and that day belongs to the whole of last month:
        Assert.Equal((D(2026, 10, 1), D(2026, 10, 31)), ReportMath.Of(ReportKind.Month, ReportMath.DayBefore(ReportKind.Month, D(2026, 11, 1))));
    }

    [Fact]
    public void The_previous_period_has_the_same_length_when_this_one_is_not_over()
    {
        // A whole month against the whole month before.
        Assert.Equal((D(2026, 10, 1), D(2026, 10, 31)), ReportMath.Previous(ReportKind.Month, D(2026, 11, 1), D(2026, 11, 30)));
        // November 1-10 against October 1-10.
        Assert.Equal((D(2026, 10, 1), D(2026, 10, 10)), ReportMath.Previous(ReportKind.Month, D(2026, 11, 1), D(2026, 11, 10)));
        // March 1-31 against February (28 days): cut at the end of February.
        Assert.Equal((D(2026, 2, 1), D(2026, 2, 28)), ReportMath.Previous(ReportKind.Month, D(2026, 3, 1), D(2026, 3, 31)));
        // Monday-Wednesday of this week against Monday-Wednesday of the last.
        Assert.Equal((D(2026, 11, 2), D(2026, 11, 4)), ReportMath.Previous(ReportKind.Week, D(2026, 11, 9), D(2026, 11, 11)));
        Assert.Equal((D(2026, 11, 6), D(2026, 11, 6)), ReportMath.Previous(ReportKind.Day, D(2026, 11, 7), D(2026, 11, 7)));
    }

    [Fact]
    public void The_comparison_is_computed_and_has_no_percent_when_there_was_nothing_before()
    {
        static ReportDto R(int visits, double hours) => new(D(2026, 11, 1), D(2026, 11, 1), "Asia/Manila", visits, 0, 0, 0, hours, 0, 0,
            new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new ReportWaitlistDto(0, 0, 0, 0, 0));
        var rows = ReportMath.Compare(R(15, 7.5), R(12, 0));
        var visits = rows.Single(r => r.Measure == "Visits");
        Assert.Equal((15, 12, 3, 25), (visits.Now, visits.Before, visits.Change, visits.ChangePercent));
        var hours = rows.Single(r => r.Measure == "PC hours");
        Assert.Equal(7.5, hours.Change);
        Assert.Null(hours.ChangePercent);
    }

    [Theory]
    [InlineData("- 2 visits, 12.5 hours.", "{ \"visits\": 2, \"hours\": 12.5 }", "")]
    [InlineData("- 1,234 visits.", "{ \"visits\": 1234 }", "")]             // a thousands comma is the same number
    [InlineData("- 12.50 hours.", "{ \"hours\": 12.5 }", "")]                 // trailing zero is the same number
    [InlineData("- 13 hours.", "{ \"hours\": 12.5 }", "13")]                  // rounding is not allowed
    [InlineData("- Busiest at 09:00.", "{ \"hour\": \"09:00\" }", "")]
    [InlineData("- 3 visits, 7 hours, 3 again.", "{ \"visits\": 3 }", "7")]
    [InlineData("1. First point\n2. Second point\n- 3 visits", "{ \"v\": 3 }", "")] // list numbering is not a figure
    [InlineData("- Down by 5.", "{ \"change\": -5 }", "")]                       // the sign is ignored
    [InlineData("- 17 and 5", "{ \"a\": 5 }", "17")]
    // Small invented numbers must not hide in the digits of dates, hours, labels or PC names.
    [InlineData("- 5 visits on 2026-11-01.", "{ \"d\": { \"2026-11-01\": 0 }, \"visits\": 2 }", "5")]
    [InlineData("- 2 visits on 2026-11-01.", "{ \"d\": { \"2026-11-01\": 0 }, \"visits\": 2 }", "")]
    [InlineData("- 11 visits.", "{ \"d\": { \"2026-11-01\": 0 }, \"visits\": 2 }", "11")]
    [InlineData("- 1 visit on 2026-11-01.", "{ \"d\": { \"2026-11-01\": 0 }, \"visits\": 2 }", "1")]
    [InlineData("- In November 2026 there were 2 visits.", "{ \"d\": { \"2026-11-01\": 0 }, \"visits\": 2 }", "")]
    [InlineData("- 18-30 years old: 2.", "{ \"ageGroup\": { \"18-30 years old\": 2 } }", "")]
    [InlineData("- 30 years old: 2.", "{ \"ageGroup\": { \"18-30 years old\": 2 } }", "30")]
    [InlineData("- PC-01 had 2 hours.", "{ \"computers\": [ { \"computer\": \"PC-01\", \"hours\": 2 } ] }", "")]
    [InlineData("- PC-07 had 2 hours.", "{ \"computers\": [ { \"computer\": \"PC-01\", \"hours\": 2 } ] }", "7")]
    public void A_draft_may_only_use_numbers_that_are_in_the_data(string draft, string facts, string unknown)
    {
        Assert.Equal(unknown, string.Join(",", ReportMath.UnknownNumbers(draft, facts)));
    }

    [Theory]
    [InlineData("{ \"error\": { \"code\": 503, \"message\": \"The model is overloaded.\\nPlease try again later.\" } }", ": The model is overloaded. Please try again later.")]
    [InlineData("not json", "")]
    [InlineData("{ \"error\": { \"code\": 503 } }", "")]
    [InlineData("", "")]
    public void Googles_error_message_is_shown_short_and_on_one_line(string json, string expected)
    {
        Assert.Equal(expected, ReportAssistant.Reason(json));
    }

    [Fact]
    public void A_very_long_google_message_is_cut()
    {
        var reason = ReportAssistant.Reason("{ \"error\": { \"message\": \"" + new string('x', 500) + "\" } }");
        Assert.True(reason.Length < 210);
        Assert.EndsWith("…", reason);
    }
}
