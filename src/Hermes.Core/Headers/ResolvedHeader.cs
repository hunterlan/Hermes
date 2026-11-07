using Hermes.Core.Models;

namespace Hermes.Core.Headers;

/// <summary>A ticked header row of the request, a folder or the collection, and what becomes of it.</summary>
/// <param name="Item">The row as written (the same instance, variables unresolved).</param>
/// <param name="OverriddenBy">The request or inner folder that sets the same header, when overridden.</param>
/// <param name="OverridesOuter">An outer folder or the collection sets a header of the same name.</param>
/// <param name="ReplacesAuthHeader">
/// Sent under the name of the header the effective auth would add, so the auth header is not sent.
/// </param>
public sealed record ResolvedHeader(
    KeyValueItem Item,
    HeaderSource Source,
    HeaderState State,
    HeaderSource? OverriddenBy,
    bool OverridesOuter,
    bool ReplacesAuthHeader)
{
    public string Key => Item.Key.Trim();
    public string Value => Item.Value;
}
