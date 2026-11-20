using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>The session: open tabs with their drafts, the active environment and the layout.</summary>
internal sealed class WorkspaceSession(WorkspaceRepository repository, WorkspaceStorage storage)
{
    private const string Subject = "Session (open tabs and drafts)";

    private bool _failureReported;

    public SessionState Load() => repository.LoadSession();

    /// <inheritdoc cref="WorkspaceService.SaveSession"/>
    public void Save(SessionState session)
    {
        var error = TrySave(session);
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

    /// <inheritdoc cref="WorkspaceService.TrySaveSession"/>
    public StorageError? TrySave(SessionState session) =>
        WorkspaceStorage.Attempt(() => repository.SaveSession(session), repository.SessionFile, Subject,
            StorageOperation.Write);
}
