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
    public async Task Collection_auth_field_sees_collection_and_environment_variables()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollectionWithVariable("collectionVar");
        var stagingEnvironment = CreateEnvironment("stagingVar");
        var productionEnvironment = CreateEnvironment("productionVar");
        try
        {
            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == stagingEnvironment);
            await vm.OpenCollectionAsync(collection);
            var tab = Assert.IsType<CollectionTabViewModel>(vm.ActiveTab);
            tab.SelectedSection = 1;
            SelectBearer(tab.Auth);
            UiTestHelpers.Pump();

            var tokenBox = VisibleBoxes(window).Single(box => box.PlaceholderText == "Token");
            Assert.True(tokenBox.Variables!.TryGet("collectionVar", out _));
            Assert.True(tokenBox.Variables.TryGet("stagingVar", out _));
            Assert.False(tokenBox.Variables.TryGet("productionVar", out _));

            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == productionEnvironment);
            UiTestHelpers.Pump();

            Assert.True(tokenBox.Variables!.TryGet("collectionVar", out _));
            Assert.True(tokenBox.Variables.TryGet("productionVar", out _));
            Assert.False(tokenBox.Variables.TryGet("stagingVar", out _));
        }
        finally
        {
            Cleanup(collection, stagingEnvironment, productionEnvironment);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Folder_auth_field_sees_collection_and_environment_variables()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollectionWithVariable("collectionVar");
        var folder = new FolderModel { Name = "Users" };
        Workspace.AddNode(collection, null, folder);
        var stagingEnvironment = CreateEnvironment("stagingVar");
        var productionEnvironment = CreateEnvironment("productionVar");
        try
        {
            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == stagingEnvironment);
            await vm.OpenFolderAsync(folder);
            var tab = Assert.IsType<FolderTabViewModel>(vm.ActiveTab);
            tab.SelectedSection = 1;
            SelectBearer(tab.Auth);
            UiTestHelpers.Pump();

            var tokenBox = VisibleBoxes(window).Single(box => box.PlaceholderText == "Token");
            Assert.True(tokenBox.Variables!.TryGet("collectionVar", out _));
            Assert.True(tokenBox.Variables.TryGet("stagingVar", out _));

            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == productionEnvironment);
            UiTestHelpers.Pump();

            Assert.True(tokenBox.Variables!.TryGet("collectionVar", out _));
            Assert.True(tokenBox.Variables.TryGet("productionVar", out _));
            Assert.False(tokenBox.Variables.TryGet("stagingVar", out _));
        }
        finally
        {
            Cleanup(collection, stagingEnvironment, productionEnvironment);
            window.Close();
        }
    }

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
            Cleanup(null, environment);
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
            Cleanup(null, environment);
            window.Close();
        }
    }

    private static IEnumerable<VariableTextBox> VisibleBoxes(Window window) =>
        window.GetVisualDescendants().OfType<VariableTextBox>().Where(box => box.IsEffectivelyVisible);

    private static void SelectBearer(AuthEditorViewModel auth) =>
        auth.SelectedType = auth.Types.Single(type => type.Value == AuthType.Bearer);

    private static CollectionModel CreateCollectionWithVariable(string key)
    {
        var collection = Workspace.CreateCollection("Inherited " + Guid.NewGuid());
        collection.Variables.Add(new Variable(key, "from collection"));
        Workspace.SaveCollection(collection);
        return collection;
    }

    private static EnvironmentModel CreateEnvironment(string variableKey)
    {
        var environment = Workspace.CreateEnvironment("Inherited " + Guid.NewGuid());
        environment.Variables.Add(new Variable(variableKey, "from environment"));
        Workspace.SaveEnvironment(environment);
        return environment;
    }

    /// <summary>An environment left active would change how later tests resolve variables.</summary>
    private static void Cleanup(CollectionModel? collection, params EnvironmentModel[] environments)
    {
        Workspace.ActiveEnvironment = null;
        foreach (var environment in environments)
            Workspace.DeleteEnvironment(environment);
        if (collection is not null)
            Workspace.DeleteCollection(collection);
    }
}
