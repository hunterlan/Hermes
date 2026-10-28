using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>Location of a node inside a collection.</summary>
public sealed record NodeLocation(CollectionModel Collection, IReadOnlyList<FolderModel> Ancestors, List<CollectionNode> Container, CollectionNode Node)
{
    public FolderModel? Parent => Ancestors.Count > 0 ? Ancestors[^1] : null;
}
