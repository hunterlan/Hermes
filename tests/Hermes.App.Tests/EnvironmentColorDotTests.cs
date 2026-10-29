using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Hermes.App.Controls;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>The environment colour dot resolves each swatch to the shade of the current theme.</summary>
public class EnvironmentColorDotTests
{
    [AvaloniaTheory]
    [InlineData(EnvironmentColor.None, "#8590A1", "#586274")]
    [InlineData(EnvironmentColor.Red, "#FF8179", "#C62F26")]
    [InlineData(EnvironmentColor.Orange, "#FF9F5A", "#B8540F")]
    [InlineData(EnvironmentColor.Amber, "#F0B84A", "#8F5600")]
    [InlineData(EnvironmentColor.Green, "#62D6A0", "#0D6E45")]
    [InlineData(EnvironmentColor.Teal, "#4FD1D9", "#0B6E78")]
    [InlineData(EnvironmentColor.Blue, "#80B9FF", "#1B5BB8")]
    [InlineData(EnvironmentColor.Purple, "#B69CFF", "#6C45C9")]
    [InlineData(EnvironmentColor.Pink, "#F58CC8", "#B02C7A")]
    public void Each_swatch_follows_the_theme(EnvironmentColor color, string darkShade, string lightShade)
    {
        var dot = new EnvironmentColorDot { Color = color };
        var window = new Window { Content = dot, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        UiTestHelpers.Pump();

        Assert.Equal(Color.Parse(darkShade), ResolvedColor(dot));

        window.RequestedThemeVariant = ThemeVariant.Light;
        UiTestHelpers.Pump();
        Assert.Equal(Color.Parse(lightShade), ResolvedColor(dot));

        window.Close();
    }

    [AvaloniaFact]
    public void Changing_the_colour_resolves_the_new_swatch()
    {
        var dot = new EnvironmentColorDot();
        var window = new Window { Content = dot, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        UiTestHelpers.Pump();

        dot.Color = EnvironmentColor.Teal;
        UiTestHelpers.Pump();

        Assert.Equal(Color.Parse("#4FD1D9"), ResolvedColor(dot));
        window.Close();
    }

    private static Color ResolvedColor(EnvironmentColorDot dot) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(dot.ResolvedBrush).Color;
}
