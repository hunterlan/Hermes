namespace Hermes.TestSupport;

/// <summary>
/// Makes a folder read-only for a test (nothing can be created in it, renamed or deleted) and gives its permissions
/// back on dispose, so the temp folder can be deleted afterwards. Uses Unix modes; check <see cref="IsEffective"/>
/// before relying on it, since Windows and root ignore them.
/// </summary>
public sealed class ReadOnlyFolder : IDisposable
{
    private const UnixFileMode ReadOnly = UnixFileMode.UserRead | UnixFileMode.UserExecute;
    private const UnixFileMode Writable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly string _path;

    private ReadOnlyFolder(string path)
    {
        _path = path;
        if (OperatingSystem.IsWindows())
            return;
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, ReadOnly);
    }

    /// <summary>Creates the folder if it's missing and makes it read-only.</summary>
    public static ReadOnlyFolder Create(string path) => new(path);

    /// <summary>False when the folder can still be written to anyway, so a test of a refused write would prove nothing.</summary>
    public bool IsEffective
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return false;
            var probe = Path.Combine(_path, Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }

    /// <summary>Makes the folder writable again while the test goes on, such as before pressing Retry.</summary>
    public void Allow()
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, Writable);
    }

    public void Dispose() => Allow();
}
