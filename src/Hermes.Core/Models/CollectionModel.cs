using Hermes.Core.Models.Auth;

namespace Hermes.Core.Models;

public sealed class CollectionModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<CollectionNode> Items { get; set; } = [];
    public AuthConfig Auth { get; set; } = AuthConfig.NoAuth();

    /// <summary>Sent with every request in the collection, unless a folder or the request sets the same header.</summary>
    public List<KeyValueItem> Headers { get; set; } = [];

    public List<Variable> Variables { get; set; } = [];
    public string? EventsJson { get; set; }

    /// <summary>The original <c>_postman_id</c> when imported, reused on export.</summary>
    public string? PostmanId { get; set; }
}
