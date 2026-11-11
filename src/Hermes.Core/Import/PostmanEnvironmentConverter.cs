using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core.Models;
using static Hermes.Core.Import.PostmanFormat;

namespace Hermes.Core.Import;

public static class PostmanEnvironmentConverter
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static bool LooksLikeEnvironment(JsonNode? root) =>
        root is JsonObject o && o["values"] is JsonArray && o["name"] is JsonValue && o["item"] is null;

    public static EnvironmentModel Import(string json) =>
        Import(JsonNode.Parse(json) ?? throw new FormatException("Empty document."));

    /// <summary>
    /// True when the file is meant to be an environment even if it is malformed (it has <c>values</c> or a variable
    /// scope), so <see cref="Import(JsonNode)"/> can say what is wrong instead of the file being called unknown.
    /// </summary>
    public static bool IsMeantAsEnvironment(JsonNode? root) =>
        root is JsonObject o && o["item"] is null && (o["values"] is not null || o["_postman_variable_scope"] is not null);

    public static EnvironmentModel Import(JsonNode root)
    {
        if (root is not JsonObject)
            throw new ImportException("A Postman environment must be a JSON object");
        if (root["values"] is null)
            throw new ImportException("The environment has no `values` list");
        if (root["values"] is not JsonArray)
            throw new ImportException("`values` must be a list");
        if (root["name"] is null)
            throw new ImportException("The environment has no `name`");
        if (root["name"] is not JsonValue)
            throw new ImportException("`name` must be text");
        return new EnvironmentModel
        {
            Name = Str(root["name"]) ?? "Imported environment",
            Variables = ReadVariables(root["values"]),
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
                ["type"] = VariableTypeName(v.Type),
                ["enabled"] = v.Enabled,
            }).ToArray()),
            ["_postman_variable_scope"] = isGlobals ? "globals" : "environment",
            ["_postman_exported_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["_postman_exported_using"] = "Hermes",
        };
        return root.ToJsonString(WriteOptions);
    }
}
