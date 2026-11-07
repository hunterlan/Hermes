using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>
/// Merges the headers of a request's collection and ancestor folders into the request, as
/// <see cref="Auth.AuthResolver"/> does for auth. Nearest wins: the request, then the innermost folder out to the
/// collection, matching names ignoring case. Unticked rows (and rows without a name) are ignored: not sent, and they
/// don't hide an outer header of the same name. Works on the rows as written; variables are resolved by whoever sends
/// them, so an export can keep them.
/// </summary>
public static class HeaderResolver
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <param name="folders">Ancestor folders, outermost first.</param>
    /// <param name="authHeaderName">
    /// The header the effective auth would add (see <see cref="Auth.AuthApplier.HeaderName"/>), to flag a header sent
    /// in its place. Null when the auth adds none.
    /// </param>
    public static EffectiveHeaders Resolve(HttpRequestModel request, IReadOnlyList<FolderModel> folders,
        CollectionModel? collection, string? authHeaderName = null)
    {
        // Inherited levels, outermost first.
        var levels = new List<(HeaderSource Source, List<KeyValueItem> Rows)>();
        if (collection is not null)
            levels.Add((new HeaderSource(HeaderSourceKind.Collection, collection.Id, collection.Name), Ticked(collection.Headers)));
        foreach (var folder in folders)
            levels.Add((new HeaderSource(HeaderSourceKind.Folder, folder.Id, folder.Name), Ticked(folder.Headers)));

        // namesOutside[i]: the names set by the levels outside level i.
        var namesOutside = new List<HashSet<string>>();
        var inheritedNames = new HashSet<string>(NameComparer);
        foreach (var (_, rows) in levels)
        {
            namesOutside.Add(new HashSet<string>(inheritedNames, NameComparer));
            inheritedNames.UnionWith(rows.Select(NameOf));
        }

        bool IsAuthHeader(string name) => authHeaderName is not null && NameComparer.Equals(name, authHeaderName.Trim());

        var requestSource = new HeaderSource(HeaderSourceKind.Request, request.Id, request.Name);
        var own = Ticked(request.Headers)
            .Select(row => new ResolvedHeader(row, requestSource, HeaderState.Sent, null,
                OverridesOuter: inheritedNames.Contains(NameOf(row)), ReplacesAuthHeader: IsAuthHeader(NameOf(row))))
            .ToList();

        // Walk innermost first; a name is claimed by the nearest level that sets it, and hidden further out.
        var claimedBy = new Dictionary<string, HeaderSource>(NameComparer);
        foreach (var row in own)
            claimedBy.TryAdd(row.Key, requestSource);
        var skipped = request.SkippedInheritedHeaders.Select(name => name.Trim()).ToHashSet(NameComparer);

        var inheritedByLevel = new List<ResolvedHeader>[levels.Count];
        for (var i = levels.Count - 1; i >= 0; i--)
        {
            var (source, rows) = levels[i];
            inheritedByLevel[i] = rows.Select(row =>
            {
                var name = NameOf(row);
                var overridden = claimedBy.TryGetValue(name, out var overriddenBy);
                var state = overridden ? HeaderState.Overridden
                    : skipped.Contains(name) ? HeaderState.Skipped
                    : HeaderState.Sent;
                return new ResolvedHeader(row, source, state, overridden ? overriddenBy : null,
                    OverridesOuter: namesOutside[i].Contains(name),
                    ReplacesAuthHeader: state == HeaderState.Sent && IsAuthHeader(name));
            }).ToList();
            // Claimed after the whole level, so repeated rows within one level are all sent, as on a request.
            foreach (var row in rows)
                claimedBy.TryAdd(NameOf(row), source);
        }

        return new EffectiveHeaders(own, inheritedByLevel.SelectMany(level => level).ToList());
    }

    private static List<KeyValueItem> Ticked(IEnumerable<KeyValueItem> rows) =>
        rows.Where(row => row.Enabled && NameOf(row).Length > 0).ToList();

    private static string NameOf(KeyValueItem row) => row.Key.Trim();
}
