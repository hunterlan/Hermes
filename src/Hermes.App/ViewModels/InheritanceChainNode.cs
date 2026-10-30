namespace Hermes.App.ViewModels;

/// <summary>
/// One level of an inheritance chain (collection → folders → request), innermost last. The detail line is a short list
/// shown after the title, such as the auth type or the headers a folder adds.
/// </summary>
public sealed record InheritanceChainNode(string Title, string Status, bool IsSource, bool IsLast, IReadOnlyList<InheritanceChainDetail> Details)
{
    public bool HasDetails => Details.Count > 0;
}
