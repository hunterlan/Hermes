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
        var collection = await CreateCollectionWithVariableAsync("collectionVar");
        var stagingEnvironment = await CreateEnvironmentAsync("stagingVar");
        var productionEnvironment = await CreateEnvironmentAsync("productionVar");
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
            await CleanupAsync(collection, stagingEnvironment, productionEnvironment);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Folder_auth_field_sees_collection_and_environment_variables()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await CreateCollectionWithVariableAsync("collectionVar");
        var folder = new FolderModel { Name = "Users" };
        await Workspace.AddNodeAsync(collection, null, folder);
        var stagingEnvironment = await CreateEnvironmentAsync("stagingVar");
        var productionEnvironment = await CreateEnvironmentAsync("productionVar");
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
            await CleanupAsync(collection, stagingEnvironment, productionEnvironment);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Request_editors_get_the_request_scope()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var environment = await CreateEnvironmentAsync("requestVar");
        try
        {
            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == environment);
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            tab.UrlEditor.Params.Load([new KeyValueItem("page", "1")]);
            tab.HeadersEditor.Headers.Load([new KeyValueItem("Accept", "application/json")]);
            SelectBearer(tab.Auth);
            tab.Sections.AuthExpanded = true;
            UiTestHelpers.Pump();

            var boxes = VisibleBoxes(window).ToList();
            Assert.Contains(boxes, box => box.PlaceholderText == "Enter URL or paste cURL");
            Assert.Contains(boxes, box => box.PlaceholderText == "Token");
            // Params and headers each show their rows' values, with a trailing blank row.
            Assert.True(boxes.Count(box => box.PlaceholderText == "Value") >= 4);
            Assert.All(boxes, box => Assert.Same(tab.Preview.Variables, box.Variables));
            Assert.True(tab.Preview.Variables.TryGet("requestVar", out _));
        }
        finally
        {
            await CleanupAsync(null, environment);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Request_editors_follow_the_active_environment()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var environment = await CreateEnvironmentAsync("requestVar");
        try
        {
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            UiTestHelpers.Pump();
            var urlBox = VisibleBoxes(window).Single(box => box.PlaceholderText == "Enter URL or paste cURL");
            Assert.False(urlBox.Variables!.TryGet("requestVar", out _));

            vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == environment);
            UiTestHelpers.Pump();

            Assert.Same(tab.Preview.Variables, urlBox.Variables);
            Assert.True(urlBox.Variables!.TryGet("requestVar", out _));
        }
        finally
        {
            await CleanupAsync(null, environment);
            window.Close();
        }
    }

    private static IEnumerable<VariableTextBox> VisibleBoxes(Window window) =>
        window.GetVisualDescendants().OfType<VariableTextBox>().Where(box => box.IsEffectivelyVisible);

    private static void SelectBearer(AuthEditorViewModel auth) =>
        auth.SelectedType = auth.Types.Single(type => type.Value == AuthType.Bearer);

    private static async Task<CollectionModel> CreateCollectionWithVariableAsync(string key)
    {
        var collection = await Workspace.CreateCollectionAsync("Inherited " + Guid.NewGuid());
        collection.Variables.Add(new Variable(key, "from collection"));
        await Workspace.SaveCollectionAsync(collection);
        return collection;
    }

    private static async Task<EnvironmentModel> CreateEnvironmentAsync(string variableKey)
    {
        var environment = await Workspace.CreateEnvironmentAsync("Inherited " + Guid.NewGuid());
        environment.Variables.Add(new Variable(variableKey, "from environment"));
        await Workspace.SaveEnvironmentAsync(environment);
        return environment;
    }

    /// <summary>An environment left active would change how later tests resolve variables.</summary>
    private static async Task CleanupAsync(CollectionModel? collection, params EnvironmentModel[] environments)
    {
        Workspace.ActiveEnvironment = null;
        foreach (var environment in environments)
            await Workspace.DeleteEnvironmentAsync(environment);
        if (collection is not null)
            await Workspace.DeleteCollectionAsync(collection);
    }
}
