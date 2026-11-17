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

        response.ToggleCollapsedCommand.Execute(null);
        Assert.True(response.IsResponseCollapsed);
        Assert.True(response.ShowsCollapsedBar);

        response.ToggleCollapsedCommand.Execute(null);
        Assert.False(response.IsResponseCollapsed);
        Assert.False(response.ShowsCollapsedBar);
    }

    [AvaloniaFact]
    public void A_response_expands_the_collapsed_pane()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.IsResponseCollapsed = true;

        response.SetResponse(Ok());

        Assert.False(response.IsResponseCollapsed);
    }

    [AvaloniaFact]
    public void A_failure_expands_the_collapsed_pane()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.IsResponseCollapsed = true;

        response.SetFailure(RequestFailure.FromMessage("Connection refused"));

        Assert.False(response.IsResponseCollapsed);
    }

    [AvaloniaFact]
    public void Collapsing_and_expanding_keeps_the_split()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.ResponseSplitRatio = 0.3;

        response.ToggleCollapsedCommand.Execute(null);
        response.ToggleCollapsedCommand.Execute(null);

        Assert.Equal(0.3, response.ResponseSplitRatio);
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

        response.RestorePane(isCollapsed: true, savedRatio);

        Assert.True(response.IsResponseCollapsed);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, response.ResponseSplitRatio);
    }

    [AvaloniaFact]
    public void Side_by_side_shows_the_pane_and_keeps_the_collapse_for_stacked()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.IsResponseCollapsed = true;
        try
        {
            response.SetLayoutCommand.Execute(ResponseLayout.Horizontal);

            Assert.False(response.ShowsCollapsedBar);
            Assert.False(response.ToggleCollapsedCommand.CanExecute(null));
            Assert.True(response.IsResponseCollapsed);

            response.SetLayoutCommand.Execute(ResponseLayout.Vertical);

            Assert.True(response.ShowsCollapsedBar);
            Assert.True(response.ToggleCollapsedCommand.CanExecute(null));
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            temp.Settings.SaveCurrent();
        }
    }

    [AvaloniaFact]
    public void A_layout_change_from_settings_reaches_every_tab()
    {
        using var temp = new TempWorkspace();
        using var response = new ResponseViewModel(new DialogService(), temp.Settings);
        var changed = new List<string?>();
        response.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        try
        {
            temp.Settings.Current.Layout = ResponseLayout.Horizontal;
            temp.Settings.SaveCurrent();

            Assert.Contains(nameof(ResponseViewModel.IsSideBySide), changed);
            Assert.True(response.IsSideBySide);
        }
        finally
        {
            temp.Settings.Current.Layout = ResponseLayout.Vertical;
            temp.Settings.SaveCurrent();
        }
    }
}
