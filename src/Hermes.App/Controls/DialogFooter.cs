using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Hermes.App.Controls;

/// <summary>
/// The button row at the bottom of a dialog. The <see cref="ContentControl.Content"/> (usually Cancel and an accent
/// button) is right-aligned, <see cref="Leading"/> sits at the left, and <see cref="CloseButtonText"/> adds a
/// button that simply closes the dialog.
/// </summary>
public sealed class DialogFooter : ContentControl
{
    public static readonly StyledProperty<object?> LeadingProperty =
        AvaloniaProperty.Register<DialogFooter, object?>(nameof(Leading));

    public static readonly StyledProperty<string?> CloseButtonTextProperty =
        AvaloniaProperty.Register<DialogFooter, string?>(nameof(CloseButtonText));

    private Button? _closeButton;

    /// <summary>Content at the left edge, such as a link-style action.</summary>
    public object? Leading
    {
        get => GetValue(LeadingProperty);
        set => SetValue(LeadingProperty, value);
    }

    /// <summary>When set, shows a button with this text that closes the window and answers the Escape key.</summary>
    public string? CloseButtonText
    {
        get => GetValue(CloseButtonTextProperty);
        set => SetValue(CloseButtonTextProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_closeButton is not null)
            _closeButton.Click -= OnCloseClick;
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        if (_closeButton is not null)
            _closeButton.Click += OnCloseClick;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
