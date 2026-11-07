using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;

namespace Hermes.App.Tests;

public class SettingRowTests
{
    private static (Window Window, SettingRow Row, Control Control) Show(bool isStacked)
    {
        var control = new TextBox();
        var row = new SettingRow { Title = "Timeout", Hint = "How long to wait.", IsStacked = isStacked, Content = control };
        var window = new Window { Width = 500, Height = 200, Content = row };
        window.Show();
        UiTestHelpers.Pump();
        return (window, row, control);
    }

    [AvaloniaFact]
    public void Shows_title_and_hint_with_the_control_beside_them()
    {
        var (window, row, control) = Show(isStacked: false);

        var texts = row.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("Timeout", texts);
        Assert.Contains("How long to wait.", texts);
        var presenter = control.FindAncestorOfType<ContentPresenter>()!;
        Assert.Equal(2, Grid.GetColumn(presenter));
        window.Close();
    }

    [AvaloniaFact]
    public void Stacked_layout_puts_the_control_under_the_text()
    {
        var (window, _, control) = Show(isStacked: true);

        var presenter = control.FindAncestorOfType<ContentPresenter>()!;
        Assert.Equal(1, Grid.GetRow(presenter));
        Assert.Equal(0, Grid.GetColumn(presenter));
        window.Close();
    }

    [AvaloniaFact]
    public void Hides_the_hint_when_there_is_none()
    {
        var (window, row, _) = Show(isStacked: false);
        row.Hint = null;
        UiTestHelpers.Pump();

        var hint = row.GetVisualDescendants().OfType<TextBlock>().First(text => text.FontSize == 12);
        Assert.False(hint.IsVisible);
        window.Close();
    }
}
