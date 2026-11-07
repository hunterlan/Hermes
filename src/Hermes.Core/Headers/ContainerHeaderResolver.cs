using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>
/// Resolves the headers of a folder or collection against the levels outside it, for its Headers tab. It stands the
/// container in for a request, so the merge rules stay those of <see cref="HeaderResolver"/>.
/// </summary>
public static class ContainerHeaderResolver
{
    /// <param name="ownRows">The container's own header rows, as edited.</param>
    /// <param name="outerFolders">Folders above the container, outermost first. Empty for a collection.</param>
    /// <param name="collection">The collection above a folder; null when the container is the collection.</param>
    /// <returns>
    /// <see cref="EffectiveHeaders.Own"/> holds the container's ticked rows, flagged when they override an outer
    /// header; <see cref="EffectiveHeaders.Inherited"/> the outer rows, overridden where the container sets the name.
    /// </returns>
    public static EffectiveHeaders Resolve(IEnumerable<KeyValueItem> ownRows, IReadOnlyList<FolderModel> outerFolders,
        CollectionModel? collection)
    {
        var containerAsRequest = new HttpRequestModel { Headers = ownRows.ToList() };
        return HeaderResolver.Resolve(containerAsRequest, outerFolders, collection);
    }
}
