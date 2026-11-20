using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.Core.Serialization;

namespace Hermes.App.Tests;

/// <summary>How a request reads where it is: the breadcrumb, the variables in scope, "Sends to" and the cURL snippet.</summary>
public class RequestPreviewViewModelTests
{
    /// <summary>A request the test can edit, move out of the workspace, or make impossible to build.</summary>
    private sealed class EditableRequest(WorkspaceService workspace, HttpRequestModel model) : IRequestSource
    {
        public HttpRequestModel Model { get; } = model;
        public Guid RequestId { get; set; } = model.Id;
        public bool FailsToBuild { get; set; }
        public bool IsPersisted => workspace.Find(RequestId) is not null;

        public HttpRequestModel BuildModel()
        {
            if (FailsToBuild)
                throw new InvalidOperationException("The request can't be built.");
            var copy = ModelCloner.Clone(Model);
            copy.Id = RequestId;
            return copy;
        }

        public RequestContext CreateContext() => workspace.CreateContext(RequestId);
    }

    private sealed record PreviewFixture(EditableRequest Request, RequestUrlEditorViewModel UrlEditor,
        RequestHeadersViewModel HeadersEditor, RequestPreviewViewModel Preview);

    /// <summary>A request at Pets / Dogs, in a collection whose baseUrl is https://api.example.com.</summary>
    private static async Task<PreviewFixture> OpenAsync(TempWorkspace temp, string url, DialogService? dialogs = null)
    {
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        collection.Variables.Add(new Variable { Key = "baseUrl", Value = "https://api.example.com" });
        var folder = new FolderModel { Name = "Dogs" };
        await temp.Workspace.AddNodeAsync(collection, null, folder);
        var model = new HttpRequestModel { Name = "List", Url = url, Headers = [new KeyValueItem("X-Trace", "1")] };
        await temp.Workspace.AddNodeAsync(collection, folder, model);

        var request = new EditableRequest(temp.Workspace, model);
        var urlEditor = new RequestUrlEditorViewModel();
        urlEditor.Load(url, [], []);
        var headersEditor = new RequestHeadersViewModel(request.BuildModel, request.CreateContext, _ => Task.CompletedTask, _ => Task.CompletedTask);
        headersEditor.Load(model.Headers, []);
        var services = RequestTabTestServices.Create(temp, dialogs);
        return new PreviewFixture(request, urlEditor, headersEditor, new RequestPreviewViewModel(request, services, urlEditor, headersEditor));
    }

    [AvaloniaFact]
    public async Task Refresh_resolves_the_url_with_the_variables_where_the_request_is()
    {
        using var temp = new TempWorkspace();
        var (_, _, headersEditor, preview) = await OpenAsync(temp, "{{baseUrl}}/pets");
        var environment = await temp.Workspace.CreateEnvironmentAsync("Staging");
        temp.Workspace.ActiveEnvironment = environment;
        var announced = new List<string?>();
        preview.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        preview.Refresh();

        Assert.Equal("https://api.example.com/pets", preview.ResolvedUrl);
        Assert.Equal("Staging", preview.ActiveEnvironmentName);
        Assert.Contains(nameof(RequestPreviewViewModel.ActiveEnvironmentName), announced);
        Assert.True(headersEditor.HasAutomaticRows);
    }

    [AvaloniaFact]
    public async Task A_request_that_cannot_be_previewed_shows_the_url_as_typed_and_no_header_preview()
    {
        using var temp = new TempWorkspace();
        var (request, _, headersEditor, preview) = await OpenAsync(temp, "{{baseUrl}}/pets");
        preview.Refresh();
        Assert.True(headersEditor.HasAutomaticRows);

        request.FailsToBuild = true;
        preview.Refresh();

        Assert.Equal("{{baseUrl}}/pets", preview.ResolvedUrl);
        Assert.False(headersEditor.HasAutomaticRows);
    }

    [AvaloniaFact]
    public async Task Refreshing_the_location_follows_where_the_request_lives()
    {
        using var temp = new TempWorkspace();
        var (request, _, _, preview) = await OpenAsync(temp, "{{baseUrl}}/pets");

        preview.RefreshLocation();

        Assert.Equal("Pets / Dogs", preview.Breadcrumb);
        Assert.True(preview.Variables.TryGet("baseUrl", out _));

        // Not in the workspace (yet), as a new request is before it is saved.
        request.RequestId = Guid.NewGuid();
        preview.RefreshLocation();

        Assert.Equal("", preview.Breadcrumb);
        Assert.False(preview.Variables.TryGet("baseUrl", out _));
    }

    [AvaloniaFact]
    public async Task Show_code_opens_the_curl_snippet_of_the_resolved_request()
    {
        using var temp = new TempWorkspace();
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var (_, _, _, preview) = await OpenAsync(temp, "{{baseUrl}}/pets", new DialogService { Owner = window });

        var showing = preview.ShowCodeCommand.ExecuteAsync(null);
        await UiTestHelpers.WaitUntil(() => window.OwnedWindows.OfType<CodeSnippetDialog>().Any());
        var dialog = window.OwnedWindows.OfType<CodeSnippetDialog>().Single();
        var snippet = Assert.IsType<CodeSnippetViewModel>(dialog.DataContext);

        Assert.StartsWith("curl", snippet.Code, StringComparison.Ordinal);
        Assert.Contains("https://api.example.com/pets", snippet.Code, StringComparison.Ordinal);
        Assert.Contains("X-Trace", snippet.Code, StringComparison.Ordinal);
        dialog.Close();
        await UiTestHelpers.WaitUntil(() => showing.IsCompleted);
        window.Close();
    }
}
