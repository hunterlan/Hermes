namespace Hermes.Core.Storage;

/// <summary>
/// Keeps a data folder to one running app: two instances would overwrite each other's files and race to set up
/// the folder's keychain entry. The lock is an open, unshared lock file, released when disposed or when the
/// process ends however it ends.
/// </summary>
public sealed class DataFolderLock : IDisposable
{
    private readonly FileStream _file;

    private DataFolderLock(FileStream file) => _file = file;

    public static string FileFor(string dataDirectory) => Path.Combine(dataDirectory, ".lock");

    /// <summary>Takes the folder's lock; null when another instance holds it.</summary>
    public static DataFolderLock? TryAcquire(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        try
        {
            return new DataFolderLock(new FileStream(FileFor(dataDirectory), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _file.Dispose();
}
