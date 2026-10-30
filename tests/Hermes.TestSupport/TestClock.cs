using Microsoft.Extensions.Time.Testing;

namespace Hermes.TestSupport;

public static class TestClock
{
    /// <summary>A clock stopped at noon on <paramref name="day"/> in the machine's local time zone, like the files history writes.</summary>
    public static FakeTimeProvider NoonOn(DateOnly day)
    {
        // FakeTimeProvider reads the start's clock time as UTC and ignores its offset, so hand it UTC.
        var clock = new FakeTimeProvider(NoonLocal(day).ToUniversalTime());
        clock.SetLocalTimeZone(TimeZoneInfo.Local);
        return clock;
    }

    /// <summary>Noon on <paramref name="day"/> in the machine's local time zone.</summary>
    public static DateTimeOffset NoonLocal(DateOnly day) =>
        new(day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified));
}
