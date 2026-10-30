using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views;
using Hermes.App.Views.Sidebar;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>An environment's colour dot everywhere the environment appears.</summary>
public class EnvironmentColorDisplayTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaFact]
    public async Task The_dot_shows_in_the_header_picker_sidebar_and_matrix()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var env = CreateEnvironment(EnvironmentColor.Red, confirmBeforeSend: true);
        var plain = CreateEnvironment(EnvironmentColor.None, confirmBeforeSend: false);
        var button = window.FindControl<Button>("EnvironmentButton")!;
        try
        {
            Assert.Equal(EnvironmentColor.None, window.FindControl<EnvironmentColorDot>("EnvironmentButtonDot")!.Color);
            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == env);
            UiTestHelpers.Pump();

            // Header: the dot replaces the accent indicator, and the lock still marks confirm-before-send.
            Assert.Equal(EnvironmentColor.Red, window.FindControl<EnvironmentColorDot>("EnvironmentButtonDot")!.Color);
            Assert.True(vm.ActiveEnvironmentConfirms);

            // Picker: each option's dot, with the checkmark still on the active one.
            button.Flyout!.ShowAt(button);
            UiTestHelpers.Pump();
            var list = window.FindControl<ListBox>("EnvironmentList")!;
            Assert.Equal(EnvironmentColor.Red, DotFor(list, env.Name).Color);
            Assert.Equal(EnvironmentColor.None, DotFor(list, plain.Name).Color);
            Assert.Equal(EnvironmentColor.None, DotFor(list, "No environment").Color);
            Assert.True(vm.EnvironmentOptions.Single(o => o.Model == env).IsActive);
            UiTestHelpers.Screenshot(window, "24-environment-colors");
            window.RequestedThemeVariant = ThemeVariant.Light;
            UiTestHelpers.Screenshot(window, "25-environment-colors-light");
            window.RequestedThemeVariant = null;
            button.Flyout.Hide();

            // Sidebar: environments show their dot; Globals keeps its icon.
            vm.SelectedPane = SidebarPane.Environments;
            UiTestHelpers.Pump();
            var pane = window.GetVisualDescendants().OfType<EnvironmentsPaneView>().Single();
            Assert.Equal(EnvironmentColor.Red, DotFor(pane, env.Name).Color);
            Assert.Equal(EnvironmentColor.None, DotFor(pane, plain.Name).Color);
            Assert.False(DotFor(pane, "Globals").IsVisible);

            // Matrix: one dot per environment column header, none for Globals.
            await vm.OpenEnvironmentMatrixAsync();
            UiTestHelpers.Pump();
            var matrix = window.GetVisualDescendants().OfType<EnvironmentMatrixView>().Single();
            Assert.Equal(EnvironmentColor.Red, DotFor(matrix, env.Name).Color);
            Assert.False(DotFor(matrix, "Globals").IsVisible);

            // Recolouring in the editor updates every place once saved.
            await vm.OpenEnvironmentAsync(env);
            var tab = Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
            tab.Color = EnvironmentColor.Green;
            await tab.SaveAsync();
            UiTestHelpers.Pump();
            Assert.Equal(EnvironmentColor.Green, window.FindControl<EnvironmentColorDot>("EnvironmentButtonDot")!.Color);
            Assert.Equal(EnvironmentColor.Green, DotFor(pane, env.Name).Color);
        }
        finally
        {
            button.Flyout!.Hide();
            Workspace.ActiveEnvironment = null;
            Workspace.DeleteEnvironment(env);
            Workspace.DeleteEnvironment(plain);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_response_shows_the_environment_it_was_sent_with()
    {
        using var server = new JsonServer();
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var env = CreateEnvironment(EnvironmentColor.Teal, confirmBeforeSend: false);
        try
        {
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            tab.Url = server.Url;
            await tab.SendCommand.ExecuteAsync(null);
            UiTestHelpers.Pump();
            Assert.Null(tab.Response.SentWithEnvironmentName);
            Assert.False(SentWith(window).IsVisible);

            Workspace.ActiveEnvironment = env;
            await tab.SendCommand.ExecuteAsync(null);
            UiTestHelpers.Pump();
            Assert.True(SentWith(window).IsVisible);
            Assert.Equal(env.Name, tab.Response.SentWithEnvironmentName);
            Assert.Equal(EnvironmentColor.Teal, SentWith(window).GetVisualDescendants().OfType<EnvironmentColorDot>().Single().Color);

            // Switching environments afterwards doesn't relabel a response already received.
            Workspace.ActiveEnvironment = null;
            UiTestHelpers.Pump();
            Assert.Equal(env.Name, tab.Response.SentWithEnvironmentName);
            Assert.Equal(EnvironmentColor.Teal, tab.Response.SentWithEnvironmentColor);
        }
        finally
        {
            Workspace.ActiveEnvironment = null;
            Workspace.DeleteEnvironment(env);
            window.Close();
        }
    }

    private static EnvironmentModel CreateEnvironment(EnvironmentColor color, bool confirmBeforeSend)
    {
        var env = Workspace.CreateEnvironment("Dot " + Guid.NewGuid());
        env.Color = color;
        env.ConfirmBeforeSend = confirmBeforeSend;
        Workspace.SaveEnvironment(env);
        return env;
    }

    /// <summary>The dot in the same row (or column header) as the text <paramref name="name"/>.</summary>
    private static EnvironmentColorDot DotFor(Visual container, string name)
    {
        var label = container.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name);
        var row = label.GetVisualAncestors().First(a => a.GetVisualDescendants().OfType<EnvironmentColorDot>().Any());
        return row.GetVisualDescendants().OfType<EnvironmentColorDot>().Single();
    }

    private static Control SentWith(Window window) =>
        window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SentWithEnvironment");
}
