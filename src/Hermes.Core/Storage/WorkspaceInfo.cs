namespace Hermes.Core.Storage;

/// <summary>Contents of workspace.json.</summary>
public sealed class WorkspaceInfo
{
    public Guid Id { get; set; }

    /// <summary>Where the folder was when it was last opened; tells a moved folder from a copied one.</summary>
    public string? Path { get; set; }
}
