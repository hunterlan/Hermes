using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The session: open tabs with their drafts, the active environment and the layout. It is written off the UI thread,
/// after the writes queued before it, from a copy taken when the save is asked for.
/// </summary>
internal sealed class WorkspaceSession(WorkspaceRepository repository, WorkspaceStorage storage)
{
    private const string Subject = "Session (open tabs and drafts)";

    private bool _failureReported;

    public SessionState Load() => repository.LoadSession();

    /// <inheritdoc cref="WorkspaceService.SaveSessionAsync"/>
    public async Task SaveAsync(SessionState session)
    {
        var error = await TrySaveAsync(session);
        if (error is null)
        {
            _failureReported = false;
            return;
        }
        if (_failureReported)
            return;
        _failureReported = true;
        storage.Report(error);
    }

    /// <inheritdoc cref="WorkspaceService.TrySaveSessionAsync"/>
    public Task<StorageError?> TrySaveAsync(SessionState session)
    {
        var snapshot = ModelCloner.Clone(session);
        return storage.AttemptAsync(() => repository.SaveSession(snapshot), repository.SessionFile, Subject,
            StorageOperation.Write);
    }
}
