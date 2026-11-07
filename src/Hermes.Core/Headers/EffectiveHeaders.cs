using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>The headers of a request merged with those of its folders and collection.</summary>
/// <param name="Own">The request's ticked rows, all sent.</param>
/// <param name="Inherited">
/// Ticked rows of the collection and ancestor folders, outermost first, with whether each is sent, skipped or
/// overridden.
/// </param>
public sealed record EffectiveHeaders(IReadOnlyList<ResolvedHeader> Own, IReadOnlyList<ResolvedHeader> Inherited)
{
    /// <summary>The inherited rows that are sent, as written.</summary>
    public IEnumerable<KeyValueItem> InheritedToSend =>
        Inherited.Where(h => h.State == HeaderState.Sent).Select(h => h.Item);

    /// <summary>The rows to send, as written: the request's own, then the inherited ones.</summary>
    public IEnumerable<KeyValueItem> HeadersToSend => Own.Select(h => h.Item).Concat(InheritedToSend);

    /// <summary>The header sent instead of the one the effective auth would add, if any. The UI warns about it.</summary>
    public ResolvedHeader? AuthHeaderOverride => Own.Concat(Inherited).FirstOrDefault(h => h.ReplacesAuthHeader);
}
