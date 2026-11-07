using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hermes.App.Controls;

namespace Hermes.App.Tests;

public class IconTagTests
{
    [AvaloniaFact]
    public void Shows_its_text_and_icon_and_follows_the_kind()
    {
        var tag = new IconTag { Kind = IconTagKind.Ok, Text = "200 OK", Icon = Geometry.Parse("M0,0L10,10") };
        var window = new Window { Content = tag };
        window.Show();
        UiTestHelpers.Pump();

        Assert.Contains(tag.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "200 OK");
        Assert.True(tag.GetVisualDescendants().OfType<PathIcon>().Single().IsVisible);
        Assert.Contains(":ok", tag.Classes);
        var okBackground = tag.Background;

        tag.Kind = IconTagKind.Err;
        UiTestHelpers.Pump();

        Assert.Contains(":err", tag.Classes);
        Assert.DoesNotContain(":ok", tag.Classes);
        Assert.NotEqual(okBackground, tag.Background);
        window.Close();
    }

    [AvaloniaFact]
    public void Hides_the_icon_when_there_is_none()
    {
        var tag = new IconTag { Text = "Plain" };
        var window = new Window { Content = tag };
        window.Show();
        UiTestHelpers.Pump();

        Assert.False(tag.GetVisualDescendants().OfType<PathIcon>().Single().IsVisible);
        window.Close();
    }
}
