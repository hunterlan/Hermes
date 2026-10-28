using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;

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
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(SavedResponse))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
[JsonSerializable(typeof(Hermes.Core.Storage.WorkspaceInfo))]
public sealed partial class HermesJsonContext : JsonSerializerContext;
