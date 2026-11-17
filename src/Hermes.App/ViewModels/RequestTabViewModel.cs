using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Requests;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels;

public sealed partial class RequestTabViewModel : TabViewModelBase, IRenamable, IRequestSource
{
    private readonly RequestChangeTracker _changes = new();

    // Preserved, not editable in the UI.
    private string? _description;
    private string? _eventsJson;

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public RequestTabViewModel(TabServices services, HttpRequestModel model, string? savedSnapshot = null) : base(services)
    {
        RequestId = model.Id;
        UrlEditor = new RequestUrlEditorViewModel();
        UrlEditor.Changed += AfterEdit;
        UrlEditor.CurlPasted += OnCurlPasted;
        HeadersEditor = new RequestHeadersViewModel(BuildModel, CreateContext,
            id => Services.OpenContainerAsync?.Invoke(id) ?? Task.CompletedTask, services.Dialogs.CopyTextAsync);
        HeadersEditor.Headers.Changed += AfterEdit;
        // A skipped inherited header updates the header rows in place, keeping focus, so only the dirty check runs.
        HeadersEditor.Changed += UpdateDirty;
        Preview = new RequestPreviewViewModel(this, services, UrlEditor, HeadersEditor);
        Body = new RequestBodyViewModel(services.Dialogs);
        Body.Changed += AfterEdit;
        Settings = new RequestSettingsViewModel(services.Settings);
        Settings.Changed += AfterEdit;
        Sections = new ComposerSectionsViewModel(UrlEditor, Body, Settings);

        Auth = services.CreateAuthEditor(allowInherit: true);
        Auth.Changed += AfterEdit;
        Auth.ScopeProvider = () => CreateContext().BuildScope();
        Auth.VerifySslProvider = () => Settings.VerifySsl;
        Auth.SslSourceProvider = () => Settings.VerifySslOverride is null ? SslSettingSource.AppWide : SslSettingSource.Request;
        Auth.InheritedAuthProvider = () => CreateContext().ResolveAuth(AuthConfig.Inherit());
        Auth.ChainProvider = () => (AuthInheritance.OuterLevels(CreateContext()), "Request", Name);

        Response = new ResponseViewModel(services.Dialogs, services.Settings);
        Response.SetSaved(services.Workspace.LoadResponses(model.Id), show: true);
        Sending = new RequestSendViewModel(this, services, Response);

        FailureActions = new RequestFailureActionRouter(UrlEditor, Body, Settings, Sections, Auth, services.Dialogs,
            Sending.SendCommand, OpenAppSettingsCommand);
        Auth.FailureActionRequested += FailureActions.OnFailureActionRequested;
        Response.FailureActionRequested += FailureActions.OnFailureActionRequested;

        Load(model);
        _changes.MarkSaved(savedSnapshot ?? CurrentSnapshot());
        UpdateDirty();
        Preview.RefreshLocation();
    }

    public Guid RequestId { get; private set; }
    public override Guid EntityId => RequestId;
    public override SessionTabKind Kind => SessionTabKind.Request;
    public override string? Method => SelectedMethod;

    public bool IsPersisted => Services.Workspace.Find(RequestId) is not null;

