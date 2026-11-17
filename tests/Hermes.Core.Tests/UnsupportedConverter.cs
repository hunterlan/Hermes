using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Core.Tests;

/// <summary>A converter that can't read anything, like a serializer setup problem that has nothing to do with the file.</summary>
internal sealed class UnsupportedConverter : JsonConverter<Uri>
{
    public override Uri Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("This converter can't read anything.");

    public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options) =>
        throw new NotSupportedException("This converter can't write anything.");
}
