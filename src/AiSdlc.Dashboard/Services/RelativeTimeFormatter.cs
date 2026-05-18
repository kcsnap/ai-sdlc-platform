namespace AiSdlc.Dashboard.Services;

// Renders a UTC timestamp as "Today HH:mm:ss.fff", "Yesterday HH:mm:ss.fff",
// "N days ago HH:mm:ss.fff" (2..7 days), or "yyyy-MM-dd HH:mm:ss.fff" beyond 7 days.
// All comparisons are done against UTC "now" — there's no per-user time zone in the dashboard.
public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset timestamp) =>
        Format(timestamp, DateTime.UtcNow);

    // Overload for tests — caller supplies "now" so it's deterministic.
    public static string Format(DateTimeOffset timestamp, DateTime utcNow)
    {
        var nowDay  = utcNow.Date;
        var thenDay = timestamp.UtcDateTime.Date;
        var diff    = (nowDay - thenDay).Days;
        var time    = timestamp.UtcDateTime.ToString("HH:mm:ss.fff");

        return diff switch
        {
            0                  => $"Today {time}",
            1                  => $"Yesterday {time}",
            >= 2 and <= 7      => $"{diff} days ago {time}",
            _                  => timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff")
        };
    }
}
