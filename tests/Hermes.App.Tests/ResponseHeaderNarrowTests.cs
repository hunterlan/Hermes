using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

/// <summary>
/// The response pane's header and its neighbours in a narrow pane: nothing draws over anything else, and the least
/// important items are dropped first.
/// </summary>
public class ResponseHeaderNarrowTests
{
    private const double Wide = 1000;
    private const double Narrowest = 60;

    /// <summary>The header items from the first to drop to the last; the Saved label goes as the compact button takes over.</summary>
    private static readonly string[] DropOrder = ["SentWithEnvironment", "SavedButton", "SizeText", "TimeText", "ResponseLabel"];

    private static readonly string[] AllItems =
    [
        "CollapseResponseButton", "ResponseLabel", "HeaderStatusTag", "TimeText", "SizeText", "SentWithEnvironment",
        "EarlierResponseTag", "CompactSavedButton", "SavedButton", "SideBySideButton", "StackedButton",
    ];

    private static readonly string[] NeverDropped =
        ["CollapseResponseButton", "HeaderStatusTag", "EarlierResponseTag", "SideBySideButton", "StackedButton"];

    private sealed record Shown(ShownMainWindow Main, Window Host, ResponseView View, ResponseViewModel Response)
    {
        public DropPriorityPanel Header => View.FindControl<DropPriorityPanel>("ResponseHeader")!;
    }

    /// <summary>A response pane in a window of its own, so its width is set exactly.</summary>
    private static Shown Show(double width, Action<ResponseViewModel>? prepare = null)
    {
        var main = UiTestHelpers.ShowMainWindow();
        var response = Assert.IsType<RequestTabViewModel>(main.ViewModel.ActiveTab).Response;
        response.Pane.SetLayoutCommand.Execute(ResponseLayout.Horizontal);
        response.SetResponse(Created(), new EnvironmentModel { Name = "Production environment", Color = EnvironmentColor.Teal });
        prepare?.Invoke(response);
        var view = new ResponseView { DataContext = response };
        var host = new Window { Width = width, Height = 300, Content = view };
        host.Show();
        UiTestHelpers.Pump();
        return new Shown(main, host, view, response);
    }

    private static void Close(Shown shown)
    {
        // The layout is a setting shared by every test; put it back.
        shown.Response.Pane.SetLayoutCommand.Execute(ResponseLayout.Vertical);
        shown.Host.Close();
        shown.Main.Window.Close();
        UiTestHelpers.Pump();
    }

    private static void Resize(Shown shown, double width)
    {
        shown.Host.Width = width;
        UiTestHelpers.Pump();
    }

    private static ResponseModel Created() => new()
    {
        StatusCode = 201,
        ReasonPhrase = "Created",
        ContentType = "text/plain",
        Body = "ok"u8.ToArray(),
        TotalTime = TimeSpan.FromMilliseconds(412),
    };

    private static void SaveThreeResponses(ResponseViewModel response) =>
        response.Saved.SetSaved(
            [.. Enumerable.Range(0, 3).Select(_ => new SavedResponse { Response = new ResponseModel { StatusCode = 201, ReasonPhrase = "Created" } })],
            show: false);

    private static void ViewAnEarlierResponse(ResponseViewModel response) =>
        response.Saved.ShowSavedCommand.Execute(response.Saved.Items[1]);

    private static Control Item(Shown shown, string name) => name switch
    {
        "SideBySideButton" => ButtonNamed(shown, "Side by side layout"),
        "StackedButton" => ButtonNamed(shown, "Stacked layout"),
        _ => shown.View.FindControl<Control>(name)!,
    };

