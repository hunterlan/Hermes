using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hermes.Core.Storage;

public static class JsonFileStore
{
    private static readonly JsonWriterOptions SingleLine = new() { Indented = false };

    // Windows can't rename two files onto one target at once (access denied), so writers to a path take turns.
    private static readonly ConcurrentDictionary<string, Lock> WriteLocks = new(StringComparer.OrdinalIgnoreCase);

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
    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo) =>
        WriteAtomically(path, stream => JsonSerializer.Serialize(stream, value, typeInfo));

    // ---- JSON Lines: one value per line, so adding one doesn't rewrite the others ----

    /// <summary>Reads every line of a JSON Lines file. Lines that can't be read are skipped and described in <paramref name="errors"/>.</summary>
    public static List<T> ReadLines<T>(string path, JsonTypeInfo<T> typeInfo, ICollection<string> errors) where T : class
    {
        var items = new List<T>();
        if (!File.Exists(path))
            return items;
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonSerializer.Deserialize(line, typeInfo) is { } item)
                    items.Add(item);
            }
            catch (JsonException ex)
            {
                errors.Add($"{path}, line {number}: {ex.Message}");
            }
        }
        return items;
    }

    /// <summary>Appends one value as a line, flushed to disk. A line cut short by a crash is skipped on the next read.</summary>
    public static void AppendLine<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        WriteLine(stream, value, typeInfo);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Replaces a JSON Lines file atomically, like <see cref="Write{T}"/>.</summary>
    public static void WriteLines<T>(string path, IEnumerable<T> values, JsonTypeInfo<T> typeInfo) =>
        WriteAtomically(path, stream =>
        {
            foreach (var value in values)
                WriteLine(stream, value, typeInfo);
        });

    private static void WriteLine<T>(Stream stream, T value, JsonTypeInfo<T> typeInfo)
    {
        // A leading newline ends a line a crash may have left unfinished, so the new value is never glued to it.
        stream.WriteByte((byte)'\n');
        using (var writer = new Utf8JsonWriter(stream, SingleLine))
            JsonSerializer.Serialize(writer, value, typeInfo);
    }

    private static void WriteAtomically(string path, Action<Stream> write)
    {
        lock (WriteLocks.GetOrAdd(Path.GetFullPath(path), _ => new Lock()))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = $"{path}.{Path.GetRandomFileName()}.tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                {
                    write(stream);
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
    }

    // Cleanup after a failed write; the write's own error is the one worth reporting.
    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
