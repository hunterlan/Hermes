using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.ViewModels.Sidebar;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>
/// Workspace state is shared across tests in the session (one Application per run),
/// so each test works with uniquely named entities.
/// </summary>
public class SidebarTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

    private static async Task<CollectionNodeViewModel> CreateCollectionAsync(MainWindowViewModel vm, string name)
    {
        var collection = await Workspace.CreateCollectionAsync(name);
        return Assert.IsType<CollectionNodeViewModel>(vm.Collections.Find(collection.Id));
    }

    [AvaloniaFact]
    public async Task Add_request_to_collection_opens_it_and_saves_changes()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await CreateCollectionAsync(vm, "Save test " + Guid.NewGuid());

        await vm.Collections.AddFolderCommand.ExecuteAsync(collection);
        var folder = Assert.IsType<FolderNodeViewModel>(vm.Collections.Find(collection.Id)!.Children.Single());
        await vm.Collections.CommitRenameAsync(folder, "Users");

        await vm.Collections.AddRequestCommand.ExecuteAsync(vm.Collections.Find(folder.Id));
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.True(tab.IsPersisted);
        Assert.False(tab.IsDirty);
        Assert.Equal($"{collection.Model.Name} / Users", tab.Preview.Breadcrumb);

        tab.UrlEditor.Url = "https://api.example.com/users";
        tab.SelectedMethod = "POST";
        Assert.True(tab.IsDirty);
        Assert.True(await tab.SaveAsync());
        Assert.False(tab.IsDirty);

        var saved = Assert.IsType<HttpRequestModel>(Workspace.Find(tab.RequestId)!.Node);
        Assert.Equal("POST", saved.Method);
        Assert.Equal("https://api.example.com/users", saved.Url);

        vm.SelectedPane = SidebarPane.Collections;
        UiTestHelpers.Screenshot(window, "10-collections");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Activating_a_tab_reveals_its_request_in_the_tree()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await Workspace.CreateCollectionAsync("Reveal test " + Guid.NewGuid());
        var folder = new FolderModel { Name = "Nested" };
        var request = new HttpRequestModel { Name = "Deep" };
        folder.Items.Add(request);
        await Workspace.AddNodeAsync(collection, null, folder);

        await vm.OpenRequestAsync(request);

        Assert.Equal(request.Id, vm.Collections.SelectedNode?.Id);
        Assert.True(vm.Collections.Find(folder.Id)!.IsExpanded);
        Assert.True(vm.Collections.Find(collection.Id)!.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Renaming_request_in_sidebar_updates_clean_tab()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await CreateCollectionAsync(vm, "Rename test " + Guid.NewGuid());
        await vm.Collections.AddRequestCommand.ExecuteAsync(collection);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        var node = vm.Collections.Find(tab.RequestId)!;
        await vm.Collections.CommitRenameAsync(node, "Renamed");

        Assert.Equal("Renamed", tab.Name);
        Assert.False(tab.IsDirty);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Deleting_a_collection_closes_its_tabs()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await CreateCollectionAsync(vm, "Delete test " + Guid.NewGuid());
        await vm.Collections.AddRequestCommand.ExecuteAsync(collection);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        // Same steps as the Delete command, minus the confirmation dialog. The tree was rebuilt, so look the node up again.
        var ids = vm.Collections.Find(collection.Id)!.SelfAndDescendants().Select(n => n.Id).ToList();
        await Workspace.DeleteCollectionAsync(collection.Model);
        vm.CloseTabsFor(ids);

        Assert.DoesNotContain(tab, vm.Tabs);
        Assert.Null(vm.Collections.Find(collection.Id));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Drag_and_drop_moves_request_into_folder()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await CreateCollectionAsync(vm, "Move test " + Guid.NewGuid());
        await vm.Collections.AddFolderCommand.ExecuteAsync(collection);
        await vm.Collections.AddRequestCommand.ExecuteAsync(vm.Collections.Find(collection.Id));

        var root = vm.Collections.Find(collection.Id)!;
        var folder = root.Children.OfType<FolderNodeViewModel>().Single();
        var request = root.Children.OfType<RequestNodeViewModel>().Single();

        Assert.False(vm.Collections.CanDrop(folder, folder, DropPosition.Inside));
        Assert.True(vm.Collections.CanDrop(request, folder, DropPosition.Inside));
        await vm.Collections.DropAsync(request, folder, DropPosition.Inside);

        var model = Workspace.FindCollection(collection.Id)!;
        Assert.Single(model.Items);
        Assert.Single(((FolderModel)model.Items[0]).Items);
        Assert.Equal(folder.Id, Workspace.Find(request.Id)!.Parent!.Id);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Environment_variables_resolve_and_history_is_recorded()
    {
        using var server = new JsonServer();
        var (window, vm) = UiTestHelpers.ShowMainWindow();

        var env = await Workspace.CreateEnvironmentAsync("Env " + Guid.NewGuid());
        env.Variables.Add(new Variable("baseUrl", server.Url.TrimEnd('/')));
        await Workspace.SaveEnvironmentAsync(env);
        vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == env);
        Assert.Same(env, Workspace.ActiveEnvironment);
        Assert.Contains(vm.QuickLookEnvironment.Rows, r => r.Key == "baseUrl");

        vm.NewRequestCommand.Execute(null);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.UrlEditor.Url = "{{baseUrl}}/env-test";
        Assert.True(tab.Preview.Variables.TryGet("baseUrl", out _));
        Assert.Empty(tab.Sending.FindUnresolvedVariables());

        var historyBefore = Workspace.History.Count;
        await tab.Sending.SendCommand.ExecuteAsync(null);
        Assert.True(tab.Response.HasResponse, tab.Response.Failure?.Model.Facts);
        Assert.Contains("/env-test", tab.Response.RawText);
        Assert.Equal(historyBefore + 1, Workspace.History.Count);
        Assert.Equal("{{baseUrl}}/env-test", Workspace.History[0].Request.Url);

        await vm.OpenEnvironmentAsync(env);
        Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
        vm.SelectedPane = SidebarPane.Environments;
        UiTestHelpers.Screenshot(window, "11-environment");

        vm.SelectedPane = SidebarPane.History;
        UiTestHelpers.Screenshot(window, "12-history");

        vm.SelectedEnvironment = vm.EnvironmentOptions[0];
        Assert.Null(Workspace.ActiveEnvironment);
        Assert.Equal(["baseUrl"], tab.Sending.FindUnresolvedVariables());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Collection_tab_edits_auth_and_variables_used_by_requests()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = await CreateCollectionAsync(vm, "Auth test " + Guid.NewGuid());
        await vm.OpenCollectionAsync(collection.Model);
        var collectionTab = Assert.IsType<CollectionTabViewModel>(vm.ActiveTab);

        collectionTab.Auth.SelectedType = collectionTab.Auth.Types.Single(t => t.Value == AuthType.Bearer);
        collectionTab.Auth.BearerToken = "{{token}}";
        var row = collectionTab.Variables.Rows.Last();
        row.Key = "token";
        row.InitialValue = "secret-123";
        Assert.True(collectionTab.IsDirty);
        await collectionTab.SaveAsync();
        Assert.False(collectionTab.IsDirty);

        await vm.Collections.AddRequestCommand.ExecuteAsync(vm.Collections.Find(collection.Id));
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.Contains(tab.HeadersEditor.AutomaticRows, h => h.Key == "Authorization" && h.Value == "Bearer secret-123");
        Assert.Contains("Bearer Token", tab.Auth.InheritedDescription);

        Assert.Equal(["Collection · " + collection.Model.Name, "Request · " + tab.Name], tab.Auth.Chain.Select(n => n.Title));
        Assert.True(tab.Auth.Chain[0].IsSource);
        Assert.Equal("Inherits", tab.Auth.Chain[1].Status);

        await vm.OpenCollectionAsync(collection.Model);
        collectionTab.SelectedSection = 1;
        Assert.Equal("Used by 1 request.", collectionTab.Auth.UsageSummary);
        UiTestHelpers.Screenshot(window, "13-collection-settings");
        window.Close();
    }
}
