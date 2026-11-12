using System.Text.Json;

namespace Hermes.Core.Storage;

/// <summary>
/// Finds the top-level <c>name</c> of a JSON file that can't be parsed completely, so a message can name the
/// collection or environment instead of its GUID file name. Reads as far as the file is valid.
/// </summary>
internal static class JsonNameProbe
{
    public static string? TryReadName(string path)
    {
        try
        {
            var reader = new Utf8JsonReader(File.ReadAllBytes(path), isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isName = reader.ValueTextEquals("name"u8);
                if (!reader.Read())
                    return null;
                if (isName)
                    return reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } name ? name : null;
                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // The file is damaged or unreadable; a name found before that point was already returned above.
        }
        return null;
    }
}
