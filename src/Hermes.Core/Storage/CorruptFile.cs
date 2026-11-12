namespace Hermes.Core.Storage;

/// <summary>Sets a file Hermes can't read out of the way, without deleting what is in it.</summary>
public static class CorruptFile
{
    private const string Extension = ".corrupt";

    /// <summary>
    /// Renames <paramref name="path"/> to <c>{path}.corrupt</c> (<c>.corrupt2</c>, <c>.corrupt3</c> … when that name
    /// is taken) so the next start finds nothing there, and returns the new path.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be renamed.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its folder isn't writable.</exception>
    public static string MoveAside(string path)
    {
        var target = path + Extension;
        for (var attempt = 2; File.Exists(target); attempt++)
            target = $"{path}{Extension}{attempt}";
        File.Move(path, target);
        return target;
    }
}
