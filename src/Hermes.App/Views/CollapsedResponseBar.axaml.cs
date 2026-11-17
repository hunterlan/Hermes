using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Hermes.App.ViewModels;

namespace Hermes.App.Views;

/// <summary>The response pane collapsed to a bar. The whole bar expands it, not only its chevron.</summary>
public partial class CollapsedResponseBar : UserControl
{
    private bool _isPressedOnBar;

    public CollapsedResponseBar() => InitializeComponent();

    // The chevron handles its own press and release, so these see only the rest of the bar.
    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _isPressedOnBar = e.GetCurrentPoint(Bar).Properties.IsLeftButtonPressed;

    private void OnBarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasPressedOnBar = _isPressedOnBar;
        _isPressedOnBar = false;
        // A press on the bar that ends somewhere else is not a click.
        var releasedOnBar = new Rect(Bar.Bounds.Size).Contains(e.GetPosition(Bar));
        if (!wasPressedOnBar || !releasedOnBar || e.InitialPressMouseButton != MouseButton.Left
            || DataContext is not ResponseViewModel { ToggleCollapsedCommand: var toggle } || !toggle.CanExecute(null))
            return;
        toggle.Execute(null);
        e.Handled = true;
    }
}
