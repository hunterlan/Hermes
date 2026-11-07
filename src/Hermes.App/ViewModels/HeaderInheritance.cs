using Hermes.Core.Headers;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Builds the "Where headers come from" chain, as <see cref="AuthInheritance"/> does for auth.</summary>
public static class HeaderInheritance
{
    /// <summary>
    /// The levels from the collection down to the leaf (a request, or the container itself when it has none to show),
    /// each listing the header names it adds; a name is struck through when it isn't sent: a nearer level sets it too, or the
    /// request skips it.
    /// </summary>
    /// <param name="effective">The leaf's own rows and the rows it inherits.</param>
    /// <param name="collection">The collection above the leaf; null when the leaf is the collection itself.</param>
    /// <param name="folders">Folders between the collection and the leaf, outermost first.</param>
    /// <param name="highlightedId">The container whose tab is open: its node is marked as the source.</param>
    public static List<InheritanceChainNode> Chain(EffectiveHeaders effective, CollectionModel? collection,
        IReadOnlyList<FolderModel> folders, HeaderSource leaf, Guid highlightedId)
    {
        var levels = new List<HeaderSource>();
        if (collection is not null)
            levels.Add(new HeaderSource(HeaderSourceKind.Collection, collection.Id, collection.Name));
        levels.AddRange(folders.Select(f => new HeaderSource(HeaderSourceKind.Folder, f.Id, f.Name)));
        levels.Add(leaf);

        return levels.Select((level, index) =>
        {
            var rows = level.Id == leaf.Id ? effective.Own : effective.Inherited.Where(h => h.Source.Id == level.Id).ToList();
            var details = rows
                .Select(row => new InheritanceChainDetail(row.Key, row.State != HeaderState.Sent))
                .ToList();
            var status = rows.Count == 0 ? "No headers" : rows.Count == 1 ? "Adds 1 header" : $"Adds {rows.Count} headers";
            return new InheritanceChainNode($"{KindLabel(level.Kind)} · {level.Name}", status, level.Id == highlightedId,
                index == levels.Count - 1, details);
        }).ToList();
    }

    private static string KindLabel(HeaderSourceKind kind) => kind switch
    {
        HeaderSourceKind.Collection => "Collection",
        HeaderSourceKind.Folder => "Folder",
        _ => "Request",
    };
}
