using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hermes.App.Services;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Hermes.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Hermes.App.Tests;

public class HistoryRetentionTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-history-ui-tests-" + Guid.NewGuid());
    private readonly FakeTimeProvider _clock = TestClock.NoonOn(Today);
    private readonly SettingsService _settings = new();
    private readonly AppSettings _originalSettings;

    public HistoryRetentionTests()
    {
        _originalSettings = _settings.Current;
    }

    [AvaloniaFact]
    public async Task Lowering_retention_in_settings_removes_expired_entries_at_once()
    {
        using var workspace = CreateWorkspace(retentionDays: 30, daysOld: [0, 1, 5, 20]);
        Assert.Equal(4, workspace.History.Count);

        SaveRetention(7);
        await workspace.FlushHistoryAsync();

        Assert.Equal([0, 1, 5], DaysOld(workspace));
        Assert.Equal(3, Directory.GetFiles(HistoryDirectory).Length);
    }

    [AvaloniaFact]
    public async Task Raising_retention_or_keeping_forever_removes_nothing()
    {
        using var workspace = CreateWorkspace(retentionDays: 30, daysOld: [0, 1, 5, 20]);

        SaveRetention(90);
        SaveRetention(HistoryRetention.KeepForever);
        await workspace.FlushHistoryAsync();

        Assert.Equal([0, 1, 5, 20], DaysOld(workspace));
        Assert.Equal(4, Directory.GetFiles(HistoryDirectory).Length);
    }

    [AvaloniaFact]
    public async Task Entries_beyond_the_retention_are_dropped_on_startup()
    {
        using var workspace = CreateWorkspace(retentionDays: 2, daysOld: [0, 1, 2, 3]);
        await workspace.FlushHistoryAsync();

        Assert.Equal([0, 1], DaysOld(workspace));
        Assert.Equal(2, Directory.GetFiles(HistoryDirectory).Length);
    }

    [AvaloniaFact]
    public async Task Entries_expire_when_the_day_changes_while_the_app_is_open()
    {
        using var workspace = CreateWorkspace(retentionDays: 2, daysOld: [0, 1]);
        Assert.Equal(2, workspace.History.Count);
        // Let the prune queued at startup finish on the starting day, so it can't be what removes the files below.
        await workspace.FlushHistoryAsync();

        // Noon to just after the next midnight.
        _clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        await workspace.FlushHistoryAsync();

        Assert.Equal([0], DaysOld(workspace));
        Assert.Single(Directory.GetFiles(HistoryDirectory));

        // The timer is set again for the following midnight.
        _clock.Advance(TimeSpan.FromDays(1));
        Dispatcher.UIThread.RunJobs();
        await workspace.FlushHistoryAsync();

        Assert.Empty(workspace.History);
        Assert.Empty(Directory.GetFiles(HistoryDirectory));
    }

    [AvaloniaFact]
    public async Task A_disposed_workspace_no_longer_prunes()
    {
        var workspace = CreateWorkspace(retentionDays: 1, daysOld: [0]);
        workspace.Dispose();
        // The prune queued at startup works out "today" when it runs, so let it finish before time moves on.
        await workspace.FlushHistoryAsync();

        _clock.Advance(TimeSpan.FromDays(2));
        Dispatcher.UIThread.RunJobs();
        SaveRetention(1);
        await workspace.FlushHistoryAsync();

        Assert.Single(workspace.History);
        Assert.Single(Directory.GetFiles(HistoryDirectory));
    }

    public void Dispose()
    {
        _settings.Update(_originalSettings);
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string HistoryDirectory => Path.Combine(_dir, "history");

    private WorkspaceService CreateWorkspace(int retentionDays, int[] daysOld)
    {
        var repository = new WorkspaceRepository(_dir, timeProvider: _clock);
        foreach (var days in daysOld)
            repository.AppendHistory(new HistoryEntry { Timestamp = TestClock.NoonLocal(Today.AddDays(-days)) });
        SaveRetention(retentionDays);
        return new WorkspaceService(repository, _settings, _clock);
    }

    private void SaveRetention(int retentionDays) =>
        _settings.Update(new AppSettings { HistoryRetentionDays = retentionDays });

    private int[] DaysOld(WorkspaceService workspace) =>
        workspace.History.Select(entry => Today.DayNumber - DateOnly.FromDateTime(entry.Timestamp.LocalDateTime).DayNumber).ToArray();
}
