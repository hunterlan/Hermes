using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>Writes a file the user picked (an export, a saved response), telling them why when it can't be written.</summary>
public static class UserFileWriter
{
    /// <summary>
    /// Runs <paramref name="write"/>. When the file can't be written, shows the reason and next step with the file
    /// to reveal, and returns false instead of letting the exception escape.
    /// </summary>
    /// <param name="title">Title of the dialog shown on failure, such as "Couldn't export".</param>
    /// <param name="path">The file being written.</param>
    public static async Task<bool> WriteAsync(DialogService dialogs, string title, string path, Func<Task> write)
    {
        try
        {
            await write();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var error = StorageErrorDescriber.Describe(ex, path, $"File \"{Path.GetFileName(path)}\"", StorageOperation.Write);
            await dialogs.ShowStorageErrorsAsync(title, [error]);
            return false;
        }
    }
}
