using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.Core.Models;

namespace Hermes.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(CollectionModel))]
[JsonSerializable(typeof(EnvironmentModel))]
[JsonSerializable(typeof(HttpRequestModel))]
[JsonSerializable(typeof(FolderModel))]
[JsonSerializable(typeof(CollectionNode))]
[JsonSerializable(typeof(AuthConfig))]
[JsonSerializable(typeof(List<Variable>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SessionState))]
public sealed partial class HermesJsonContext : JsonSerializerContext;
