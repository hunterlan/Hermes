using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;

namespace Hermes.App.Tests;

public class DialogChromeTests
{
    [AvaloniaFact]
    public void Header_shows_title_and_subtitle_and_hides_an_empty_subtitle()
    {
        var header = new DialogHeader { Title = "Cookies", Subtitle = "Stored per domain." };
        var window = new Window { Content = header };
        window.Show();
        UiTestHelpers.Pump();

        var texts = header.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(texts, text => text.Text == "Cookies");
        var subtitle = texts.Single(text => text.Text == "Stored per domain.");

        header.Subtitle = null;
        UiTestHelpers.Pump();

        Assert.False(subtitle.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Footer_close_button_closes_the_window()
    {
        var footer = new DialogFooter { CloseButtonText = "Close" };
        var window = new Window { Content = footer };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        UiTestHelpers.Pump();

        var close = footer.GetVisualDescendants().OfType<Button>().Single(button => button.IsVisible);
        Assert.Equal("Close", close.Content);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        UiTestHelpers.Pump();

        Assert.True(closed);
    }
}
