using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models.Auth;

namespace Hermes.App.Tests;

public class InheritanceChainTests
{
    private static AuthConfig Inherit() => new() { Type = AuthType.Inherit };

    private static AuthConfig Bearer() => new() { Type = AuthType.Bearer };

    [Fact]
    public void Auth_chain_marks_the_nearest_defining_level_as_the_source()
    {
        var chain = AuthInheritance.Chain(
            [("Collection", "Shop", Bearer()), ("Folder", "Orders", Inherit())],
            ("Request", "List", Inherit()));

        Assert.Equal(["Collection · Shop", "Folder · Orders", "Request · List"], chain.Select(node => node.Title));
        Assert.Equal(["Source", "Inherits", "Inherits"], chain.Select(node => node.Status));
        Assert.Equal([true, false, false], chain.Select(node => node.IsSource));
        Assert.Equal([false, false, true], chain.Select(node => node.IsLast));
    }

    [Fact]
    public void Auth_chain_shows_the_auth_type_only_on_levels_that_define_it()
    {
        var chain = AuthInheritance.Chain(
            [("Collection", "Shop", Bearer())],
            ("Request", "List", new AuthConfig { Type = AuthType.Basic }));

        Assert.Equal(["Overridden", "Defined here"], chain.Select(node => node.Status));
        Assert.All(chain, node => Assert.Single(node.Details));
        Assert.Equal([AuthInheritance.Label(AuthType.Bearer), AuthInheritance.Label(AuthType.Basic)], chain.Select(node => node.Details[0].Text));

        var inheriting = AuthInheritance.Chain([("Collection", "Shop", Bearer())], ("Request", "List", Inherit()));
        Assert.False(inheriting[1].HasDetails);
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Chain_view_joins_details_with_separators_and_strikes_out_overridden_ones(string themeName)
    {
        InheritanceChainNode[] nodes =
        [
            new("Collection · Shop", "Source", true, false, [new("X-Api-Version"), new("Accept */*", IsStruckThrough: true)]),
            new("Request · List", "Inherits", false, true, []),
        ];
        var view = new InheritanceChainView { Nodes = nodes };
        var window = new Window { Content = view, Width = 320, Height = 240, RequestedThemeVariant = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        UiTestHelpers.Pump();

        var detailTexts = view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("chainDetail")).ToList();
        var visibleSeparators = detailTexts.Where(text => text.Classes.Contains("chainDetailSeparator") && text.IsVisible).ToList();
        var names = detailTexts.Where(text => !text.Classes.Contains("chainDetailSeparator")).ToList();
        Assert.Single(visibleSeparators);
        Assert.Equal(["X-Api-Version", "Accept */*"], names.Select(text => text.Text));
        Assert.Null(names[0].TextDecorations);
        Assert.Same(TextDecorations.Strikethrough, names[1].TextDecorations);

        var nodeBorders = view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("chainNode")).ToList();
        Assert.Equal([true, false], nodeBorders.Select(border => border.Classes.Contains("source")));
        Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("chainLink") && border.IsVisible);

        window.Close();
    }
}
