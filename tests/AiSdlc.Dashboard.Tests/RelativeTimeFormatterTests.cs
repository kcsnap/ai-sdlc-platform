using AiSdlc.Dashboard.Services;
using Xunit;

namespace AiSdlc.Dashboard.Tests;

public sealed class RelativeTimeFormatterTests
{
    // Fixed "now" so the tests are deterministic regardless of when they run.
    private static readonly DateTime Now = new(2026, 5, 18, 10, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Today_PrefixedWithToday()
    {
        var ts = new DateTimeOffset(2026, 5, 18, 9, 15, 22, 123, TimeSpan.Zero);
        Assert.Equal("Today 09:15:22.123", RelativeTimeFormatter.Format(ts, Now));
    }

    [Fact]
    public void Yesterday_PrefixedWithYesterday()
    {
        var ts = new DateTimeOffset(2026, 5, 17, 23, 59, 59, 999, TimeSpan.Zero);
        Assert.Equal("Yesterday 23:59:59.999", RelativeTimeFormatter.Format(ts, Now));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(7)]
    public void TwoToSevenDaysAgo_PrefixedWithDaysAgo(int daysAgo)
    {
        var thenDay = Now.AddDays(-daysAgo);
        var ts = new DateTimeOffset(thenDay.Year, thenDay.Month, thenDay.Day, 12, 0, 0, 500, TimeSpan.Zero);
        Assert.Equal($"{daysAgo} days ago 12:00:00.500", RelativeTimeFormatter.Format(ts, Now));
    }

    [Fact]
    public void EightDaysAgo_FallsBackToAbsoluteDate()
    {
        var thenDay = Now.AddDays(-8);
        var ts = new DateTimeOffset(thenDay.Year, thenDay.Month, thenDay.Day, 14, 22, 11, 345, TimeSpan.Zero);
        Assert.Equal("2026-05-10 14:22:11.345", RelativeTimeFormatter.Format(ts, Now));
    }

    [Fact]
    public void OldEvent_FallsBackToAbsoluteDate()
    {
        var ts = new DateTimeOffset(2025, 12, 25, 8, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("2025-12-25 08:00:00.000", RelativeTimeFormatter.Format(ts, Now));
    }

    [Fact]
    public void DayBoundary_IsByCalendarDateNotElapsedHours()
    {
        // 23 hours and 50 minutes ago, but still "Yesterday" because the calendar day differs.
        var ts = new DateTimeOffset(2026, 5, 17, 10, 40, 0, TimeSpan.Zero);
        Assert.Equal("Yesterday 10:40:00.000", RelativeTimeFormatter.Format(ts, Now));
    }
}
