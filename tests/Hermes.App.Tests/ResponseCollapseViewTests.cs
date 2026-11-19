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

/// <summary>The response pane collapsed to its bar (stacked, issue #45) or strip (side by side, issue #46), in a shown window.</summary>
public class ResponseCollapseViewTests
{
    private const double BarHeight = 40;
    private const double StripWidth = 44;

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

        shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
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

        shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
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

        shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
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
        Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
        Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);

        shown.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
        UiTestHelpers.Pump();
        Assert.False(shown.Tab.Response.Pane.IsResponseCollapsed);
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
        Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);

        // Right of the texts, away from the chevron.
        var bar = Bar(shown.View);
        var point = bar.TranslatePoint(new Point(bar.Bounds.Width * 0.6, BarHeight / 2), shown.Window)!.Value;
        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        UiTestHelpers.Pump();

        Assert.False(shown.Tab.Response.Pane.IsResponseCollapsed);
        Close(shown);
    }

    [AvaloniaFact]
    public void The_chevron_on_the_bar_expands_once()
    {
        var shown = Show();
        shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
        UiTestHelpers.Pump();

        var chevron = Bar(shown.View).FindControl<Button>("ExpandResponseButton")!;
        var point = chevron.TranslatePoint(new Point(chevron.Bounds.Width / 2, chevron.Bounds.Height / 2), shown.Window)!.Value;
        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        UiTestHelpers.Pump();

        Assert.False(shown.Tab.Response.Pane.IsResponseCollapsed);
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
        var draggedShare = shown.Tab.Response.Pane.ResponseSplitRatio;
        Assert.True(draggedShare > 0.55, $"The response's share is {draggedShare}.");

        // Down to the bottom edge: the pane collapses and keeps the share it had before this drag.
        start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;
        var bottom = SplitGrid(shown.View).TranslatePoint(new Point(0, SplitGrid(shown.View).Bounds.Height - 10), shown.Window)!.Value;
        shown.Window.MouseDown(start, MouseButton.Left);
        shown.Window.MouseMove(new Point(start.X, (start.Y + bottom.Y) / 2));
        shown.Window.MouseMove(new Point(start.X, bottom.Y));
        shown.Window.MouseUp(new Point(start.X, bottom.Y), MouseButton.Left);
        UiTestHelpers.Pump();
        Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
        Assert.Equal(draggedShare, shown.Tab.Response.Pane.ResponseSplitRatio);

        shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
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

        Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, shown.Tab.Response.Pane.ResponseSplitRatio, 3);
        Close(shown);
    }

    [AvaloniaFact]
    public void A_settings_change_keeps_the_split_and_the_collapse()
    {
        var shown = Show();
        try
        {
            shown.Tab.Response.Pane.ResponseSplitRatio = 0.3;
            UiTestHelpers.Pump();
            Settings.ToggleTheme();
            UiTestHelpers.Pump();
            Assert.Equal(0.3, ResponseShare(shown.View), 3);

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            Settings.ToggleTheme();
            UiTestHelpers.Pump();
            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
            Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            Assert.Equal(0.3, ResponseShare(shown.View), 3);
        }
        finally
        {
            Close(shown);
        }
    }

    private static CollapsedResponseStrip Strip(RequestTabView view) => view.FindControl<CollapsedResponseStrip>("ResponseStrip")!;

    /// <summary>The texts a person can see on the strip.</summary>
    private static List<string> VisibleStripTexts(RequestTabView view) => Strip(view).GetVisualDescendants()
        .OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "").ToList();

    /// <summary>The response pane's share of the width, from the grid's column lengths.</summary>
    private static double ResponseWidthShare(RequestTabView view)
    {
        var columns = SplitGrid(view).ColumnDefinitions;
        return columns[2].Width.Value / (columns[0].Width.Value + columns[2].Width.Value);
    }

    private static void ShowSideBySide(Shown shown)
    {
        shown.Tab.Response.Pane.SetLayoutCommand.Execute(ResponseLayout.Horizontal);
        UiTestHelpers.Pump();
    }

    private static void ShowStacked(Shown shown)
    {
        shown.Tab.Response.Pane.SetLayoutCommand.Execute(ResponseLayout.Vertical);
        UiTestHelpers.Pump();
    }

    [AvaloniaFact]
    public void Side_by_side_collapsed_the_response_column_is_the_strip_showing_the_status()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            shown.Tab.Response.SetResponse(Created());

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();

            var responseColumn = SplitGrid(shown.View).ColumnDefinitions[2];
            Assert.Equal(new GridLength(StripWidth, GridUnitType.Pixel), responseColumn.Width);
            Assert.Equal(StripWidth, responseColumn.ActualWidth);
            Assert.True(Strip(shown.View).IsEffectivelyVisible);
            Assert.False(Bar(shown.View).IsEffectivelyVisible);
            Assert.False(shown.View.FindControl<ResponseView>("ResponsePane")!.IsEffectivelyVisible);
            Assert.False(shown.View.FindControl<GridSplitter>("Splitter")!.IsEffectivelyVisible);
            Assert.Contains("201", VisibleStripTexts(shown.View));
            Assert.Contains("412 ms", VisibleStripTexts(shown.View));
            Assert.Contains("Response", VisibleStripTexts(shown.View));
            var dot = Strip(shown.View).FindControl<Panel>("StatusDot")!;
            Assert.True(dot.IsEffectivelyVisible);
            Assert.Equal("201 Created", ToolTip.GetTip(dot));
            Assert.Equal("Expand response", Strip(shown.View).FindControl<Button>("ExpandResponseButton")!.GetValue(Avalonia.Automation.AutomationProperties.NameProperty));
            UiTestHelpers.Screenshot(shown.Window, "46-collapsed-response-strip");
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_expanding_restores_the_previous_width_share()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            shown.Tab.Response.Pane.SideBySideResponseSplitRatio = 0.35;
            UiTestHelpers.Pump();
            Assert.Equal(0.35, ResponseWidthShare(shown.View), 3);

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();

            Assert.Equal(0.35, ResponseWidthShare(shown.View), 3);
            Assert.True(shown.View.FindControl<GridSplitter>("Splitter")!.IsEffectivelyVisible);
            Assert.False(Strip(shown.View).IsEffectivelyVisible);
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_before_the_first_send_the_strip_says_not_sent_yet_without_a_dot()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();

            Assert.Contains("· not sent yet", VisibleStripTexts(shown.View));
            Assert.False(Strip(shown.View).FindControl<Panel>("StatusDot")!.IsEffectivelyVisible);
            Assert.False(Strip(shown.View).FindControl<PathIcon>("NoResponseIcon")!.IsEffectivelyVisible);
            Assert.DoesNotContain("· No response", VisibleStripTexts(shown.View));
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_after_a_failure_the_strip_says_no_response()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            shown.Tab.Response.SetResponse(Created());
            shown.Tab.Response.SetFailure(RequestFailure.FromMessage("Connection refused"));

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();

            Assert.Contains("· No response", VisibleStripTexts(shown.View));
            Assert.True(Strip(shown.View).FindControl<PathIcon>("NoResponseIcon")!.IsEffectivelyVisible);
            Assert.False(Strip(shown.View).FindControl<Panel>("StatusDot")!.IsEffectivelyVisible);
            Assert.DoesNotContain("201", VisibleStripTexts(shown.View));
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Switching_layout_while_collapsed_keeps_the_pane_collapsed_in_both_directions()
    {
        var shown = Show();
        try
        {
            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            Assert.True(Bar(shown.View).IsEffectivelyVisible);
            Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);

            ShowSideBySide(shown);
            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
            Assert.True(Strip(shown.View).IsEffectivelyVisible);
            Assert.False(Bar(shown.View).IsEffectivelyVisible);
            Assert.Empty(SplitGrid(shown.View).RowDefinitions);
            Assert.Equal(StripWidth, SplitGrid(shown.View).ColumnDefinitions[2].ActualWidth);

            ShowStacked(shown);
            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
            Assert.True(Bar(shown.View).IsEffectivelyVisible);
            Assert.False(Strip(shown.View).IsEffectivelyVisible);
            Assert.Empty(SplitGrid(shown.View).ColumnDefinitions);
            Assert.Equal(BarHeight, SplitGrid(shown.View).RowDefinitions[2].ActualHeight);
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_Ctrl_J_and_the_header_chevron_toggle_the_pane()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            var chevron = shown.View.FindControl<ResponseView>("ResponsePane")!.FindControl<Button>("CollapseResponseButton")!;
            Assert.True(chevron.IsEffectivelyVisible);
            Assert.True(shown.View.FindControl<ResponseView>("ResponsePane")!.FindControl<PathIcon>("CollapseRightIcon")!.IsEffectivelyVisible);
            Assert.False(shown.View.FindControl<ResponseView>("ResponsePane")!.FindControl<PathIcon>("CollapseDownIcon")!.IsEffectivelyVisible);

            shown.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            UiTestHelpers.Pump();
            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
            Assert.Equal(StripWidth, SplitGrid(shown.View).ColumnDefinitions[2].ActualWidth);

            shown.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control);
            UiTestHelpers.Pump();
            Assert.False(shown.Tab.Response.Pane.IsResponseCollapsed);

            chevron.Command!.Execute(null);
            UiTestHelpers.Pump();
            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_a_click_anywhere_on_the_strip_expands_the_pane()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();

            // Low on the strip, away from the chevron and the text.
            var strip = Strip(shown.View);
            var point = strip.TranslatePoint(new Point(strip.Bounds.Width / 2, strip.Bounds.Height - 20), shown.Window)!.Value;
            shown.Window.MouseDown(point, MouseButton.Left);
            shown.Window.MouseUp(point, MouseButton.Left);
            UiTestHelpers.Pump();

            Assert.False(shown.Tab.Response.Pane.IsResponseCollapsed);
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_a_double_click_on_the_splitter_collapses_the_pane()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            var splitter = shown.View.FindControl<GridSplitter>("Splitter")!;
            var point = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;

            shown.Window.MouseDown(point, MouseButton.Left);
            shown.Window.MouseUp(point, MouseButton.Left);
            shown.Window.MouseDown(point, MouseButton.Left);
            shown.Window.MouseUp(point, MouseButton.Left);
            UiTestHelpers.Pump();

            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
        }
        finally
        {
            ShowStacked(shown);
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Side_by_side_dragging_keeps_the_width_and_near_the_right_edge_collapses()
    {
        var shown = Show();
        try
        {
            ShowSideBySide(shown);
            var splitter = shown.View.FindControl<GridSplitter>("Splitter")!;
            var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;

            // Left by 100 px: the response grows.
            shown.Window.MouseDown(start, MouseButton.Left);
            shown.Window.MouseMove(start + new Point(-50, 0));
            shown.Window.MouseMove(start + new Point(-100, 0));
            shown.Window.MouseUp(start + new Point(-100, 0), MouseButton.Left);
            UiTestHelpers.Pump();
            var draggedShare = shown.Tab.Response.Pane.SideBySideResponseSplitRatio;
            Assert.True(draggedShare > 0.55, $"The response's share is {draggedShare}.");
            Assert.Equal(SessionTab.DefaultResponseSplitRatio, shown.Tab.Response.Pane.ResponseSplitRatio);

            // To the right edge: the pane collapses and keeps the share it had before this drag.
            start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), shown.Window)!.Value;
            var right = SplitGrid(shown.View).TranslatePoint(new Point(SplitGrid(shown.View).Bounds.Width - 10, 0), shown.Window)!.Value;
            shown.Window.MouseDown(start, MouseButton.Left);
            shown.Window.MouseMove(new Point((start.X + right.X) / 2, start.Y));
            shown.Window.MouseMove(new Point(right.X, start.Y));
            shown.Window.MouseUp(new Point(right.X, start.Y), MouseButton.Left);
            UiTestHelpers.Pump();
            Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
            Assert.Equal(draggedShare, shown.Tab.Response.Pane.SideBySideResponseSplitRatio);

            shown.Tab.Response.Pane.ToggleCollapsedCommand.Execute(null);
            UiTestHelpers.Pump();
            Assert.Equal(draggedShare, ResponseWidthShare(shown.View), 3);
        }
        finally
        {
            ShowStacked(shown);
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

        Assert.True(shown.Tab.Response.Pane.IsResponseCollapsed);
        Close(shown);
    }
}
