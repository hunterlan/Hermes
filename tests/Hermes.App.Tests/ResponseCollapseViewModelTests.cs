using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>Collapsing the response pane to its bar, without a view: the toggle, auto-expand and the kept split.</summary>
public class ResponseCollapseViewModelTests
{
    private static ResponseModel Ok() =>
        new() { StatusCode = 201, ReasonPhrase = "Created", ContentType = "text/plain", Body = "ok"u8.ToArray() };

    [AvaloniaFact]
    public void The_toggle_collapses_and_expands_the_pane()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);

        response.Pane.ToggleCollapsedCommand.Execute(null);
        Assert.True(response.Pane.IsResponseCollapsed);
        Assert.True(response.Pane.ShowsCollapsedBar);

        response.Pane.ToggleCollapsedCommand.Execute(null);
        Assert.False(response.Pane.IsResponseCollapsed);
        Assert.False(response.Pane.ShowsCollapsedBar);
    }

    [AvaloniaFact]
    public void A_response_expands_the_collapsed_pane()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.Pane.IsResponseCollapsed = true;

        response.SetResponse(Ok());

        Assert.False(response.Pane.IsResponseCollapsed);
    }

    [AvaloniaFact]
    public void A_failure_expands_the_collapsed_pane()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.Pane.IsResponseCollapsed = true;

        response.SetFailure(RequestFailure.FromMessage("Connection refused"));

        Assert.False(response.Pane.IsResponseCollapsed);
    }

    [AvaloniaFact]
    public void Collapsing_and_expanding_keeps_the_split()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.Pane.ResponseSplitRatio = 0.3;

        response.Pane.ToggleCollapsedCommand.Execute(null);
        response.Pane.ToggleCollapsedCommand.Execute(null);

        Assert.Equal(0.3, response.Pane.ResponseSplitRatio);
    }

    [AvaloniaTheory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-0.2)]
    [InlineData(double.NaN)]
    public void A_restored_split_that_cannot_be_one_falls_back_to_half(double savedRatio)
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);

        response.Pane.RestorePane(isCollapsed: true, savedRatio, savedRatio);

        Assert.True(response.Pane.IsResponseCollapsed);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, response.Pane.ResponseSplitRatio);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, response.Pane.SideBySideResponseSplitRatio);
    }

    [AvaloniaFact]
    public async Task A_response_expands_the_pane_collapsed_to_a_strip_and_the_width_share_stays()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        try
        {
            response.Pane.SetLayoutCommand.Execute(ResponseLayout.Horizontal);
            response.Pane.SideBySideResponseSplitRatio = 0.3;
            response.Pane.ToggleCollapsedCommand.Execute(null);
            Assert.True(response.Pane.ShowsCollapsedStrip);

            response.SetResponse(Ok());

            Assert.False(response.Pane.IsResponseCollapsed);
            Assert.False(response.Pane.ShowsCollapsedStrip);
            Assert.Equal(0.3, response.Pane.SideBySideResponseSplitRatio);
            Assert.Equal(SessionTab.DefaultResponseSplitRatio, response.Pane.ResponseSplitRatio);
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            await temp.Settings.SaveCurrentAsync();
        }
    }

    [AvaloniaFact]
    public async Task The_collapse_is_a_bar_when_stacked_and_a_strip_when_side_by_side()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.Pane.IsResponseCollapsed = true;
        try
        {
            response.Pane.SetLayoutCommand.Execute(ResponseLayout.Horizontal);

            Assert.False(response.Pane.ShowsCollapsedBar);
            Assert.True(response.Pane.ShowsCollapsedStrip);
            Assert.True(response.Pane.ToggleCollapsedCommand.CanExecute(null));
            Assert.True(response.Pane.IsResponseCollapsed);

            response.Pane.SetLayoutCommand.Execute(ResponseLayout.Vertical);

            Assert.True(response.Pane.ShowsCollapsedBar);
            Assert.False(response.Pane.ShowsCollapsedStrip);
            Assert.True(response.Pane.ToggleCollapsedCommand.CanExecute(null));
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            await temp.Settings.SaveCurrentAsync();
        }
    }

    [AvaloniaFact]
    public async Task A_layout_change_from_settings_reaches_every_tab()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        var changed = new List<string?>();
        response.Pane.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        try
        {
            temp.Settings.Current.Layout = ResponseLayout.Horizontal;
            await temp.Settings.SaveCurrentAsync();

            Assert.Contains(nameof(ResponsePaneLayoutViewModel.IsSideBySide), changed);
            Assert.True(response.Pane.IsSideBySide);
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            await temp.Settings.SaveCurrentAsync();
        }
    }
}
