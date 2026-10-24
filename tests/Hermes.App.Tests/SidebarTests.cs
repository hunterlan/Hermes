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

    private static CollectionNodeViewModel CreateCollection(MainWindowViewModel vm, string name)
    {
        var collection = Workspace.CreateCollection(name);
        return Assert.IsType<CollectionNodeViewModel>(vm.Collections.Find(collection.Id));
    }

    [AvaloniaFact]
    public void Add_request_to_collection_opens_it_and_saves_changes()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollection(vm, "Save test " + Guid.NewGuid());

        vm.Collections.AddFolderCommand.Execute(collection);
        var folder = Assert.IsType<FolderNodeViewModel>(vm.Collections.Find(collection.Id)!.Children.Single());
        vm.Collections.CommitRename(folder, "Users");

        vm.Collections.AddRequestCommand.Execute(vm.Collections.Find(folder.Id));
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.True(tab.IsPersisted);
        Assert.False(tab.IsDirty);
        Assert.Equal($"{collection.Model.Name} / Users", tab.Breadcrumb);

        tab.Url = "https://api.example.com/users";
        tab.SelectedMethod = "POST";
        Assert.True(tab.IsDirty);
        Assert.True(tab.SaveAsync().Result);
        Assert.False(tab.IsDirty);

        var saved = Assert.IsType<HttpRequestModel>(Workspace.Find(tab.RequestId)!.Node);
        Assert.Equal("POST", saved.Method);
        Assert.Equal("https://api.example.com/users", saved.Url);

        vm.SelectedPane = SidebarPane.Collections;
        UiTestHelpers.Screenshot(window, "10-collections");
        window.Close();
    }

    [AvaloniaFact]
    public void Activating_a_tab_reveals_its_request_in_the_tree()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = Workspace.CreateCollection("Reveal test " + Guid.NewGuid());
        var folder = new FolderModel { Name = "Nested" };
        var request = new HttpRequestModel { Name = "Deep" };
        folder.Items.Add(request);
        Workspace.AddNode(collection, null, folder);

        vm.OpenRequest(request);

        Assert.Equal(request.Id, vm.Collections.SelectedNode?.Id);
        Assert.True(vm.Collections.Find(folder.Id)!.IsExpanded);
        Assert.True(vm.Collections.Find(collection.Id)!.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void Renaming_request_in_sidebar_updates_clean_tab()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollection(vm, "Rename test " + Guid.NewGuid());
        vm.Collections.AddRequestCommand.Execute(collection);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        var node = vm.Collections.Find(tab.RequestId)!;
        vm.Collections.CommitRename(node, "Renamed");

        Assert.Equal("Renamed", tab.Name);
        Assert.False(tab.IsDirty);
        window.Close();
    }

    [AvaloniaFact]
    public void Deleting_a_collection_closes_its_tabs()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollection(vm, "Delete test " + Guid.NewGuid());
        vm.Collections.AddRequestCommand.Execute(collection);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);

        // Same steps as the Delete command, minus the confirmation dialog. The tree was rebuilt, so look the node up again.
        var ids = vm.Collections.Find(collection.Id)!.SelfAndDescendants().Select(n => n.Id).ToList();
        Workspace.DeleteCollection(collection.Model);
        vm.CloseTabsFor(ids);

        Assert.DoesNotContain(tab, vm.Tabs);
        Assert.Null(vm.Collections.Find(collection.Id));
        window.Close();
    }

    [AvaloniaFact]
    public void Drag_and_drop_moves_request_into_folder()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollection(vm, "Move test " + Guid.NewGuid());
        vm.Collections.AddFolderCommand.Execute(collection);
        vm.Collections.AddRequestCommand.Execute(vm.Collections.Find(collection.Id));

        var root = vm.Collections.Find(collection.Id)!;
        var folder = root.Children.OfType<FolderNodeViewModel>().Single();
        var request = root.Children.OfType<RequestNodeViewModel>().Single();

        Assert.False(vm.Collections.CanDrop(folder, folder, DropPosition.Inside));
        Assert.True(vm.Collections.CanDrop(request, folder, DropPosition.Inside));
        vm.Collections.Drop(request, folder, DropPosition.Inside);

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

        var env = Workspace.CreateEnvironment("Env " + Guid.NewGuid());
        env.Variables.Add(new Variable("baseUrl", server.Url.TrimEnd('/')));
        Workspace.SaveEnvironment(env);
        vm.SelectedEnvironment = vm.EnvironmentOptions.Single(o => o.Model == env);
        Assert.Same(env, Workspace.ActiveEnvironment);
        Assert.Contains(vm.QuickLookEnvironment.Rows, r => r.Key == "baseUrl");

        vm.NewRequestCommand.Execute(null);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = "{{baseUrl}}/env-test";
        Assert.True(tab.Variables.TryGet("baseUrl", out _));
        Assert.Empty(tab.FindUnresolvedVariables());

        var historyBefore = Workspace.History.Count;
        await tab.SendCommand.ExecuteAsync(null);
        Assert.True(tab.Response.HasResponse, tab.Response.Error);
        Assert.Contains("/env-test", tab.Response.RawText);
        Assert.Equal(historyBefore + 1, Workspace.History.Count);
        Assert.Equal("{{baseUrl}}/env-test", Workspace.History[0].Request.Url);

        vm.OpenEnvironment(env);
        Assert.IsType<EnvironmentTabViewModel>(vm.ActiveTab);
        vm.SelectedPane = SidebarPane.Environments;
        UiTestHelpers.Screenshot(window, "11-environment");

        vm.SelectedPane = SidebarPane.History;
        UiTestHelpers.Screenshot(window, "12-history");

        vm.SelectedEnvironment = vm.EnvironmentOptions[0];
        Assert.Null(Workspace.ActiveEnvironment);
        Assert.Equal(["baseUrl"], tab.FindUnresolvedVariables());
        window.Close();
    }

    [AvaloniaFact]
    public void Collection_tab_edits_auth_and_variables_used_by_requests()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = CreateCollection(vm, "Auth test " + Guid.NewGuid());
        vm.OpenCollection(collection.Model);
        var collectionTab = Assert.IsType<CollectionTabViewModel>(vm.ActiveTab);

        collectionTab.Auth.SelectedType = collectionTab.Auth.Types.Single(t => t.Value == AuthType.Bearer);
        collectionTab.Auth.BearerToken = "{{token}}";
        var row = collectionTab.Variables.Rows.Last();
        row.Key = "token";
        row.InitialValue = "secret-123";
        Assert.True(collectionTab.IsDirty);
        collectionTab.SaveAsync().Wait();
        Assert.False(collectionTab.IsDirty);

        vm.Collections.AddRequestCommand.Execute(vm.Collections.Find(collection.Id));
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.Contains(tab.HiddenHeaders, h => h.Key == "Authorization" && h.Value == "Bearer secret-123");
        Assert.Contains("Bearer Token", tab.Auth.InheritedDescription);

        Assert.Equal(["Collection", "Request"], tab.Auth.Chain.Select(n => n.Kind));
        Assert.True(tab.Auth.Chain[0].IsSource);
        Assert.Equal("Inherits", tab.Auth.Chain[1].Status);

        vm.OpenCollection(collection.Model);
        collectionTab.SelectedSection = 1;
        Assert.Equal("Used by 1 request.", collectionTab.Auth.UsageSummary);
        UiTestHelpers.Screenshot(window, "13-collection-settings");
        window.Close();
    }
}
