using VibeDesk.Application.Scripting;
using Xunit;

namespace VibeDesk.Tests.Scripting;

/// <summary>
/// Cron parsing and next-firing. The cases that matter are the ones a scheduler gets wrong quietly:
/// the day-of-month/day-of-week OR rule, step syntax, and never firing twice inside one minute.
/// </summary>
public class CronScheduleTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso).ToUniversalTime();

    private static DateTimeOffset? Next(string expression, string from)
    {
        Assert.True(CronSchedule.TryParse(expression, out var schedule), $"'{expression}' should parse");

        return schedule!.Next(At(from), Utc);
    }

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 1,15 * *")]
    [InlineData("@daily")]
    [InlineData("@hourly")]
    [InlineData("@weekly")]
    public void AcceptsValidExpressions(string expression) =>
        Assert.True(CronSchedule.TryParse(expression, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("* * * *")]          // four fields
    [InlineData("* * * * * *")]      // six fields
    [InlineData("60 * * * *")]       // minute out of range
    [InlineData("* 24 * * *")]       // hour out of range
    [InlineData("* * 0 * *")]        // day-of-month starts at 1
    [InlineData("*/0 * * * *")]      // zero step
    [InlineData("banana")]
    public void RejectsInvalidExpressions(string? expression) =>
        Assert.False(CronSchedule.TryParse(expression, out _));

    [Fact]
    public void FiresAtTheNextWholeMinuteNeverTwiceInTheSameOne()
    {
        // Mid-minute: the next firing is the following minute, not this one again.
        var next = Next("* * * * *", "2026-03-10T08:15:30Z");

        Assert.Equal(At("2026-03-10T08:16:00Z"), next);
    }

    [Fact]
    public void DailyScheduleRollsToTomorrowOncePassed()
    {
        Assert.Equal(At("2026-03-11T09:00:00Z"), Next("0 9 * * *", "2026-03-10T09:00:00Z"));
        Assert.Equal(At("2026-03-10T09:00:00Z"), Next("0 9 * * *", "2026-03-10T08:59:00Z"));
    }

    [Fact]
    public void WeekdayScheduleSkipsTheWeekend()
    {
        // 2026-03-13 is a Friday; the next weekday firing is Monday the 16th.
        Assert.Equal(At("2026-03-16T09:00:00Z"), Next("0 9 * * 1-5", "2026-03-13T09:30:00Z"));
    }

    [Fact]
    public void SundayIsAcceptedAsBothZeroAndSeven()
    {
        var asZero = Next("0 9 * * 0", "2026-03-10T00:00:00Z");
        var asSeven = Next("0 9 * * 7", "2026-03-10T00:00:00Z");

        Assert.Equal(asZero, asSeven);
        Assert.Equal(DayOfWeek.Sunday, asZero!.Value.DayOfWeek);
    }

    [Fact]
    public void StepSyntaxFiresOnTheInterval()
    {
        Assert.Equal(At("2026-03-10T08:15:00Z"), Next("*/15 * * * *", "2026-03-10T08:02:00Z"));
        Assert.Equal(At("2026-03-10T08:30:00Z"), Next("*/15 * * * *", "2026-03-10T08:15:00Z"));
    }

    [Fact]
    public void DayOfMonthAndWeekdayAreOrdedNotAnded()
    {
        // Standard cron: with both day fields restricted, either match fires. 2026-03-01 is a Sunday,
        // so "1st of the month OR any Monday" must fire on the 1st even though it is not a Monday.
        var next = Next("0 0 1 * 1", "2026-02-25T00:00:00Z");

        Assert.Equal(At("2026-03-01T00:00:00Z"), next);
    }

    [Fact]
    public void ImpossibleDateReturnsNullRatherThanLoopingForever()
    {
        // 30 February never arrives; the search gives up after a year instead of spinning.
        Assert.True(CronSchedule.TryParse("0 0 30 2 *", out var schedule));

        Assert.Null(schedule!.Next(At("2026-01-01T00:00:00Z"), Utc));
    }

    [Fact]
    public void NextRunPushesABrokenExpressionADayOutInsteadOfRetryingEveryTick()
    {
        var now = At("2026-03-10T08:00:00Z");

        Assert.Equal(now.AddDays(1), CronSchedule.NextRun("not a cron", "UTC", now));
    }

    [Fact]
    public void UnknownTimeZoneFallsBackToUtcRatherThanThrowing() =>
        Assert.Equal(TimeZoneInfo.Utc, CronSchedule.ResolveZone("Not/AZone"));

    [Fact]
    public void ScheduleIsInterpretedInItsOwnZone()
    {
        Assert.True(CronSchedule.TryParse("0 9 * * *", out var schedule));

        var jakarta = CronSchedule.ResolveZone("Asia/Jakarta");
        var next = schedule!.Next(At("2026-03-10T00:00:00Z"), jakarta);

        // 09:00 in UTC+7 is 02:00 UTC — the point of storing a zone with the trigger.
        Assert.Equal(At("2026-03-10T02:00:00Z"), next);
    }
}
