namespace Hermes.Core.Storage;

/// <summary>
/// Checks at startup that the subfolders Hermes reads and writes under the data folder can be used, so a folder
/// that can't be listed or written to is reported before anything loads from it.
/// </summary>
public static class DataFolderCheck
{
    /// <summary>How the collections folder is named in messages.</summary>
    public const string CollectionsSubject = "Collections folder";

    /// <summary>How the environments folder is named in messages.</summary>
    public const string EnvironmentsSubject = "Environments folder";

    private static readonly CheckedSubfolder[] Subfolders =
    [
        new(WorkspaceRepository.CollectionsFolderName, CollectionsSubject, "your collections stay"),
        new(WorkspaceRepository.EnvironmentsFolderName, EnvironmentsSubject, "your environments stay"),
        new(WorkspaceRepository.HistoryFolderName, "History folder", "your history stays"),
        new(WorkspaceRepository.ResponsesFolderName, "Saved responses folder", "your saved responses stay"),
    ];

    /// <summary>
    /// The first existing subfolder of <paramref name="dataDirectory"/> that can't be listed or written to, or null
    /// when all can. A missing subfolder is fine: it is created when first needed.
    /// </summary>
    public static StorageError? FindUnusableSubfolder(string dataDirectory)
    {
        foreach (var subfolder in Subfolders)
        {
            var folder = Path.Combine(dataDirectory, subfolder.FolderName);
            if (!Directory.Exists(folder))
                continue;
            try
            {
                Probe(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var error = StorageErrorDescriber.Describe(ex, folder, $"{subfolder.Subject} \"{folder}\"", StorageOperation.Use);
                return error with { NextStep = NextStepFor(error, subfolder.StaysHere) };
            }
        }
        return null;
    }

    private static string NextStepFor(StorageError error, string staysHere)
    {
        return error.Kind == StorageErrorKind.PermissionDenied
            ? $"Fix the folder's permissions and restart Hermes, or choose another data folder ({staysHere} in this one)."
            : $"{error.NextStep} Or choose another data folder ({staysHere} in this one).";
    }

    // Listing and creating files are what Hermes does there; a probe file is the only way to know writing works.
    private static void Probe(string folder)
    {
        using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
        entries.MoveNext();
        var probeFile = Path.Combine(folder, $".probe-{Guid.NewGuid():N}");
        File.WriteAllBytes(probeFile, []);
        File.Delete(probeFile);
    }

    /// <param name="StaysHere">What stays in the folder, as a clause: <c>your collections stay</c>.</param>
    private sealed record CheckedSubfolder(string FolderName, string Subject, string StaysHere);
}
