using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

/// <summary>The response pane collapsed to a vertical strip. The whole strip expands it, not only its chevron.</summary>
public partial class CollapsedResponseStrip : UserControl
{
    private bool _isPressedOnStrip;

    public CollapsedResponseStrip() => InitializeComponent();

    // The chevron handles its own press and release, so these see only the rest of the strip.
    private void OnStripPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _isPressedOnStrip = e.GetCurrentPoint(Strip).Properties.IsLeftButtonPressed;

    private void OnStripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasPressedOnStrip = _isPressedOnStrip;
        _isPressedOnStrip = false;
        // A press on the strip that ends somewhere else is not a click.
        var releasedOnStrip = new Rect(Strip.Bounds.Size).Contains(e.GetPosition(Strip));
        if (!wasPressedOnStrip || !releasedOnStrip || e.InitialPressMouseButton != MouseButton.Left
            || DataContext is not ResponseViewModel { Pane.ToggleCollapsedCommand: var toggle } || !toggle.CanExecute(null))
            return;
        toggle.Execute(null);
        e.Handled = true;
    }
}
