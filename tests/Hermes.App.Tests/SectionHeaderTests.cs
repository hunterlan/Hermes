using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class SectionHeaderTests
{
    [AvaloniaFact]
    public void Clicking_the_arrow_of_a_section_header_toggles_the_section()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        var headersHeader = window.GetVisualDescendants().OfType<ToggleButton>()
            .First(button => button.Classes.Contains("sectionHeader")
                             && button.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Headers"));
        var arrowPoint = headersHeader.TranslatePoint(new Point(2, headersHeader.Bounds.Height / 2), window)!.Value;
        Assert.True(tab.HeadersExpanded);

        // Expanded (checked) header: collapse it.
        window.MouseDown(arrowPoint, MouseButton.Left);
        UiTestHelpers.Pump();
        window.MouseUp(arrowPoint, MouseButton.Left);
        UiTestHelpers.Pump();
        Assert.False(tab.HeadersExpanded);

        // Collapsed (unchecked) header: pressing it must not shrink it, so the arrow stays under the pointer.
        window.MouseDown(arrowPoint, MouseButton.Left);
        UiTestHelpers.Pump();
        Assert.True(headersHeader.RenderTransform is null || headersHeader.RenderTransform.Value.IsIdentity);
        window.MouseUp(arrowPoint, MouseButton.Left);
        UiTestHelpers.Pump();
        Assert.True(tab.HeadersExpanded);
        window.Close();
    }
}
