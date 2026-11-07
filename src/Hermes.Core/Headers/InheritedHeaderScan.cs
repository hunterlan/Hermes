using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>Finds the collection and folder headers that <see cref="HeaderResolver"/> sends to requests.</summary>
public static class InheritedHeaderScan
{
    /// <summary>
    /// True when the collection or any folder nested in it has a ticked header row with a name. Those are the rows a
    /// Postman export copies into each request, so the user should know before exporting.
    /// </summary>
    public static bool HasTickedHeaders(CollectionModel collection) =>
        HasTickedRow(collection.Headers) || FoldersHaveTickedRow(collection.Items);

    private static bool FoldersHaveTickedRow(IEnumerable<CollectionNode> nodes) =>
        nodes.OfType<FolderModel>().Any(folder => HasTickedRow(folder.Headers) || FoldersHaveTickedRow(folder.Items));

    private static bool HasTickedRow(IEnumerable<KeyValueItem> rows) =>
        rows.Any(row => row.Enabled && row.Key.Trim().Length > 0);
}
