using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;

namespace Hermes.App.Tests;

public class CollapsibleSectionTests
{
    private static (Window Window, CollapsibleSection Section, Button Action, Border Body) Show(bool isExpanded)
    {
        var action = new Button { Content = "Format" };
        var body = new Border { Height = 20 };
        var section = new CollapsibleSection
        {
            Header = "Body",
            Summary = "json",
            IsExpanded = isExpanded,
            HeaderActions = action,
            Content = body,
        };
        var window = new Window { Width = 400, Height = 300, Content = section };
        window.Show();
        UiTestHelpers.Pump();
        return (window, section, action, body);
    }

    [AvaloniaFact]
    public void Content_and_header_actions_follow_IsExpanded()
    {
        var (window, section, action, body) = Show(isExpanded: false);
        Assert.False(body.IsAttachedToVisualTree() && body.IsEffectivelyVisible);
        Assert.False(action.IsAttachedToVisualTree() && action.IsEffectivelyVisible);

        section.IsExpanded = true;
        UiTestHelpers.Pump();

        Assert.True(body.IsEffectivelyVisible);
        Assert.True(action.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Toggling_the_header_updates_IsExpanded()
    {
        var (window, section, _, _) = Show(isExpanded: true);
        var header = section.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Classes.Contains("sectionHeader"));

        header.IsChecked = false;
        UiTestHelpers.Pump();

        Assert.False(section.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void Summary_is_hidden_when_empty()
    {
        var (window, section, _, _) = Show(isExpanded: true);
        var summary = section.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("summary"));
        Assert.True(summary.IsVisible);

        section.Summary = "";
        UiTestHelpers.Pump();

        Assert.False(summary.IsVisible);
        window.Close();
    }
}
