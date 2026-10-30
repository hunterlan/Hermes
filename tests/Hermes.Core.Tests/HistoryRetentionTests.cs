using Hermes.Core.Models;
using Hermes.Core.Storage;
using Hermes.TestSupport;

namespace Hermes.Core.Tests;

public class HistoryRetentionTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-history-tests-" + Guid.NewGuid());
    private readonly HistoryStore _store;

    public HistoryRetentionTests()
    {
        _store = new HistoryStore(_dir, TestClock.NoonOn(Today));
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(7, 6, true)]
    [InlineData(7, 7, false)]
    public void Prune_keeps_today_as_one_of_the_days(int retentionDays, int daysOld, bool kept)
    {
        AppendOn(Today.AddDays(-daysOld));

        _store.Prune(retentionDays);

        Assert.Equal(kept, File.Exists(FileOn(Today.AddDays(-daysOld))));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1_000_000)]
    public void A_window_reaching_before_the_first_date_expires_nothing(int retentionDays)
    {
        Assert.Null(HistoryRetention.OldestKeptDay(retentionDays, Today));

        AppendOn(Today.AddDays(-10));
        _store.Prune(retentionDays);

        Assert.True(File.Exists(FileOn(Today.AddDays(-10))));
    }

    [Fact]
    public void Prune_deletes_only_expired_days()
    {
        for (var daysOld = 0; daysOld < 10; daysOld++)
            AppendOn(Today.AddDays(-daysOld));

        _store.Prune(3);

        Assert.Equal(
            [Today, Today.AddDays(-1), Today.AddDays(-2)],
            Directory.GetFiles(_dir).Select(DayOf).Order().Reverse());
    }

    [Fact]
    public void Prune_works_without_loading_history()
    {
        AppendOn(Today);
        AppendOn(Today.AddDays(-30));

        new HistoryStore(_dir, TestClock.NoonOn(Today)).Prune(7);

        var remaining = new HistoryStore(_dir).Load([]);
        Assert.Equal(Today, DateOnly.FromDateTime(Assert.Single(remaining).Timestamp.LocalDateTime));
    }

    [Fact]
    public void Zero_days_keeps_history_forever()
    {
        AppendOn(Today);
        AppendOn(Today.AddDays(-1000));

        _store.Prune(HistoryRetention.KeepForever);

        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    [Fact]
    public void Prune_follows_the_clock_as_days_pass()
    {
        var clock = TestClock.NoonOn(Today);
        var store = new HistoryStore(_dir, clock);
        AppendOn(Today);

        store.Prune(1);
        Assert.True(File.Exists(FileOn(Today)));

        clock.Advance(TimeSpan.FromDays(1));
        store.Prune(1);
        Assert.False(File.Exists(FileOn(Today)));
    }

    [Fact]
    public void Prune_ignores_a_missing_folder_and_other_files()
    {
        _store.Prune(1);

        Directory.CreateDirectory(_dir);
        var notes = Path.Combine(_dir, "notes.jsonl");
        File.WriteAllText(notes, "");
        _store.Prune(1);

        Assert.True(File.Exists(notes));
    }

    [Fact]
    public void Load_returns_every_day_and_leaves_expired_files_alone()
    {
        AppendOn(Today);
        AppendOn(Today.AddDays(-400));

        var loaded = _store.Load([]);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private void AppendOn(DateOnly day) =>
        _store.Append(new HistoryEntry { Timestamp = TestClock.NoonLocal(day) });

    private string FileOn(DateOnly day) => Path.Combine(_dir, day.ToString("yyyy-MM-dd") + ".jsonl");

    private static DateOnly DayOf(string file) => DateOnly.ParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd");
}