    private static Button ButtonNamed(Shown shown, string automationName) => shown.Header.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == automationName);

    /// <summary>Whether the item is on screen: it has something to show, wasn't dropped, and has a size.</summary>
    private static bool IsShown(Shown shown, string name)
    {
        var item = Item(shown, name);
        return item.IsVisible && !DropPriorityPanel.GetIsDropped(item) && item.Bounds.Width > 0;
    }

    private static List<string> ShownItems(Shown shown) => AllItems.Where(name => IsShown(shown, name)).ToList();

    /// <summary>The header items at the right edge; the others are the group from the left.</summary>
    private static readonly string[] EndAlignedItems =
        ["EarlierResponseTag", "CompactSavedButton", "SavedButton", "SideBySideButton", "StackedButton"];

    private const double GroupSpacing = 8;

    private static void AssertNothingOverlaps(Shown shown)
    {
        var shownItems = ShownItems(shown);
        var startRights = shownItems.Where(name => !EndAlignedItems.Contains(name)).Select(name => Item(shown, name).Bounds.Right).ToList();
        var endLefts = shownItems.Where(EndAlignedItems.Contains).Select(name => Item(shown, name).Bounds.X).ToList();
        if (startRights.Count > 0 && endLefts.Count > 0)
        {
            Assert.True(endLefts.Min() - startRights.Max() >= GroupSpacing - 0.01,
                $"The left items end at {startRights.Max()} and the right ones start at {endLefts.Min()} at width {shown.Host.Width}.");
        }
        foreach (var first in shownItems)
        {
            foreach (var second in shownItems.Where(other => string.CompareOrdinal(other, first) > 0))
            {
                Assert.False(Item(shown, first).Bounds.Intersects(Item(shown, second).Bounds),
                    $"{first} {Item(shown, first).Bounds} overlaps {second} {Item(shown, second).Bounds} at width {shown.Host.Width}.");
            }
        }
    }

    /// <summary>Items drop in order: when one is gone, so is everything that goes before it.</summary>
    private static void AssertDroppedInOrder(Shown shown)
    {
        var isDroppedInOrder = DropOrder.Select(name => DropPriorityPanel.GetIsDropped(Item(shown, name))).ToList();
        for (var index = 1; index < isDroppedInOrder.Count; index++)
        {
            Assert.True(!isDroppedInOrder[index] || isDroppedInOrder[index - 1],
                $"{DropOrder[index]} is dropped while {DropOrder[index - 1]} is not, at width {shown.Host.Width}.");
        }
    }

    [AvaloniaFact]
    public void Wide_shows_every_item_in_the_old_arrangement()
    {
        var shown = Show(Wide);
        try
        {
            Assert.Equal(0, shown.Header.Step);
            foreach (var name in new[] { "CollapseResponseButton", "ResponseLabel", "HeaderStatusTag", "TimeText", "SizeText", "SentWithEnvironment", "SideBySideButton", "StackedButton" })
                Assert.True(IsShown(shown, name), name);
            Assert.False(IsShown(shown, "EarlierResponseTag"));
            AssertNothingOverlaps(shown);
            // The layout buttons sit flush right, the rest from the left.
            var stacked = Item(shown, "StackedButton");
            Assert.Equal(shown.Header.Bounds.Width, stacked.Bounds.Right, 0.5);
            Assert.Equal(0, Item(shown, "CollapseResponseButton").Bounds.X);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Narrowing_drops_the_items_in_order_and_nothing_overlaps()
    {
        var shown = Show(Wide);
        try
        {
            var previousStep = -1;
            foreach (var width in new double[] { Wide, 650, 450, 400, 350, 300, 250, 200, 150, Narrowest })
            {
                Resize(shown, width);
                AssertNothingOverlaps(shown);
                AssertDroppedInOrder(shown);
                Assert.True(shown.Header.Step >= previousStep, $"The step went back from {previousStep} to {shown.Header.Step} at width {width}.");
                previousStep = shown.Header.Step;
                foreach (var name in NeverDropped.Where(name => name != "EarlierResponseTag"))
                    Assert.False(DropPriorityPanel.GetIsDropped(Item(shown, name)), $"{name} is dropped at width {width}.");
            }
            // Everything that can go has gone at the narrowest.
            Assert.Equal(5, shown.Header.Step);
            Assert.False(IsShown(shown, "ResponseLabel"));
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Sweeping_every_few_pixels_never_lets_the_two_groups_touch()
    {
        var shown = Show(Wide, response => { SaveThreeResponses(response); ViewAnEarlierResponse(response); });
        try
        {
            for (var width = Wide; width >= Narrowest; width -= 3)
            {
                Resize(shown, width);
                AssertNothingOverlaps(shown);
            }
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Sweeping_without_saved_responses_never_lets_the_two_groups_touch()
    {
        var shown = Show(Wide);
        try
        {
            for (var width = Wide; width >= Narrowest; width -= 3)
            {
                Resize(shown, width);
                AssertNothingOverlaps(shown);
            }
        }
        finally
        {
            Close(shown);
        }
    }

    /// <summary>The pixels (BGRA) of a region of the window's last rendered frame.</summary>
    private static byte[] PixelsOf(Window window, PixelRect region)
    {
        UiTestHelpers.Pump();
        using var frame = window.CaptureRenderedFrame()!;
        using var framebuffer = frame.Lock();
        var bytes = new byte[region.Width * region.Height * 4];
        for (var row = 0; row < region.Height; row++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                framebuffer.Address + (region.Y + row) * framebuffer.RowBytes + region.X * 4, bytes, row * region.Width * 4, region.Width * 4);
        }
        return bytes;
    }

    /// <summary>The header's first item, the chevron, and the strip above and below it.</summary>
    private static PixelRect ChevronRegion(Shown shown)
    {
        var chevron = Item(shown, "CollapseResponseButton");
        return new PixelRect(0, 0, (int)Math.Ceiling(chevron.Bounds.Width), (int)Math.Ceiling(shown.Header.Bounds.Bottom) + 4);
    }

    [AvaloniaFact]
    public void A_wide_header_with_saved_responses_draws_the_chevron_like_one_without()
    {
        var without = Show(Wide);
        byte[] expected;
        try
        {
            expected = PixelsOf(without.Host, ChevronRegion(without));
        }
        finally
        {
            Close(without);
        }

        var with = Show(Wide, SaveThreeResponses);
        try
        {
            Assert.True(DropPriorityPanel.GetIsDropped(Item(with, "CompactSavedButton")));
            Assert.Equal(expected, PixelsOf(with.Host, ChevronRegion(with)));
        }
        finally
        {
            Close(with);
        }
    }

    [AvaloniaFact]
    public void At_every_width_the_dropped_items_draw_nothing_over_the_chevron()
    {
        var plain = Show(Wide);
        byte[] expected;
        try
        {
            expected = PixelsOf(plain.Host, ChevronRegion(plain));
        }
        finally
        {
            Close(plain);
        }

        var shown = Show(Wide, response => { SaveThreeResponses(response); ViewAnEarlierResponse(response); });
        try
        {
            for (var width = Wide; width >= 120; width -= 20)
            {
                Resize(shown, width);
                foreach (var name in AllItems.Where(name => Item(shown, name).IsVisible && DropPriorityPanel.GetIsDropped(Item(shown, name))))
                    Assert.True(Item(shown, name).Bounds.X >= shown.Header.Bounds.Width, $"{name} is dropped but inside the header at width {width}.");
                Assert.Equal(expected, PixelsOf(shown.Host, ChevronRegion(shown)));
            }
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void A_dropped_item_with_a_bound_opacity_comes_back_at_the_right_opacity_also_while_loading()
    {
        var shown = Show(Wide);
        try
        {
            var time = Item(shown, "TimeText");
            var size = Item(shown, "SizeText");
            Assert.Equal(1, time.Opacity);

            Resize(shown, 200);
            Assert.True(DropPriorityPanel.GetIsDropped(time));
            // The binding changes the opacity while the item is dropped.
            shown.Response.IsLoading = true;
            UiTestHelpers.Pump();
            Assert.True(time.Opacity < 1);
            Assert.True(time.Bounds.X >= shown.Header.Bounds.Width);

            Resize(shown, Wide);
            Assert.False(DropPriorityPanel.GetIsDropped(time));
            Assert.True(time.Opacity < 1);
            Assert.Equal(time.Opacity, size.Opacity);

            shown.Response.IsLoading = false;
            UiTestHelpers.Pump();
            Assert.Equal(1, time.Opacity);
            Assert.Equal(1, size.Opacity);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Each_item_drops_at_its_own_step()
    {
        var shown = Show(Wide, SaveThreeResponses);
        try
        {
            var seenSteps = new SortedSet<int>();
            for (var width = Wide; width >= Narrowest; width -= 5)
            {
                Resize(shown, width);
                seenSteps.Add(shown.Header.Step);
                var droppedCount = DropOrder.Count(name => DropPriorityPanel.GetIsDropped(Item(shown, name)));
                // Step 2 drops the Saved label and the compact button takes over, so that step drops two names of the order.
                Assert.Equal(shown.Header.Step, droppedCount);
            }
            Assert.Equal([0, 1, 2, 3, 4, 5], seenSteps);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Widening_brings_the_items_back_in_reverse_order_to_the_same_layout()
    {
        var shown = Show(Wide);
        try
        {
            var widths = new double[] { Wide, 450, 350, 250, 150, Narrowest };
            var goingDown = new List<(double Width, List<string> Items, int Step)>();
            foreach (var width in widths)
            {
                Resize(shown, width);
                goingDown.Add((width, ShownItems(shown), shown.Header.Step));
            }

            foreach (var (width, items, step) in goingDown.AsEnumerable().Reverse())
            {
                Resize(shown, width);
                Assert.Equal(items, ShownItems(shown));
                Assert.Equal(step, shown.Header.Step);
            }
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void A_narrow_pane_keeps_the_chevron_the_status_and_the_layout_buttons()
    {
        var shown = Show(250);
        try
        {
            foreach (var name in new[] { "CollapseResponseButton", "HeaderStatusTag", "SideBySideButton", "StackedButton" })
                Assert.True(IsShown(shown, name), name);
            Assert.False(IsShown(shown, "SentWithEnvironment"));
            AssertNothingOverlaps(shown);
            Assert.True(Item(shown, "StackedButton").Bounds.Right <= shown.Header.Bounds.Width + 0.5);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Below_the_minimum_the_header_is_cut_off_at_the_right_without_overlap()
    {
        var shown = Show(Narrowest);
        try
        {
            Assert.True(shown.Header.ClipToBounds);
            AssertNothingOverlaps(shown);
            Assert.Equal(0, Item(shown, "CollapseResponseButton").Bounds.X);
            Assert.True(Item(shown, "StackedButton").Bounds.Right > shown.Header.Bounds.Width, "The last button should be cut off.");
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void A_long_environment_name_is_trimmed_before_it_is_dropped()
    {
        var shown = Show(Wide, response => response.SetResponse(Created(), new EnvironmentModel { Name = new string('E', 80) }));
        try
        {
            var environment = Item(shown, "SentWithEnvironment");
            // Wide enough for everything else but not for the whole name.
            Resize(shown, 560);
            Assert.Equal(0, shown.Header.Step);
            Assert.True(IsShown(shown, "SentWithEnvironment"));
            Assert.True(environment.Bounds.Width >= 64, $"Squeezed to {environment.Bounds.Width}.");
            Assert.True(environment.Bounds.Width < environment.DesiredSize.Width + 0.5);
            AssertNothingOverlaps(shown);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void Dropped_items_cannot_be_clicked_or_tabbed_to_and_come_back_when_widened()
    {
        var shown = Show(Wide, response => { SaveThreeResponses(response); ViewAnEarlierResponse(response); });
        try
        {
            var full = Item(shown, "SavedButton");
            Assert.True(full.IsTabStop);
            Assert.True(full.IsHitTestVisible);

            Resize(shown, 300);
            Assert.True(DropPriorityPanel.GetIsDropped(full));
            Assert.False(full.IsTabStop);
            Assert.False(full.IsHitTestVisible);
            Assert.True(full.Bounds.X >= shown.Header.Bounds.Width, "A dropped item is past the right edge, where the panel clips it.");
            foreach (var name in new[] { "SizeText", "TimeText", "ResponseLabel", "SentWithEnvironment" }.Where(name => Item(shown, name).IsVisible && DropPriorityPanel.GetIsDropped(Item(shown, name))))
            {
                Assert.False(Item(shown, name).IsHitTestVisible, name);
                Assert.True(Item(shown, name).Bounds.X >= shown.Header.Bounds.Width, name);
            }
            var compact = Item(shown, "CompactSavedButton");
            Assert.True(compact.IsTabStop);
            Assert.True(compact.IsHitTestVisible);

            Resize(shown, Wide);
            Assert.False(DropPriorityPanel.GetIsDropped(full));
            Assert.True(full.IsTabStop);
            Assert.True(full.IsHitTestVisible);
            Assert.True(DropPriorityPanel.GetIsDropped(compact));
            Assert.False(compact.IsTabStop);
            Assert.False(compact.IsHitTestVisible);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void A_dropped_time_is_not_under_the_pointer()
    {
        var shown = Show(Wide);
        try
        {
            var time = Item(shown, "TimeText");
            var timeCentre = time.TranslatePoint(new Point(time.Bounds.Width / 2, time.Bounds.Height / 2), shown.Host)!.Value;
            Assert.Contains(time, (shown.Host.InputHitTest(timeCentre) as Visual)!.GetSelfAndVisualAncestors());

            Resize(shown, 250);
            Assert.True(DropPriorityPanel.GetIsDropped(time));
            // Whatever is where the time was, it is not the time.
            var hitNow = shown.Host.InputHitTest(timeCentre) as Visual;
            Assert.DoesNotContain(time, hitNow?.GetSelfAndVisualAncestors() ?? []);
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void With_saved_responses_a_narrow_pane_has_no_overlap_at_any_width()
    {
        var shown = Show(Wide, response => { SaveThreeResponses(response); ViewAnEarlierResponse(response); });
        try
        {
            Assert.Equal("Saved · 2 earlier responses", shown.Response.Saved.Label);
            Assert.True(IsShown(shown, "EarlierResponseTag"));
            foreach (var width in new double[] { Wide, 750, 650, 550, 450, 350, 250, 150, Narrowest })
            {
                Resize(shown, width);
                AssertNothingOverlaps(shown);
                AssertDroppedInOrder(shown);
                foreach (var name in new[] { "CollapseResponseButton", "HeaderStatusTag", "EarlierResponseTag", "SideBySideButton", "StackedButton" })
                    Assert.False(DropPriorityPanel.GetIsDropped(Item(shown, name)), $"{name} is dropped at width {width}.");
                // Exactly one of the two Saved buttons is on screen.
                Assert.NotEqual(IsShown(shown, "SavedButton"), IsShown(shown, "CompactSavedButton"));
            }
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void The_compact_saved_button_keeps_the_label_as_tooltip_and_name_and_opens_the_same_flyout()
    {
        var shown = Show(300, response => { SaveThreeResponses(response); ViewAnEarlierResponse(response); });
        try
        {
            var full = Assert.IsType<Button>(Item(shown, "SavedButton"));
            var compact = Assert.IsType<Button>(Item(shown, "CompactSavedButton"));
            Assert.False(IsShown(shown, "SavedButton"));
            Assert.True(IsShown(shown, "CompactSavedButton"));
            Assert.Equal("Saved · 2 earlier responses", ToolTip.GetTip(compact));
            Assert.Equal("Saved · 2 earlier responses", AutomationProperties.GetName(compact));
            Assert.DoesNotContain(compact.GetVisualDescendants().OfType<TextBlock>(), text => !string.IsNullOrEmpty(text.Text));

            var menu = Assert.IsType<MenuFlyout>(full.Flyout);
            var centre = compact.TranslatePoint(new Point(compact.Bounds.Width / 2, compact.Bounds.Height / 2), shown.Host)!.Value;
            shown.Host.MouseDown(centre, MouseButton.Left);
            shown.Host.MouseUp(centre, MouseButton.Left);
            UiTestHelpers.Pump();

            Assert.True(menu.IsOpen);
            Assert.Equal(3, menu.Items.Count);
            Assert.Same(compact, menu.Target);
            menu.Hide();
            UiTestHelpers.Pump();

            // The full button keeps its own tooltip when there is room.
            Resize(shown, Wide);
            Assert.Equal("Responses are saved with the request", ToolTip.GetTip(full));
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void A_failed_send_shows_no_header_items_but_the_chevron_and_layout_buttons_without_overlap()
    {
        var shown = Show(Wide, response => response.SetFailure(RequestFailure.FromMessage("Connection refused")));
        try
        {
            foreach (var width in new double[] { Wide, 350, 250, Narrowest })
            {
                Resize(shown, width);
                AssertNothingOverlaps(shown);
                Assert.True(IsShown(shown, "CollapseResponseButton"));
                Assert.True(IsShown(shown, "SideBySideButton"));
            }
        }
        finally
        {
            Close(shown);
        }
    }

    [AvaloniaFact]
    public void The_view_mode_row_does_not_overlap_the_copy_and_save_buttons_when_narrow()
    {
        var shown = Show(Wide);
        try
        {
            foreach (var width in new double[] { Wide, 450, 350, 250, 150 })
            {
                Resize(shown, width);
                var pretty = shown.View.GetVisualDescendants().OfType<ToggleButton>().First(button => button.Content as string == "Pretty");
                var preview = shown.View.GetVisualDescendants().OfType<ToggleButton>().First(button => button.Content as string == "Preview");
                var copy = shown.View.GetVisualDescendants().OfType<Button>().First(button => ToolTip.GetTip(button) as string == "Copy response body");
                var save = shown.View.GetVisualDescendants().OfType<Button>().First(button => ToolTip.GetTip(button) as string == "Save response to file");
                var segments = new Rect(pretty.TranslatePoint(default, shown.View)!.Value, new Size(
                    preview.TranslatePoint(new Point(preview.Bounds.Width, 0), shown.View)!.Value.X - pretty.TranslatePoint(default, shown.View)!.Value.X, pretty.Bounds.Height));
                var copyBounds = new Rect(copy.TranslatePoint(default, shown.View)!.Value, copy.Bounds.Size);
                var saveBounds = new Rect(save.TranslatePoint(default, shown.View)!.Value, save.Bounds.Size);
                Assert.False(segments.Intersects(copyBounds), $"The view modes {segments} overlap copy {copyBounds} at width {width}.");
                Assert.False(copyBounds.Intersects(saveBounds), $"Copy {copyBounds} overlaps save {saveBounds} at width {width}.");
            }
        }
        finally
        {
            Close(shown);
        }
    }

    private sealed record ShownBar(ShownMainWindow Main, Window Host, CollapsedResponseBar Bar);

    private static ShownBar ShowBar(double width, Action<ResponseViewModel> prepare)
    {
        var main = UiTestHelpers.ShowMainWindow();
        var response = Assert.IsType<RequestTabViewModel>(main.ViewModel.ActiveTab).Response;
        prepare(response);
        response.Pane.SetLayoutCommand.Execute(ResponseLayout.Vertical);
        response.Pane.ToggleCollapsedCommand.Execute(null);
        var bar = new CollapsedResponseBar { DataContext = response };
        var host = new Window { Width = width, Height = 200, Content = bar };
        host.Show();
        UiTestHelpers.Pump();
        return new ShownBar(main, host, bar);
    }

    private static void CloseBar(ShownBar shown)
    {
        shown.Host.Close();
        shown.Main.Window.Close();
        UiTestHelpers.Pump();
    }

    private static Control BarItem(ShownBar shown, string name) => shown.Bar.FindControl<Control>(name)!;

    private static List<string> ShownBarItems(ShownBar shown) =>
        new[] { "ExpandResponseButton", "ResponseLabel", "NotSentYetText", "SendingText", "TimeText", "SizeText", "ShortcutHint" }
            .Where(name => BarItem(shown, name) is { IsEffectivelyVisible: true } item && !DropPriorityPanel.GetIsDropped(item) && item.Bounds.Width > 0)
            .ToList();

    private static void AssertBarDoesNotOverlap(ShownBar shown)
    {
        var items = new[] { "ExpandResponseButton", "ResponseLabel", "TimeText", "SizeText", "ShortcutHint" }
            .Where(name => ShownBarItems(shown).Contains(name))
            .Select(name => (Name: name, Bounds: BarItem(shown, name).Bounds))
            .ToList();
        var tag = shown.Bar.GetVisualDescendants().OfType<IconTag>().FirstOrDefault(candidate => candidate.IsEffectivelyVisible);
        if (tag is not null)
            items.Add(("status", tag.TranslatePoint(default, BarItem(shown, "BarItems")) is { } origin ? new Rect(origin, tag.Bounds.Size) : default));
        foreach (var first in items)
        {
            foreach (var second in items.Where(other => string.CompareOrdinal(other.Name, first.Name) > 0))
                Assert.False(first.Bounds.Intersects(second.Bounds), $"{first.Name} {first.Bounds} overlaps {second.Name} {second.Bounds} at width {shown.Host.Width}.");
        }
        if (items.Any(item => item.Name == "ShortcutHint"))
        {
            var hintLeft = items.Single(item => item.Name == "ShortcutHint").Bounds.X;
            var startRight = items.Where(item => item.Name != "ShortcutHint").Max(item => item.Bounds.Right);
            Assert.True(hintLeft - startRight >= GroupSpacing - 0.01,
                $"The items end at {startRight} and the hint starts at {hintLeft} at width {shown.Host.Width}.");
        }
    }

    [AvaloniaFact]
    public void The_collapsed_bars_dropped_items_are_past_the_edge_and_draw_nothing_over_the_chevron()
    {
        var shown = ShowBar(1000, response => response.SetResponse(Created()));
        try
        {
            var chevron = BarItem(shown, "ExpandResponseButton");
            var region = new PixelRect(0, 0, (int)Math.Ceiling(chevron.Bounds.Width), (int)Math.Ceiling(BarItem(shown, "BarItems").Bounds.Bottom) + 8);
            var expected = PixelsOf(shown.Host, region);
            var sawDropped = false;
            for (var width = 1000.0; width >= 100; width -= 20)
            {
                shown.Host.Width = width;
                UiTestHelpers.Pump();
                foreach (var name in new[] { "ResponseLabel", "TimeText", "SizeText", "ShortcutHint" }.Where(name => DropPriorityPanel.GetIsDropped(BarItem(shown, name))))
                {
                    sawDropped = true;
                    Assert.True(BarItem(shown, name).Bounds.X >= BarItem(shown, "BarItems").Bounds.Width, $"{name} at width {width}.");
                }
                Assert.Equal(expected, PixelsOf(shown.Host, region));
            }
            Assert.True(sawDropped);
        }
        finally
        {
            CloseBar(shown);
        }
    }

    [AvaloniaFact]
    public void Sweeping_the_collapsed_bar_never_lets_the_hint_touch_the_items()
    {
        var shown = ShowBar(1000, response => response.SetResponse(Created()));
        try
        {
            for (var width = 1000.0; width >= 100; width -= 3)
            {
                shown.Host.Width = width;
                UiTestHelpers.Pump();
                AssertBarDoesNotOverlap(shown);
            }
        }
        finally
        {
            CloseBar(shown);
        }
    }

    [AvaloniaFact]
    public void The_collapsed_bar_drops_the_hint_then_size_then_time_then_the_label_and_never_overlaps()
    {
        var shown = ShowBar(1000, response => response.SetResponse(Created()));
        try
        {
            Assert.Equal(["ExpandResponseButton", "ResponseLabel", "TimeText", "SizeText", "ShortcutHint"], ShownBarItems(shown));

            var widths = new double[] { 1000, 400, 330, 300, 270, 240, 210, 180, 150, 100 };
            var shownCounts = new List<int>();
            foreach (var width in widths)
            {
                shown.Host.Width = width;
                UiTestHelpers.Pump();
                AssertBarDoesNotOverlap(shown);
                var items = ShownBarItems(shown);
                shownCounts.Add(items.Count);
                Assert.Contains("ExpandResponseButton", items);
                // Dropped in order: the hint, the size, the time, the label.
                var order = new[] { "ShortcutHint", "SizeText", "TimeText", "ResponseLabel" };
                for (var index = 1; index < order.Length; index++)
                    Assert.True(!items.Contains(order[index - 1]) || items.Contains(order[index]),
                        $"{order[index - 1]} is shown but {order[index]}, which goes after it, is not, at width {width}.");
            }
            Assert.Equal(shownCounts.OrderByDescending(count => count), shownCounts);
            Assert.Equal(["ExpandResponseButton"], ShownBarItems(shown));

            shown.Host.Width = 1000;
            UiTestHelpers.Pump();
            Assert.Equal(["ExpandResponseButton", "ResponseLabel", "TimeText", "SizeText", "ShortcutHint"], ShownBarItems(shown));
        }
        finally
        {
            CloseBar(shown);
        }
    }

    [AvaloniaFact]
    public void The_collapsed_bar_before_the_first_send_does_not_overlap_when_narrow()
    {
        var shown = ShowBar(1000, _ => { });
        try
        {
            Assert.Contains("NotSentYetText", ShownBarItems(shown));
            foreach (var width in new double[] { 1000, 250, 200, 150, 100 })
            {
                shown.Host.Width = width;
                UiTestHelpers.Pump();
                AssertBarDoesNotOverlap(shown);
                Assert.Contains("ExpandResponseButton", ShownBarItems(shown));
            }
        }
        finally
        {
            CloseBar(shown);
        }
    }
}
