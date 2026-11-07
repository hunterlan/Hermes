using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class SidebarSharedStyleTests
{
    [AvaloniaFact]
    public void Every_sidebar_pane_has_a_search_box_with_the_search_icon()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();

        foreach (var pane in new[] { SidebarPane.Collections, SidebarPane.Environments, SidebarPane.History })
        {
            vm.SelectedPane = pane;
            UiTestHelpers.Pump();

            var searchBox = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => box.IsEffectivelyVisible && box.Classes.Contains("search"));
            Assert.IsType<PathIcon>(searchBox.InnerLeftContent);
        }

        window.Close();
    }

    [AvaloniaFact]
    public void Row_menu_buttons_are_hidden_until_the_row_is_hovered()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.SelectedPane = SidebarPane.Environments;
        UiTestHelpers.Pump();

        var rowMenu = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("rowMenu") && button.IsEffectivelyVisible);
        Assert.NotNull(rowMenu);
        Assert.Equal(0, rowMenu.Opacity);

        var item = rowMenu.FindAncestorOfType<ListBoxItem>()!;
        var hoverPoint = item.TranslatePoint(new Point(20, 10), window)!.Value;
        window.MouseMove(hoverPoint);
        UiTestHelpers.Pump();
        Assert.Equal(1, rowMenu.Opacity);
        window.Close();
    }
}
