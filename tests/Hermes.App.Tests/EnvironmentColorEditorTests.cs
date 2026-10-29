using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The colour swatch row in the environment editor.</summary>
public class EnvironmentColorEditorTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaFact]
    public async Task Choosing_a_colour_marks_the_tab_dirty_and_saves_it()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var env = Workspace.CreateEnvironment("Color " + Guid.NewGuid());
        try
        {
            vm.OpenEnvironment(env);
            var tab = Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
            Assert.Equal(EnvironmentColor.None, tab.Color);
            Assert.False(tab.IsDirty);

            tab.Color = EnvironmentColor.Teal;
            Assert.True(tab.IsDirty);
            Assert.Equal(EnvironmentColor.None, env.Color);

            tab.Color = EnvironmentColor.None;
            Assert.False(tab.IsDirty);

            tab.Color = EnvironmentColor.Purple;
            Assert.True(await tab.SaveAsync());
            Assert.False(tab.IsDirty);
            Assert.Equal(EnvironmentColor.Purple, env.Color);
            Assert.False(env.ConfirmBeforeSend);

            var repository = App.Services.GetRequiredService<WorkspaceRepository>();
            var reloaded = new WorkspaceRepository(repository.Root).LoadEnvironments().Single(e => e.Id == env.Id);
            Assert.Equal(EnvironmentColor.Purple, reloaded.Color);
        }
        finally
        {
            Workspace.DeleteEnvironment(env);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Globals_never_take_a_colour()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            vm.OpenEnvironment(Workspace.Globals);
            var tab = Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();
            Assert.False(ColorSwatches(window).IsEffectivelyVisible);

            tab.Color = EnvironmentColor.Red;
            await tab.SaveAsync();
            Assert.Equal(EnvironmentColor.None, Workspace.Globals.Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Swatches_are_one_named_radio_group_driven_by_the_keyboard()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var env = Workspace.CreateEnvironment("Color " + Guid.NewGuid());
        try
        {
            vm.OpenEnvironment(env);
            var tab = Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();
            var swatches = ColorSwatches(window);

            var items = swatches.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            Assert.Equal(
                ["No color", "Red", "Orange", "Amber", "Green", "Teal", "Blue", "Purple", "Pink"],
                items.Select(AutomationProperties.GetName));
            Assert.All(items, item => Assert.Equal(32, item.Bounds.Width));

            // Tab lands on the chosen swatch; arrows move the choice.
            items[0].Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            UiTestHelpers.Pump();
            Assert.Equal(EnvironmentColor.Orange, tab.Color);
            Assert.True(tab.IsDirty);

            // The group is a single tab stop: Tab leaves it, Shift+Tab comes back to the chosen swatch.
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            UiTestHelpers.Pump();
            var focused = window.FocusManager!.GetFocusedElement() as Visual;
            Assert.False(focused is not null && swatches.IsVisualAncestorOf(focused));
            UiTestHelpers.Screenshot(window, "23-environment-color");

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Shift);
            UiTestHelpers.Pump();
            Assert.Same(items[2], window.FocusManager!.GetFocusedElement());
        }
        finally
        {
            Workspace.DeleteEnvironment(env);
            window.Close();
        }
    }

    private static ListBox ColorSwatches(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "ColorSwatches");
}
