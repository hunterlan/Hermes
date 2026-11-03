using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;

namespace Hermes.App.Tests;

public class TabStripTests
{
    private const int ManyTabs = 25;
    private const double WheelStepPixels = 60;
    private const double NewTabGapTolerance = 8;

    [AvaloniaFact]
    public void Opening_many_tabs_scrolls_the_newest_into_view_and_keeps_new_tab_visible()
    {
        var (window, vm) = OpenWindowWithTabs(ManyTabs);
        try
        {
            var scroller = window.FindControl<ScrollViewer>("TabScroller")!;
            Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
            // New tabs open right after the active one, so the newest tab is the active one.
            Assert.True(IsInView(scroller, TabContainer(window, vm.Tabs.IndexOf(vm.ActiveTab!))));
            Assert.True(IsButtonInsideWindow(window));
        }
        finally
        {
            CloseWindow(window, vm);
        }
    }

    [AvaloniaFact]
    public void Selecting_a_tab_scrolls_it_into_view()
    {
        var (window, vm) = OpenWindowWithTabs(ManyTabs);
        try
        {
            var scroller = window.FindControl<ScrollViewer>("TabScroller")!;

            vm.ActiveTab = vm.Tabs[0];
            UiTestHelpers.Pump();

            Assert.Equal(0, scroller.Offset.X);
            Assert.True(IsInView(scroller, TabContainer(window, 0)));
        }
        finally
        {
            CloseWindow(window, vm);
        }
    }

    [AvaloniaFact]
    public void Mouse_wheel_over_the_tab_strip_scrolls_it_sideways_one_step_at_a_time()
    {
        var (window, vm) = OpenWindowWithTabs(ManyTabs);
        try
        {
            var scroller = window.FindControl<ScrollViewer>("TabScroller")!;
            var wheelPoint = scroller.TranslatePoint(new Point(20, 10), window)!.Value;
            var startOffset = scroller.Offset.X;
            Assert.True(startOffset > 0);

            // Wheel up (positive Y) moves toward the first tab, by exactly one step (no double scrolling).
            window.MouseWheel(wheelPoint, new Vector(0, 1));
            UiTestHelpers.Pump();
            Assert.Equal(startOffset - WheelStepPixels, scroller.Offset.X, 3);

            // A horizontal swipe (positive X) also moves exactly one step.
            var beforeSwipe = scroller.Offset.X;
            window.MouseWheel(wheelPoint, new Vector(1, 0));
            UiTestHelpers.Pump();
            Assert.Equal(beforeSwipe - WheelStepPixels, scroller.Offset.X, 3);

            // Wheel down (negative Y) moves back, and the offset never goes past the end.
            for (var i = 0; i < 100; i++)
                window.MouseWheel(wheelPoint, new Vector(0, -1));
            UiTestHelpers.Pump();
            Assert.Equal(scroller.Extent.Width - scroller.Viewport.Width, scroller.Offset.X, 3);
        }
        finally
        {
            CloseWindow(window, vm);
        }
    }

    [AvaloniaFact]
    public void New_tab_button_follows_the_last_tab_while_tabs_fit()
    {
        var (window, vm) = OpenWindowWithTabs(2);
        try
        {
            var scroller = window.FindControl<ScrollViewer>("TabScroller")!;
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width);

            var lastTabRight = TabContainer(window, vm.Tabs.Count - 1).TranslatePoint(
                new Point(TabContainer(window, vm.Tabs.Count - 1).Bounds.Width, 0), window)!.Value.X;
            var buttonLeft = window.FindControl<Button>("NewTabButton")!.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var gap = buttonLeft - lastTabRight;
            Assert.InRange(gap, 0, NewTabGapTolerance);
        }
        finally
        {
            CloseWindow(window, vm);
        }
    }

    /// <summary>
    /// Shows a window with exactly <paramref name="tabCount"/> request tabs. The saved session is restored on
    /// startup, so the leftover tabs are removed first.
    /// </summary>
    private static (Window Window, MainWindowViewModel ViewModel) OpenWindowWithTabs(int tabCount)
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        RemoveAllTabs(vm);
        for (var i = 0; i < tabCount; i++)
            vm.NewRequestCommand.Execute(null);
        UiTestHelpers.Pump();
        return (window, vm);
    }

    private static void CloseWindow(Window window, MainWindowViewModel vm)
    {
        RemoveAllTabs(vm);
        window.Close();
    }

    private static void RemoveAllTabs(MainWindowViewModel vm)
    {
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
    }

    private static Control TabContainer(Window window, int index) =>
        window.FindControl<ItemsControl>("TabItems")!.ContainerFromIndex(index)!;

    private static bool IsInView(ScrollViewer scroller, Control container)
    {
        var origin = container.TranslatePoint(new Point(0, 0), scroller);
        return origin is { } point
            && point.X >= -0.5
            && point.X + container.Bounds.Width <= scroller.Viewport.Width + 0.5;
    }

    private static bool IsButtonInsideWindow(Window window)
    {
        var button = window.FindControl<Button>("NewTabButton")!;
        var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X;
        return right <= window.Bounds.Width;
    }
}
