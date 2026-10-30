using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Controls;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>The variables a tab shows reach every <see cref="VariableTextBox"/> inside it, wherever it sits.</summary>
public class InheritedVariablesTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    [AvaloniaFact]
    public void Request_editors_get_the_request_scope()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var environment = CreateEnvironment("requestVar");
        try
        {
            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == environment);
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            tab.Params.Load([new KeyValueItem("page", "1")]);
            tab.Headers.Load([new KeyValueItem("Accept", "application/json")]);
            SelectBearer(tab.Auth);
            tab.AuthExpanded = true;
            UiTestHelpers.Pump();

            var boxes = VisibleBoxes(window).ToList();
            Assert.Contains(boxes, box => box.PlaceholderText == "Enter URL or paste cURL");
            Assert.Contains(boxes, box => box.PlaceholderText == "Token");
            // Params and headers each show their rows' values, with a trailing blank row.
            Assert.True(boxes.Count(box => box.PlaceholderText == "Value") >= 4);
            Assert.All(boxes, box => Assert.Same(tab.Variables, box.Variables));
            Assert.True(tab.Variables.TryGet("requestVar", out _));
        }
        finally
        {
            Cleanup(environment);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Request_editors_follow_the_active_environment()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var environment = CreateEnvironment("requestVar");
        try
        {
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();
            var urlBox = VisibleBoxes(window).Single(box => box.PlaceholderText == "Enter URL or paste cURL");
            Assert.False(urlBox.Variables!.TryGet("requestVar", out _));

            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == environment);
            UiTestHelpers.Pump();

            Assert.Same(tab.Variables, urlBox.Variables);
            Assert.True(urlBox.Variables!.TryGet("requestVar", out _));
        }
        finally
        {
            Cleanup(environment);
            window.Close();
        }
    }

    private static IEnumerable<VariableTextBox> VisibleBoxes(Window window) =>
        window.GetVisualDescendants().OfType<VariableTextBox>().Where(box => box.IsEffectivelyVisible);

    private static void SelectBearer(AuthEditorViewModel auth) =>
        auth.SelectedType = auth.Types.Single(type => type.Value == AuthType.Bearer);

    private static EnvironmentModel CreateEnvironment(string variableKey)
    {
        var environment = Workspace.CreateEnvironment("Inherited " + Guid.NewGuid());
        environment.Variables.Add(new Variable(variableKey, "from environment"));
        Workspace.SaveEnvironment(environment);
        return environment;
    }

    /// <summary>An environment left active would change how later tests resolve variables.</summary>
    private static void Cleanup(EnvironmentModel environment)
    {
        Workspace.ActiveEnvironment = null;
        Workspace.DeleteEnvironment(environment);
    }
}
