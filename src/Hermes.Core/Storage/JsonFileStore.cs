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

    /// <summary>Writes atomically: to a temp file first, then renamed over the target.</summary>
    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, value, typeInfo);
        File.Move(temp, path, overwrite: true);
    }
}
