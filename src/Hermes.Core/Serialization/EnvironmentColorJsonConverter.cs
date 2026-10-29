using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.Core.Models;

namespace Hermes.Core.Serialization;

/// <summary>
/// Writes the swatch name; reads it back case-insensitively. Anything it doesn't recognise (a swatch from a newer
/// version, a hand-edited typo, a number) loads as <see cref="EnvironmentColor.None"/> instead of failing the file.
/// </summary>
public sealed class EnvironmentColorJsonConverter : JsonConverter<EnvironmentColor>
{
    public override EnvironmentColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return EnvironmentColor.None;
        }
        var name = reader.GetString();
        // Matched by name rather than Enum.TryParse, which would also accept numeric strings such as "3".
        foreach (var color in Enum.GetValues<EnvironmentColor>())
        {
            if (string.Equals(color.ToString(), name, StringComparison.OrdinalIgnoreCase))
                return color;
        }
        return EnvironmentColor.None;
    }

    public override void Write(Utf8JsonWriter writer, EnvironmentColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
