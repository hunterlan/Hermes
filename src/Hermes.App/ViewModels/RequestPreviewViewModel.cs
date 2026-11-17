using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Views.Dialogs;
using Hermes.Core.CodeGen;
using Hermes.Core.Requests;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

/// <summary>
/// How a request tab's request reads where it is now: where it lives (the breadcrumb), the variables in scope, the URL
/// it sends to with the active environment, the headers it will send and its cURL snippet. The tab says when to
/// refresh them.
/// </summary>
public sealed partial class RequestPreviewViewModel(
    IRequestSource request,
    TabServices services,
    RequestUrlEditorViewModel urlEditor,
    RequestHeadersViewModel headersEditor) : ObservableObject
{
    /// <summary>The variables in scope where the request is; the editors highlight and suggest them.</summary>
    [ObservableProperty] public partial VariableScope Variables { get; set; } = VariableScope.Empty;

    /// <summary>The collection and folders the request is in; empty while it isn't saved.</summary>
    [ObservableProperty] public partial string Breadcrumb { get; set; } = "";

    /// <summary>The URL after variable resolution, shown under the URL bar as "Sends to".</summary>
    [ObservableProperty] public partial string ResolvedUrl { get; set; } = "";

    public string? ActiveEnvironmentName => services.Workspace.ActiveEnvironment?.Name;

    /// <summary>
    /// Updates the resolved URL and the Headers section from one preview of the request, so the preview is computed
    /// once per change.
    /// </summary>
    public void Refresh()
    {
        try
        {
            var model = request.BuildModel();
            var context = request.CreateContext();
            var prepared = RequestPreparer.Preview(model, context);
            ResolvedUrl = prepared.Url;
            headersEditor.Refresh(model, context, prepared);
        }
        catch (Exception)
        {
            headersEditor.Clear();
            ResolvedUrl = urlEditor.Url;
        }
        OnPropertyChanged(nameof(ActiveEnvironmentName));
    }

    /// <summary>Where the request lives may have changed (it was saved or moved): the breadcrumb and the variables follow.</summary>
    public void RefreshLocation()
    {
        RefreshBreadcrumb();
        RefreshVariables();
    }

    public void RefreshVariables() => Variables = request.CreateContext().BuildScope();

    private void RefreshBreadcrumb()
    {
        if (services.Workspace.Find(request.RequestId) is { } location)
        {
            Breadcrumb = string.Join(" / ", new[] { location.Collection.Name }.Concat(location.Ancestors.Select(a => a.Name)));
        }
        else
        {
            Breadcrumb = "";
        }
    }

    [RelayCommand]
    private async Task ShowCode()
    {
        var prepared = RequestPreparer.Prepare(request.BuildModel(), request.CreateContext(), includeDefaultHeaders: false);
        var dialog = new CodeSnippetDialog { DataContext = new CodeSnippetViewModel(CurlGenerator.Generate(prepared), services.Dialogs) };
        await services.Dialogs.ShowDialogAsync<object>(dialog);
    }
}
