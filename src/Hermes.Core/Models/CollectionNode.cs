using System.Text.Json.Serialization;

namespace Hermes.Core.Models;

/// <summary>An entry in a collection tree: either a folder or a request.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(FolderModel), "folder")]
[JsonDerivedType(typeof(HttpRequestModel), "request")]
public abstract class CollectionNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Postman pre-request/test scripts, kept verbatim (JSON) for lossless round trips.</summary>
    public string? EventsJson { get; set; }
}

public sealed class FolderModel : CollectionNode
{
    public List<CollectionNode> Items { get; set; } = [];
    public AuthConfig Auth { get; set; } = AuthConfig.Inherit();
}
