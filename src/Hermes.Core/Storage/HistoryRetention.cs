namespace Hermes.Core.Storage;

/// <summary>
/// How many days of history are kept. Today is one of the days, so 1 keeps only today and 7 keeps today and the
/// 6 days before it. 0 keeps everything.
/// </summary>
public static class HistoryRetention
{
    public const int KeepForever = 0;

    /// <summary>
    /// The earliest day still kept, or null when nothing expires: for <see cref="KeepForever"/> and for a window
    /// that reaches back before <see cref="DateOnly.MinValue"/>.
    /// </summary>
    public static DateOnly? OldestKeptDay(int retentionDays, DateOnly today)
    {
        var daysBeforeToday = (long)retentionDays - 1;
        if (retentionDays <= KeepForever || daysBeforeToday > today.DayNumber)
            return null;
        return today.AddDays(-(int)daysBeforeToday);
    }

    /// <summary>
    /// Whether the entry's day, in the machine's local time zone (the zone its file is named in), is before
    /// <paramref name="oldestKeptDay"/>. Pass the day from a clock that uses the same zone.
    /// </summary>
    public static bool IsExpired(DateTimeOffset timestamp, DateOnly? oldestKeptDay) =>
        oldestKeptDay is { } oldest && DateOnly.FromDateTime(timestamp.LocalDateTime) < oldest;
}
