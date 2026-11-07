using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;

namespace Hermes.App.Controls;

/// <summary>
/// A section with a clickable header (chevron, title, quiet summary) that shows or hides its content.
/// <see cref="HeaderedContentControl.Header"/> is the title. <see cref="HeaderActions"/> sit at the right of the
/// header and are only shown while the section is expanded.
/// </summary>
public sealed class CollapsibleSection : HeaderedContentControl
{
    public static readonly StyledProperty<string?> SummaryProperty =
        AvaloniaProperty.Register<CollapsibleSection, string?>(nameof(Summary));

    public static readonly StyledProperty<IBrush?> SummaryForegroundProperty =
        AvaloniaProperty.Register<CollapsibleSection, IBrush?>(nameof(SummaryForeground));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<CollapsibleSection, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<object?> HeaderActionsProperty =
        AvaloniaProperty.Register<CollapsibleSection, object?>(nameof(HeaderActions));

    /// <summary>Quiet text after the title, e.g. how many items the section holds.</summary>
    public string? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public IBrush? SummaryForeground
    {
        get => GetValue(SummaryForegroundProperty);
        set => SetValue(SummaryForegroundProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Controls at the right edge of the header, outside the toggle so clicking them does not collapse the section.</summary>
    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }
}
