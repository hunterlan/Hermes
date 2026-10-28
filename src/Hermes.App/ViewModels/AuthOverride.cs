namespace Hermes.App.ViewModels;

/// <summary>A request or folder inside a container that sets its own auth instead of inheriting.</summary>
public sealed record AuthOverride(string? Method, string Name, string TypeLabel)
{
    public bool IsFolder => Method is null;
}
