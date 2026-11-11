using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.ViewModels;
using Hermes.App.Views;

namespace Hermes.App.Tests;

/// <summary>
/// IconTag replaced hand-written Border.tag markup. Pins that the text and icon end up exactly as the old markup did
/// in the places where an outer style (the status bar's) used to reach the text.
/// </summary>
public class TagLookTests
{
    private static string Describe(Visual root, bool includeIconSize = true)
    {
        var text = root.GetVisualDescendants().OfType<TextBlock>().First();
        var icon = root.GetVisualDescendants().OfType<PathIcon>().First();
        return $"textSize={text.FontSize} weight={text.FontWeight} text={(text.Foreground as ISolidColorBrush)?.Color} " +
               $"icon={(icon.Foreground as ISolidColorBrush)?.Color}" + (includeIconSize ? $" iconSize={icon.Width}" : "");
    }

    private static Border LegacyTag(string kind, string? status, double iconSize, double spacing) => new()
    {
        Classes = { status ?? "tag", "tag", kind },
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing,
            Children = { new PathIcon { Width = iconSize, Height = iconSize }, new TextBlock { Text = "x" } },
        },
    };

    private static IconTag NewTag(IconTagKind kind, bool status) => new()
    {
        Kind = kind,
        Text = "x",
        Icon = Geometry.Parse("M0,0L1,1"),
        Classes = { status ? "status" : "plain" },
    };

    private static void AssertSameLook(Panel host, Border legacy, IconTag replacement, bool includeIconSize)
    {
        host.Children.Add(legacy);
        host.Children.Add(replacement);
        UiTestHelpers.Pump();
        Assert.Equal(Describe(legacy, includeIconSize), Describe(replacement, includeIconSize));
    }

    [AvaloniaFact]
    public void Status_bar_tag_looks_like_the_old_markup()
    {
        var (window, _) = UiTestHelpers.ShowMainWindow();
        var statusBar = window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("statusBar"));

        AssertSameLook((Grid)statusBar.Child!, LegacyTag("warn", null, 11, 5), NewTag(IconTagKind.Warn, false), true);
        window.Close();
    }

    [AvaloniaFact]
    public void Environment_pane_tag_looks_like_the_old_markup()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.SelectedPane = SidebarPane.Environments;
        UiTestHelpers.Pump();
        // The host only has to sit inside the window so that theme and style resources reach the tags; the status bar
        // grid is used because it is always there.
        var statusBar = window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("statusBar"));
        var host = (Grid)statusBar.Child!;

        // The old Confirm icon was 10px; the shared control uses 11px on purpose.
        AssertSameLook(host, LegacyTag("err", null, 11, 5), NewTag(IconTagKind.Err, false), true);
        window.Close();
    }

    [AvaloniaFact]
    public void Response_status_tag_looks_like_the_old_markup()
    {
        // The response pane is hidden until there is a response, so compare in a plain host: nothing local reaches it.
        var host = new StackPanel();
        var window = new Window { Content = host };
        window.Show();

        AssertSameLook(host, LegacyTag("ok", "status", 13, 6), NewTag(IconTagKind.Ok, true), true);
        window.Close();
    }
}
