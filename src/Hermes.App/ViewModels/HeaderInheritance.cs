using Hermes.Core.Headers;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Builds the "Where headers come from" chain, as <see cref="AuthInheritance"/> does for auth.</summary>
public static class HeaderInheritance
{
    /// <summary>
    /// The levels from the collection down to <paramref name="container"/>, each listing the header names it adds; a
    /// name is struck through when the container or a level between sets it too. The container's node is the source.
    /// </summary>
    /// <param name="effective">The container's own rows and the rows it inherits.</param>
    /// <param name="collection">The collection above the container; null when the container is the collection.</param>
    /// <param name="folders">Folders between the collection and the container, outermost first.</param>
    /// <param name="container">The collection or folder whose tab is open.</param>
    public static List<InheritanceChainNode> Chain(EffectiveHeaders effective, CollectionModel? collection,
        IReadOnlyList<FolderModel> folders, HeaderSource container)
    {
        var levels = new List<HeaderSource>();
        if (collection is not null)
            levels.Add(new HeaderSource(HeaderSourceKind.Collection, collection.Id, collection.Name));
        levels.AddRange(folders.Select(f => new HeaderSource(HeaderSourceKind.Folder, f.Id, f.Name)));
        levels.Add(container);

        return levels.Select((level, index) =>
        {
            var isContainer = index == levels.Count - 1;
            var rows = isContainer ? effective.Own : effective.Inherited.Where(h => h.Source.Id == level.Id).ToList();
            var details = rows
                .Select(row => new InheritanceChainDetail(row.Key, row.State != HeaderState.Sent))
                .ToList();
            var status = rows.Count == 0 ? "No headers" : rows.Count == 1 ? "Adds 1 header" : $"Adds {rows.Count} headers";
            var kind = level.Kind == HeaderSourceKind.Collection ? "Collection" : "Folder";
            return new InheritanceChainNode($"{kind} · {level.Name}", status, isContainer, isContainer, details);
        }).ToList();
    }
}
