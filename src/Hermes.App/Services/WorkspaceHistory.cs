using Avalonia.Threading;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>The history of sent requests, trimmed to the retention setting at start, on a settings change and each midnight.</summary>
internal sealed class WorkspaceHistory : IDisposable
{
    // Fires a little after midnight so the new day has certainly started.
    private static readonly TimeSpan MidnightMargin = TimeSpan.FromSeconds(1);

    private readonly WorkspaceRepository _repository;
    private readonly SettingsService _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _dayChangeTimer;

    // History is written in the background, in order, so a clear always lands after the appends before it.
    private readonly BackgroundWriter _writer = new();

    private bool _disposed;

    public WorkspaceHistory(WorkspaceRepository repository, WorkspaceStorage storage, SettingsService settings,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _settings = settings;
        _timeProvider = timeProvider;
        _writer.Failed += ex => storage.Report(StorageErrorDescriber.Describe(ex,
            Path.Combine(repository.Root, "history"), "History", StorageOperation.Write, target: StorageTarget.Folder));
        Entries = new HistoryCollection(repository.LoadHistory());
        ApplyRetention();
        _settings.Changed += ApplyRetention;
        _dayChangeTimer = timeProvider.CreateTimer(_ => OnDayChanged(), null, UntilNextMidnight(), Timeout.InfiniteTimeSpan);
    }

    public HistoryCollection Entries { get; }

    public void Add(HistoryEntry entry)
    {
        Entries.Insert(0, entry);
        _writer.Enqueue(() => _repository.AppendHistory(entry));
    }

    public void Delete(HistoryEntry entry)
    {
        Entries.Remove(entry);
        _writer.Enqueue(() => _repository.DeleteHistoryEntry(entry));
    }

    public void Clear()
    {
        Entries.Clear();
        _writer.Enqueue(_repository.ClearHistory);
    }

    /// <summary>Completes when queued history writes are on disk (or have failed).</summary>
    public Task FlushAsync() => _writer.FlushAsync();

    /// <summary>
    /// Drops history older than the retention setting from the sidebar now and deletes its files on the history
    /// writer, after the appends already queued. Must run on the UI thread, which owns <see cref="Entries"/>.
    /// </summary>
    private void ApplyRetention()
    {
        var retentionDays = _settings.Current.HistoryRetentionDays;
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var oldestKeptDay = HistoryRetention.OldestKeptDay(retentionDays, today);
        Entries.RemoveWhere(entry => HistoryRetention.IsExpired(entry.Timestamp, oldestKeptDay));
        _writer.Enqueue(() => _repository.PruneHistory(retentionDays));
    }

    private TimeSpan UntilNextMidnight()
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        return now.Date.AddDays(1) - now + MidnightMargin;
    }

    // The timer fires on a thread-pool thread; the entries belong to the UI thread.
    private void OnDayChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;
            ApplyRetention();
            _dayChangeTimer.Change(UntilNextMidnight(), Timeout.InfiniteTimeSpan);
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _settings.Changed -= ApplyRetention;
        _dayChangeTimer.Dispose();
    }
}
