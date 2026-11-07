using Avalonia;
using Avalonia.Controls;

namespace Hermes.App.Controls;

/// <summary>
/// The title row of a dialog: a heading, an optional subtitle under it and an optional action
/// (the <see cref="ContentControl.Content"/>) at the right.
/// </summary>
public sealed class DialogHeader : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Subtitle));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
