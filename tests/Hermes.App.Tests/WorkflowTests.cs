using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Http;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class WorkflowTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();
    private static TabServices TabServices => App.Services.GetRequiredService<TabServices>();

    private const string PostmanCollection = """
        {
          "info": { "name": "Imported API", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "variable": [{ "key": "base", "value": "https://api.example.com" }],
          "item": [
            { "name": "Users", "item": [
              { "name": "List users", "request": { "method": "GET", "url": "{{base}}/users?page=1" } },
              { "name": "Create user", "request": { "method": "POST", "url": "{{base}}/users",
                "body": { "mode": "raw", "raw": "{\"name\":\"Ada\"}", "options": { "raw": { "language": "json" } } } } }
            ] }
          ]
        }
        """;

    [AvaloniaFact]
    public async Task Import_adds_collection_and_opens_curl_requests()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var result = new ImportResult();
        ImportService.ImportText(PostmanCollection, result);
        ImportService.ImportText("curl https://api.example.com/health -H 'X-Trace: 1'", result);

        await vm.ApplyImportAsync(result);

        var collection = Assert.Single(Workspace.Collections, c => c.Id == result.Collections[0].Id);
        Assert.Equal("Imported API", collection.Name);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.Equal("https://api.example.com/health", tab.Url);
        Assert.True(tab.IsDirty);

        // Open the imported POST request: body and collection variables come along.
        var create = (HttpRequestModel)((FolderModel)collection.Items[0]).Items[1];
        vm.OpenRequest(create);
        var createTab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.True(createTab.IsBodyRaw);
        Assert.Equal("{\"name\":\"Ada\"}", createTab.RawBody);
        Assert.True(createTab.Variables.TryGet("base", out var baseUrl));
        Assert.Equal("https://api.example.com", baseUrl);
        Assert.True(createTab.IsBodySectionVisible);
        UiTestHelpers.Screenshot(window, "20-imported-body");
        window.Close();
    }

    [AvaloniaFact]
    public void Session_restores_tabs_and_unsaved_drafts()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        var collection = Workspace.CreateCollection("Session " + Guid.NewGuid());
        var request = new HttpRequestModel { Name = "Persisted", Url = "https://example.com/a" };
        Workspace.AddNode(collection, null, request);

        vm.OpenRequest(request);
        var persistedTab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        persistedTab.Url = "https://example.com/changed";
        vm.NewRequestCommand.Execute(null);
        var draftTab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        draftTab.Url = "https://example.com/draft";
        vm.OpenCollection(collection);

        vm.SaveSession();
        window.Close();

        // A fresh view model (as on next launch) restores from session.json.
        var restored = new MainWindowViewModel(TabServices);
        var restoredPersisted = restored.Tabs.OfType<RequestTabViewModel>().Single(t => t.RequestId == request.Id);
        Assert.Equal("https://example.com/changed", restoredPersisted.Url);
        Assert.True(restoredPersisted.IsDirty);
        Assert.True(restoredPersisted.IsPersisted);

        var restoredDraft = restored.Tabs.OfType<RequestTabViewModel>().Single(t => t.RequestId == draftTab.RequestId);
        Assert.Equal("https://example.com/draft", restoredDraft.Url);
        Assert.False(restoredDraft.IsPersisted);

        Assert.Contains(restored.Tabs, t => t is CollectionTabViewModel && t.EntityId == collection.Id);
        Assert.IsType<CollectionTabViewModel>(restored.ActiveTab);
    }

    [AvaloniaFact]
    public void Settings_change_layout_and_defaults()
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        var original = settings.Current;
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            var dialog = new SettingsDialogViewModel(settings, App.Services.GetRequiredService<DialogService>(),
                App.Services.GetRequiredService<HttpClientProvider>(), Workspace.DataDirectory, vm.ConfirmTurningTabsOffAsync);
            dialog.SelectedLayout = dialog.Layouts.Single(l => l.Value == ResponseLayout.Horizontal);
            dialog.SelectedTheme = dialog.Themes.Single(t => t.Value == ThemePreference.Light);
            dialog.TimeoutText = "1500";
            dialog.SaveCommand.Execute(null);

            Assert.Equal(ResponseLayout.Horizontal, settings.Current.Layout);
            Assert.Equal(1500, settings.Current.RequestTimeoutMs);

            vm.NewRequestCommand.Execute(null);
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            tab.Url = "https://example.com";
            var prepared = RequestPreparer.Prepare(tab.BuildModel(), Workspace.CreateContext(tab.RequestId));
            Assert.Equal(1500, prepared.TimeoutMs);
            UiTestHelpers.Screenshot(window, "21-light-horizontal");
        }
        finally
        {
            // Update() replaced Current with a new object, so the original is untouched.
            settings.Update(original);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Cookie_manager_lists_and_deletes_cookies()
    {
        using var server = new JsonServer();
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        vm.NewRequestCommand.Execute(null);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = server.Url;
        await tab.SendCommand.ExecuteAsync(null);

        var cookies = new CookieManagerViewModel(App.Services.GetRequiredService<HttpClientProvider>());
        var domain = Assert.Single(cookies.Domains, d => d.Domain == "127.0.0.1");
        var row = Assert.Single(domain.Cookies, c => c.Name == "session");

        cookies.DeleteCommand.Execute(row);
        Assert.DoesNotContain(cookies.Domains, d => d.Domain == "127.0.0.1" && d.Cookies.Any(c => c.Name == "session"));
        window.Close();
    }

    [AvaloniaFact]
    public void Request_preview_leaves_dynamic_variables_as_written()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        try
        {
            vm.NewRequestCommand.Execute(null);
            var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
            tab.Url = "https://example.com/items/{{$guid}}";

            Assert.Equal("https://example.com/items/{{$guid}}", tab.ResolvedUrl);
        }
        finally
        {
            window.Close();
        }
    }
}
