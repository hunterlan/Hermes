namespace Hermes.App.ViewModels;

/// <summary>A palette row: a saved request to open or a command to run.</summary>
public sealed record PaletteItem(string Title, string? Detail, string? Method, string? Shortcut, Action Run)
{
    public bool IsRequest => Method is not null;
    public bool HasShortcut => Shortcut is not null;
}
