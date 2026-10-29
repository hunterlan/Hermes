using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class StatusBarTests
{
    /// <summary>The sans-serif items use the bundled Inter font, so their centres are the same on every OS.</summary>
    private const double MaximumCentreOffset = 0.5;

    /// <summary>
    /// The data folder path uses the per-OS monospace fallback chain (Consolas, Menlo, DejaVu Sans Mono...). Its line
    /// height differs between systems, so an odd height can round to a pixel away from the other items.
    /// </summary>
    private const double MaximumMonoCentreOffset = 1;

    private const double MaximumIconCentreOffset = 0.25;

    [AvaloniaFact]
    public void Status_bar_items_share_one_vertical_centre()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var statusBar = window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("statusBar"));
        var cookiesButton = window.GetVisualDescendants().OfType<Button>().First(button => button.Command == vm.OpenCookiesCommand);
        var themeButton = window.GetVisualDescendants().OfType<Button>().First(button => button.Command == vm.ToggleThemeCommand);
        var dataDirectoryText = statusBar.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("mono"));
        var versionText = statusBar.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == vm.AppVersion);

        var themeTextCentre = CentreYWithin(FindTextVisual(themeButton), statusBar);
        var sansTextCentres = new Dictionary<string, double>
        {
            ["Cookies"] = CentreYWithin(FindTextVisual(cookiesButton), statusBar),
            ["Theme"] = themeTextCentre,
            ["Version"] = CentreYWithin(versionText, statusBar),
        };

        foreach (var (itemName, centre) in sansTextCentres)
        {
            Assert.True(Math.Abs(centre - themeTextCentre) <= MaximumCentreOffset,
                $"{itemName} text centre {centre} is more than {MaximumCentreOffset}px away from the Theme text centre {themeTextCentre}.");
        }

        var dataDirectoryCentre = CentreYWithin(dataDirectoryText, statusBar);
        Assert.True(Math.Abs(dataDirectoryCentre - themeTextCentre) <= MaximumMonoCentreOffset,
            $"Data folder text centre {dataDirectoryCentre} is more than {MaximumMonoCentreOffset}px away from the Theme text centre {themeTextCentre}.");

        window.Close();
    }

    [AvaloniaFact]
    public void Theme_icon_is_optically_centred_with_theme_text()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var statusBar = window.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("statusBar"));
        var themeButton = window.GetVisualDescendants().OfType<Button>().First(button => button.Command == vm.ToggleThemeCommand);

        var themeTextCentre = CentreYWithin(FindTextVisual(themeButton), statusBar);
        var iconCentre = CentreYWithin(themeButton.GetVisualDescendants().OfType<PathIcon>().Single(), statusBar);

        Assert.True(Math.Abs(iconCentre - themeTextCentre) <= MaximumIconCentreOffset,
            $"Theme icon centre {iconCentre} is more than {MaximumIconCentreOffset}px away from the Theme text centre {themeTextCentre}.");
        window.Close();
    }

    private static TextBlock FindTextVisual(Visual container) => container.GetVisualDescendants().OfType<TextBlock>().First();

    /// <summary>
    /// Vertical middle of a control's text, relative to <paramref name="container"/>. Uses the desired height
    /// rather than Bounds: a stretched AccessText keeps its text at the top of taller bounds.
    /// </summary>
    private static double CentreYWithin(Control control, Visual container)
    {
        var top = control.TranslatePoint(new Point(0, 0), container)!.Value.Y;
        return top + control.DesiredSize.Height / 2;
    }
}
