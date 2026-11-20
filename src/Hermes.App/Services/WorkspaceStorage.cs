using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The one place where the workspace's writes are run and their problems reported. Each area of the workspace
/// (collections, environments, session, saved responses) writes through it, so a failure is described and raised the
/// same way wherever it happens.
/// Writes run off the UI thread, one at a time, in the order they were queued: a delete queued after a save runs after
/// it. A write must only use what it was given (a snapshot taken on the UI thread), never the live models.
/// </summary>
internal sealed class WorkspaceStorage
{
    private readonly BackgroundWriter _writes = new();

    /// <summary>Something couldn't be read or written. May be raised on a background thread.</summary>
    public event Action<StorageError>? StorageProblem;

    /// <summary>
    /// Queues a write and describes the failure of its file operation. Completes, with null when it worked, once the
    /// write and everything queued before it has run.
    /// </summary>
    public async Task<StorageError?> AttemptAsync(Action write, string path, string subject, StorageOperation operation,
        string? failedAction = null, StorageTarget target = StorageTarget.File)
    {
        try
        {
            // Not back to the caller's thread: the result needs nothing from it, and quitting waits for a write on it.
            await _writes.EnqueueAsync(write).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StorageErrorDescriber.Describe(ex, path, subject, operation, failedAction, target);
        }
    }

    /// <summary>Completes when everything queued so far has run (or has failed).</summary>
    public Task FlushAsync() => _writes.FlushAsync();

    /// <summary>Raises <see cref="StorageProblem"/> for <paramref name="error"/> if there is one. True when there was.</summary>
    public bool Report(StorageError? error)
    {
        if (error is null)
            return false;
        StorageProblem?.Invoke(error);
        return true;
    }
}
