using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Models;

namespace Hermes.Core.Import;

public static class PostmanEnvironmentConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static bool LooksLikeEnvironment(JsonNode? root) =>
        root is JsonObject o && o["values"] is JsonArray && o["name"] is JsonValue && o["item"] is null;

    public static EnvironmentModel Import(string json) =>
        Import(JsonNode.Parse(json) ?? throw new FormatException("Empty document."));

    public static EnvironmentModel Import(JsonNode root)
    {
        if (!LooksLikeEnvironment(root))
            throw new FormatException("Not a Postman environment.");
        return new EnvironmentModel
        {
            Name = PostmanCollectionImporter.Str(root["name"]) ?? "Imported environment",
            Variables = PostmanCollectionImporter.ReadVariables(root["values"]),
        };
    }

    public static string Export(EnvironmentModel environment, bool isGlobals = false)
    {
        var root = new JsonObject
        {
            ["id"] = environment.Id.ToString(),
            ["name"] = environment.Name,
            ["values"] = new JsonArray(environment.Variables.Select(v => (JsonNode?)new JsonObject
            {
                ["key"] = v.Key,
                ["value"] = v.Value,
                ["type"] = v.Type == VariableType.Secret ? "secret" : "default",
                ["enabled"] = v.Enabled,
            }).ToArray()),
            ["_postman_variable_scope"] = isGlobals ? "globals" : "environment",
            ["_postman_exported_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["_postman_exported_using"] = "Hermes",
        };
        return root.ToJsonString(WriteOptions);
    }
}