    public RequestUrlEditorViewModel UrlEditor { get; }
    public RequestHeadersViewModel HeadersEditor { get; }
    public RequestPreviewViewModel Preview { get; }
    public RequestBodyViewModel Body { get; }
    public RequestSettingsViewModel Settings { get; }
    public ComposerSectionsViewModel Sections { get; }
    public AuthEditorViewModel Auth { get; }
    public ResponseViewModel Response { get; }
    public RequestSendViewModel Sending { get; }
    public RequestFailureActionRouter FailureActions { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string SelectedMethod { get; set; } = "GET";
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    // ---- load / build ----

    private void Load(HttpRequestModel model)
    {
        using (_changes.Loading())
        {
            RequestId = model.Id;
            Name = model.Name;
            Title = model.Name;
            SelectedMethod = model.Method;
            UrlEditor.Load(model.Url, model.QueryParams, model.PathVariables);
            HeadersEditor.Load(model.Headers, model.SkippedInheritedHeaders);
            Body.Load(model.Body);
            Auth.Load(model.Auth);
            Settings.Load(model.Settings);
            _description = model.Description;
            _eventsJson = model.EventsJson;
        }
        OnPropertyChanged(nameof(Method));
        Preview.Refresh();
    }

    public HttpRequestModel BuildModel()
    {
        return new HttpRequestModel
        {
            Id = RequestId,
            Name = Name,
            Description = _description,
            EventsJson = _eventsJson,
            Method = string.IsNullOrWhiteSpace(SelectedMethod) ? "GET" : SelectedMethod.Trim().ToUpperInvariant(),
            Url = UrlEditor.Url,
            QueryParams = UrlEditor.Params.ToModels(),
            PathVariables = UrlEditor.PathVariables.ToModels(),
            Headers = HeadersEditor.Headers.ToModels(),
            SkippedInheritedHeaders = [.. HeadersEditor.SkippedInheritedHeaders],
            Body = Body.ToModel(),
            Auth = Auth.ToModel(),
            Settings = Settings.ToModel(),
        };
    }

    private string CurrentSnapshot() => ModelCloner.Serialize(BuildModel());

    public RequestContext CreateContext() => Services.Workspace.CreateContext(RequestId);

    // ---- change tracking ----
    // Edits are what the URL, headers, body, settings and auth report through Changed, plus the name and the method.

    partial void OnNameChanged(string value)
    {
        Title = value;
        AfterEdit();
    }

    partial void OnSelectedMethodChanged(string value)
    {
        OnPropertyChanged(nameof(Method));
        Sections.Method = value;
        AfterEdit();
    }

    /// <summary>Runs after each edit: updates the preview, then whether the tab has unsaved edits.</summary>
    private void AfterEdit()
    {
        if (_changes.IsLoading)
            return;
        Preview.Refresh();
        UpdateDirty();
    }

    private void UpdateDirty()
    {
        if (!_changes.IsLoading)
            IsDirty = _changes.IsDirty(CurrentSnapshot());
    }

    /// <summary>The request as it is now is the saved one.</summary>
    private void MarkSaved()
    {
        _changes.MarkSaved(CurrentSnapshot());
        UpdateDirty();
    }

    /// <summary>A cURL command pasted into the URL bar replaces the request, like Postman; the tab keeps its identity.</summary>
    private void OnCurlPasted(object? sender, CurlPastedEventArgs args)
    {
        var imported = args.Request;
        imported.Id = RequestId;
        imported.Name = Name;
        Load(imported);
        UpdateDirty();
    }

    // ---- variables / workspace ----

    public override void OnSettingsChanged() => Settings.OnAppSettingsChanged();

    public override void OnVariablesChanged()
    {
        Preview.RefreshVariables();
        Preview.Refresh();
        Auth.RefreshInherited();
    }

    public override void OnWorkspaceChanged()
    {
        Preview.RefreshLocation();
        Auth.RefreshInherited();
        Preview.Refresh();
    }

    // ---- commands ----

    /// <summary>Cancels the send and the token request in flight and frees the preview image.</summary>
    public override void Dispose()
    {
        Sending.Dispose();
        Auth.Dispose();
        Response.Dispose();
    }

    protected override void OnActiveChanged(bool isActive)
    {
        if (isActive)
            return;
        HeadersEditor.HideRevealedValues();
        Auth.HideRevealedValues();
        Response.HideRevealedValues();
    }

    public override async Task<bool> SaveAsync()
    {
        if (Services.Workspace.Find(RequestId) is { } location)
        {
            // When the write is given up, the edits stay in the tab.
            if (!await Services.Saver.SaveExistingAsync(location, BuildModel()))
                return false;
            MarkSaved();
            return true;
        }
        return await SaveAsNewAsync(keepId: true);
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    [RelayCommand]
    private Task SaveAs() => SaveAsNewAsync(keepId: false);

    private async Task<bool> SaveAsNewAsync(bool keepId)
    {
        var dialogVm = new SaveRequestDialogViewModel(Services.Workspace, Name);
        var target = await Services.Dialogs.ShowDialogAsync<SaveRequestTarget>(new SaveRequestDialog { DataContext = dialogVm });
        if (target is null)
            return false;

        var previousId = RequestId;
        var previousName = Name;
        if (!keepId)
            RequestId = Guid.NewGuid();
        Name = target.Name;
        var model = BuildModel();
        if (!await Services.Saver.SaveNewAsync(target, model))
        {
            // Not on disk, so the tab is still the unsaved request it was.
            RequestId = previousId;
            Name = previousName;
            return false;
        }
        MarkSaved();
        Preview.RefreshLocation();
        Auth.RefreshInherited();
        return true;
    }

    [RelayCommand]
    private void StartRename() => IsRenaming = true;

    /// <summary>Ends the in-place rename. A null name cancels it and keeps the current one.</summary>
    public void CommitRename(string? newName)
    {
        IsRenaming = false;
        if (newName is null)
            return;
        Name = string.IsNullOrWhiteSpace(newName) ? "Untitled Request" : newName;
    }

    /// <summary>The request was renamed from the sidebar. A clean tab stays clean.</summary>
    public void ApplyExternalRename(string name)
    {
        var wasDirty = IsDirty;
        Name = name;
        if (!wasDirty)
            MarkSaved();
    }

    public override bool KeepsDraftInSession => true;

    public override SessionTab ToSession() => new()
    {
        Kind = SessionTabKind.Request,
        EntityId = RequestId,
        RequestDraft = IsDirty || !IsPersisted ? BuildModel() : null,
        IsPinned = IsPinned,
    };

    public override string? CollectionName => Services.Workspace.Find(RequestId)?.Collection.Name;
}
