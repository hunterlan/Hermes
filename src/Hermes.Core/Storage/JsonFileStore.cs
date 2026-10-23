using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hermes.Core.Storage;

public static class JsonFileStore
{
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (!File.Exists(path))
            return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, typeInfo);
    }

    /// <summary>
    /// Writes atomically: to a temp file of its own (so concurrent writers never share one), flushed to disk, then
    /// renamed over the target. A failed write leaves the target untouched and no temp file behind.
    /// </summary>
    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Path.GetRandomFileName()}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, value, typeInfo);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    // Cleanup after a failed write; the write's own error is the one worth reporting.
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
