using System.Text.Json;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;

namespace Hermes.Core.Serialization;

/// <summary>Deep copies and structural comparison via the JSON representation.</summary>
public static class ModelCloner
{
    public static HttpRequestModel Clone(HttpRequestModel model) =>
        JsonSerializer.Deserialize(Serialize(model), HermesJsonContext.Default.HttpRequestModel)!;

    public static FolderModel Clone(FolderModel model) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(model, HermesJsonContext.Default.FolderModel),
            HermesJsonContext.Default.FolderModel)!;

    public static CollectionModel Clone(CollectionModel model) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(model, HermesJsonContext.Default.CollectionModel),
            HermesJsonContext.Default.CollectionModel)!;

    public static EnvironmentModel Clone(EnvironmentModel model) =>
        JsonSerializer.Deserialize(Serialize(model), HermesJsonContext.Default.EnvironmentModel)!;

    public static AuthConfig Clone(AuthConfig model) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(model, HermesJsonContext.Default.AuthConfig),
            HermesJsonContext.Default.AuthConfig)!;

    public static string Serialize(HttpRequestModel model) =>
        JsonSerializer.Serialize(model, HermesJsonContext.Default.HttpRequestModel);

    public static string Serialize(EnvironmentModel model) =>
        JsonSerializer.Serialize(model, HermesJsonContext.Default.EnvironmentModel);

    /// <summary>Recursively clones a node, giving it and all descendants fresh ids (used by "Duplicate").</summary>
    public static CollectionNode Duplicate(CollectionNode node)
    {
        var json = JsonSerializer.Serialize(node, HermesJsonContext.Default.CollectionNode);
        var copy = JsonSerializer.Deserialize(json, HermesJsonContext.Default.CollectionNode)!;
        Reassign(copy);
        return copy;

        static void Reassign(CollectionNode n)
        {
            n.Id = Guid.NewGuid();
            if (n is FolderModel f)
                foreach (var child in f.Items)
                    Reassign(child);
        }
    }
}
