using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The one place where the workspace's writes are attempted and their problems reported. Each area of the workspace
/// (collections, environments, history, session, saved responses) writes through it, so a failure is described and
/// raised the same way wherever it happens.
/// </summary>
internal sealed class WorkspaceStorage
{
    /// <summary>Something couldn't be read or written. May be raised on a background thread.</summary>
    public event Action<StorageError>? StorageProblem;

    /// <summary>Runs a write and describes the failure of a file operation. Null when it worked.</summary>
    public static StorageError? Attempt(Action write, string path, string subject, StorageOperation operation,
        string? failedAction = null)
    {
        try
        {
            write();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StorageErrorDescriber.Describe(ex, path, subject, operation, failedAction);
        }
    }

    /// <summary>Raises <see cref="StorageProblem"/> for <paramref name="error"/> if there is one. True when there was.</summary>
    public bool Report(StorageError? error)
    {
        if (error is null)
            return false;
        StorageProblem?.Invoke(error);
        return true;
    }
}
