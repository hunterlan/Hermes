using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>The pane's layout on its own: it follows the settings and lets go of them when disposed.</summary>
public class ResponsePaneLayoutViewModelTests
{
    [AvaloniaFact]
    public async Task Side_by_side_can_be_toggled_and_shows_the_strip()
    {
        using var temp = new TempWorkspace();
        using var pane = new ResponsePaneLayoutViewModel(temp.Settings);
        try
        {
            temp.Settings.Current.Layout = ResponseLayout.Horizontal;
            await temp.Settings.SaveCurrentAsync();

            Assert.True(pane.IsSideBySide);
            Assert.True(pane.ToggleCollapsedCommand.CanExecute(null));

            pane.ToggleCollapsedCommand.Execute(null);

            Assert.True(pane.ShowsCollapsedStrip);
            Assert.False(pane.ShowsCollapsedBar);
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            await temp.Settings.SaveCurrentAsync();
        }
    }

    [AvaloniaFact]
    public void A_collapse_shows_the_bar_only_when_stacked()
    {
        using var temp = new TempWorkspace();
        using var pane = new ResponsePaneLayoutViewModel(temp.Settings);

        pane.ToggleCollapsedCommand.Execute(null);

        Assert.True(pane.ShowsCollapsedBar);
    }

    [AvaloniaFact]
    public void Expand_opens_a_collapsed_pane()
    {
        using var temp = new TempWorkspace();
        using var pane = new ResponsePaneLayoutViewModel(temp.Settings);
        pane.IsResponseCollapsed = true;

        pane.Expand();

        Assert.False(pane.IsResponseCollapsed);
        Assert.False(pane.ShowsCollapsedBar);
    }

    [AvaloniaFact]
    public void A_restored_split_inside_the_range_is_kept()
    {
        using var temp = new TempWorkspace();
        using var pane = new ResponsePaneLayoutViewModel(temp.Settings);

        pane.RestorePane(isCollapsed: false, splitRatio: 0.3, sideBySideSplitRatio: 0.7);

        Assert.Equal(0.3, pane.ResponseSplitRatio);
        Assert.Equal(0.7, pane.SideBySideResponseSplitRatio);
    }

    [AvaloniaFact]
    public async Task A_disposed_pane_no_longer_follows_the_settings()
    {
        using var temp = new TempWorkspace();
        var pane = new ResponsePaneLayoutViewModel(temp.Settings);
        var changed = new List<string?>();
        pane.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        pane.Dispose();
        try
        {
            temp.Settings.Current.Layout = ResponseLayout.Horizontal;
            await temp.Settings.SaveCurrentAsync();

            Assert.Empty(changed);
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            await temp.Settings.SaveCurrentAsync();
        }
    }
}
