using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>What a container's headers reach: the requests and subfolders inside it, and which requests override them.</summary>
/// <param name="RequestCount">Requests anywhere inside the container.</param>
/// <param name="SubfolderCount">Folders anywhere inside the container.</param>
/// <param name="Overriding">Requests where a nearer level sets one of the container's header names.</param>
public sealed record ContainerHeaderUsage(int RequestCount, int SubfolderCount, IReadOnlyList<HeaderOverride> Overriding)
{
    /// <summary>The header names of the container that some request overrides.</summary>
    public IReadOnlyList<string> OverriddenNames =>
        Overriding.SelectMany(o => o.HeaderNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <param name="containerId">The collection's or folder's id, as in <see cref="HeaderSource.Id"/>.</param>
    /// <param name="items">The container's children.</param>
    /// <param name="outerFolders">Folders above the container, outermost first. Empty for a collection.</param>
    /// <param name="containerFolder">The container when it is a folder; null for a collection.</param>
    public static ContainerHeaderUsage Compute(Guid containerId, IEnumerable<CollectionNode> items,
        IReadOnlyList<FolderModel> outerFolders, CollectionModel collection, FolderModel? containerFolder)
    {
        var path = containerFolder is null ? outerFolders : [.. outerFolders, containerFolder];
        var requestCount = 0;
        var subfolderCount = 0;
        var overriding = new List<HeaderOverride>();
        Walk(items, path);
        return new ContainerHeaderUsage(requestCount, subfolderCount, overriding);

        void Walk(IEnumerable<CollectionNode> nodes, IReadOnlyList<FolderModel> folders)
        {
            foreach (var node in nodes)
            {
                if (node is FolderModel folder)
                {
                    subfolderCount++;
                    Walk(folder.Items, [.. folders, folder]);
                }
                else if (node is HttpRequestModel request)
                {
                    requestCount++;
                    var overridden = HeaderResolver.Resolve(request, folders, collection).Inherited
                        .Where(h => h.Source.Id == containerId && h.State == HeaderState.Overridden)
                        .Select(h => h.Key).ToList();
                    if (overridden.Count > 0)
                        overriding.Add(new HeaderOverride(request, overridden));
                }
            }
        }
    }
}
