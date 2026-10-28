namespace Hermes.App.ViewModels;

/// <summary>One level of the auth chain (collection → folders → request), innermost last.</summary>
public sealed record AuthChainNode(string Kind, string Name, string Status, string? TypeLabel, bool IsSource, bool IsSelf)
{
    public bool HasType => TypeLabel is not null;
    public string Title => $"{Kind} · {Name}";
}
