using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The response pane collapsed to its bar in stacked layout (issue #45), in a shown window.</summary>
public class ResponseCollapseViewTests
{
    private const double BarHeight = 40;

    private static SettingsService Settings => App.Services.GetRequiredService<SettingsService>();

    private sealed record Shown(Window Window, MainWindowViewModel ViewModel, RequestTabViewModel Tab, RequestTabView View);

    private static Shown Show()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        var view = window.GetVisualDescendants().OfType<RequestTabView>().Single(v => v.IsEffectivelyVisible);
        return new Shown(window, vm, tab, view);
    }

    private static ResponseModel Created() => new()
    {
        StatusCode = 201,
        ReasonPhrase = "Created",
        ContentType = "text/plain",
        Body = "ok"u8.ToArray(),
        TotalTime = TimeSpan.FromMilliseconds(412),
    };

    private static Grid SplitGrid(RequestTabView view) => view.FindControl<Grid>("SplitGrid")!;

    private static CollapsedResponseBar Bar(RequestTabView view) => view.FindControl<CollapsedResponseBar>("ResponseBar")!;

    /// <summary>The texts a person can see on the bar.</summary>
    private static List<string> VisibleBarTexts(RequestTabView view) => Bar(view).GetVisualDescendants()
        .OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)
        .Select(text => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)) : text.Text ?? "")
        .ToList();

    /// <summary>The response pane's share of the split, from the grid's row lengths.</summary>
    private static double ResponseShare(RequestTabView view)
    {
        var rows = SplitGrid(view).RowDefinitions;
        return rows[2].Height.Value / (rows[0].Height.Value + rows[2].Height.Value);
    }

    private static void Close(Shown shown)
    {
        shown.Window.Close();
        UiTestHelpers.Pump();
    }

    [AvaloniaFact]
    public void Collapsed_the_response_row_is_the_bar_showing_the_status()
    {
        var shown = Show();
        shown.Tab.Response.SetResponse(Created());

        shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
        UiTestHelpers.Pump();

        var responseRow = SplitGrid(shown.View).RowDefinitions[2];
        Assert.Equal(new GridLength(BarHeight, GridUnitType.Pixel), responseRow.Height);
        Assert.Equal(BarHeight, responseRow.ActualHeight);
        Assert.True(Bar(shown.View).IsEffectivelyVisible);
        Assert.False(shown.View.FindControl<ResponseView>("ResponsePane")!.IsEffectivelyVisible);
        Assert.False(shown.View.FindControl<GridSplitter>("Splitter")!.IsEffectivelyVisible);
        var tag = Bar(shown.View).GetVisualDescendants().OfType<IconTag>().Single(t => t.IsEffectivelyVisible);
        Assert.Equal("201 Created", tag.Text);
        Assert.Equal(IconTagKind.Ok, tag.Kind);
        Assert.Contains("412 ms · 2 B", VisibleBarTexts(shown.View));
        Assert.Contains("Ctrl J", VisibleBarTexts(shown.View));
        UiTestHelpers.Screenshot(shown.Window, "45-collapsed-response");
        Close(shown);
    }

    [AvaloniaFact]
    public void Before_the_first_send_the_bar_says_not_sent_yet_without_a_tag()
    {
        var shown = Show();

        shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
        UiTestHelpers.Pump();

        Assert.Contains("· not sent yet", VisibleBarTexts(shown.View));
        Assert.DoesNotContain(Bar(shown.View).GetVisualDescendants().OfType<IconTag>(), tag => tag.IsEffectivelyVisible);
        Assert.DoesNotContain("No response", VisibleBarTexts(shown.View));
        Close(shown);
    }

    [AvaloniaFact]
    public void After_a_failure_the_bar_shows_no_response()
    {
        var shown = Show();
        shown.Tab.Response.SetResponse(Created());
        shown.Tab.Response.SetFailure(RequestFailure.FromMessage("Connection refused"));

        shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
        UiTestHelpers.Pump();

        Assert.Contains("No response", VisibleBarTexts(shown.View));
        Assert.DoesNotContain("· not sent yet", VisibleBarTexts(shown.View));
        Assert.DoesNotContain(Bar(shown.View).GetVisualDescendants().OfType<IconTag>(), tag => tag.IsEffectivelyVisible);
        Close(shown);
    }

    [AvaloniaFact]
    public void Ctrl_J_collapses_and_expands_the_response()
    {
        var shown = Show();

        shown.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
        UiTestHelpers.Pump();
        Assert.True(shown.Tab.Response.IsResponseCollapsed);
        Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);

        shown.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
        UiTestHelpers.Pump();
        Assert.False(shown.Tab.Response.IsResponseCollapsed);
        Assert.Equal(GridUnitType.Star, SplitGrid(shown.View).RowDefinitions[2].Height.GridUnitType);
        Close(shown);
    }

    [AvaloniaFact]
    public void The_chevron_and_a_click_anywhere_on_the_bar_toggle_the_pane()
    {
        var shown = Show();
        shown.View.FindControl<ResponseView>("ResponsePane")!.FindControl<Button>("CollapseResponseButton")!
            .Command!.Execute(null);
        UiTestHelpers.Pump();
        Assert.True(shown.Tab.Response.IsResponseCollapsed);

        // Right of the texts, away from the chevron.
        var bar = Bar(shown.View);
        var point = bar.TranslatePoint(new Point(bar.Bounds.Width * 0.6, BarHeight / 2), shown.Window)!.Value;
        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        UiTestHelpers.Pump();

        Assert.False(shown.Tab.Response.IsResponseCollapsed);
        Close(shown);
    }

    [AvaloniaFact]
    public void The_chevron_on_the_bar_expands_once()
    {
        var shown = Show();
        shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
        UiTestHelpers.Pump();

        var chevron = Bar(shown.View).FindControl<Button>("ExpandResponseButton")!;
        var point = chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), shown.Window)!.Value;
        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        UiTestHelpers.Pump();

        Assert.False(shown.Tab.Response.IsResponseCollapsed);
        Close(shown);
    }

    [AvaloniaFact]
    public void Dragging_the_splitter_keeps_the_split_and_near_the_bottom_collapses()
    {
        var shown = Show();
        var splitter = shown.View.FindControl<GridSplitter>("Splitter")!;
        var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;

        // Up by 100 px: the response grows.
        shown.Window.MouseDown(start, MouseButton.Left);
        shown.Window.MouseMove(start + new Point(0, -50));
        shown.Window.MouseMove(start + new Point(0, -100));
        shown.Window.MouseUp(start + new Point(0, -100), MouseButton.Left);
        UiTestHelpers.Pump();
        var draggedShare = shown.Tab.Response.ResponseSplitRatio;
        Assert.True(draggedShare > 0.55, $"The response's share is {draggedShare}.");

        // Down to the bottom edge: the pane collapses and keeps the share it had before this drag.
        start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;
        var bottom = SplitGrid(shown.View).TranslatePoint(new Point(0, SplitGrid(shown.View).Bounds.Height - 10), shown.Window)!.Value;
        shown.Window.MouseDown(start, MouseButton.Left);
        shown.Window.MouseMove(new Point(start.X, (start.Y + bottom.Y) / 2));
        shown.Window.MouseMove(new Point(start.X, bottom.Y));
        shown.Window.MouseUp(new Point(start.X, bottom.Y), MouseButton.Left);
        UiTestHelpers.Pump();
        Assert.True(shown.Tab.Response.IsResponseCollapsed);
        Assert.Equal(draggedShare, shown.Tab.Response.ResponseSplitRatio);

        shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
        UiTestHelpers.Pump();
        Assert.Equal(draggedShare, ResponseShare(shown.View), 3);
        Close(shown);
    }

    [AvaloniaFact]
    public void A_double_click_on_the_splitter_collapses_the_pane()
    {
        var shown = Show();
        var splitter = shown.View.FindControl<GridSplitter>("Splitter")!;
        var point = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;

        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        UiTestHelpers.Pump();

        Assert.True(shown.Tab.Response.IsResponseCollapsed);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, shown.Tab.Response.ResponseSplitRatio, 3);
        Close(shown);
    }

    [AvaloniaFact]
    public void A_settings_change_keeps_the_split_and_the_collapse()
    {
        var shown = Show();
        try
        {
            shown.Tab.Response.ResponseSplitRatio = 0.3;
            UiTestHelpers.Pump();
            Settings.ToggleTheme();
            UiTestHelpers.Pump();
            Assert.Equal(0.3, ResponseShare(shown.View), 3);

            shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            Settings.ToggleTheme();
            UiTestHelpers.Pump();
            Assert.True(shown.Tab.Response.IsResponseCollapsed);
            Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);

            shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            Assert.Equal(0.3, ResponseShare(shown.View), 3);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_shows_the_pane_and_ignores_the_collapse()
    {
        var shown = Show();
        try
        {
            shown.Tab.Response.ToggleCollapsedCommand.Execute(null);
            shown.Tab.Response.SetLayoutCommand.Execute(ResponseLayout.Horizontal);
            UiTestHelpers.Pump();

            Assert.True(shown.View.FindControl<ResponseView>("ResponsePane")!.IsEffectivelyVisible);
            Assert.False(Bar(shown.View).IsEffectivelyVisible);
            Assert.True(shown.View.FindControl<GridSplitter>("Splitter")!.IsEffectivelyVisible);
            Assert.Empty(SplitGrid(shown.View).RowDefinitions);
            Assert.False(shown.View.FindControl<ResponseView>("ResponsePane")!.FindControl<Button>("CollapseResponseButton")!.IsVisible);

            shown.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            UiTestHelpers.Pump();
            Assert.True(shown.Tab.Response.IsResponseCollapsed);
            Assert.False(Bar(shown.View).IsEffectivelyVisible);

            shown.Tab.Response.SetLayoutCommand.Execute(ResponseLayout.Vertical);
            UiTestHelpers.Pump();
            Assert.True(Bar(shown.View).IsEffectivelyVisible);
            Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);
        }
        finally
        {
            shown.Tab.Response.SetLayoutCommand.Execute(ResponseLayout.Vertical);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void The_palette_offers_the_toggle_with_its_shortcut()
    {
        var shown = Show();
        shown.ViewModel.CommandPalette.Open();
        shown.ViewModel.CommandPalette.Query = "collapse";

        var item = Assert.Single(shown.ViewModel.CommandPalette.Results, i => i.Title == "Collapse / expand response");
        Assert.Equal("Ctrl J", item.Shortcut);
        shown.ViewModel.CommandPalette.Run(item);

        Assert.True(shown.Tab.Response.IsResponseCollapsed);
        Close(shown);
    }
}
