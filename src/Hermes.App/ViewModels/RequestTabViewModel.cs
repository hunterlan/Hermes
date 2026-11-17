using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.CodeGen;
using Hermes.Core.Headers;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Requests;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

public sealed partial class RequestTabViewModel : TabViewModelBase, IRenamable
{
    private string _savedSnapshot = "";
    private bool _loading;
    private CancellationTokenSource? _sendCts;
    private bool _isClosed;

    // Preserved, not editable in the UI.
    private string? _description;
    private string? _eventsJson;

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public RequestTabViewModel(TabServices services, HttpRequestModel model, string? savedSnapshot = null) : base(services)
    {
        RequestId = model.Id;
        UrlEditor = new RequestUrlEditorViewModel();
        UrlEditor.Changed += OnAnyChanged;
        UrlEditor.CurlPasted += OnCurlPasted;
        HeadersEditor = new RequestHeadersViewModel(BuildModel, CreateContext,
            id => Services.OpenContainerAsync?.Invoke(id) ?? Task.CompletedTask, services.Dialogs.CopyTextAsync);
        HeadersEditor.Headers.Changed += OnAnyChanged;
        HeadersEditor.Changed += UpdateDirty;
        Body = new RequestBodyViewModel(services.Dialogs);
        Body.Changed += OnAnyChanged;
        Settings = new RequestSettingsViewModel(services.Settings);
        Settings.Changed += OnAnyChanged;
        Sections = new ComposerSectionsViewModel(UrlEditor, Body, Settings);

        Auth = services.CreateAuthEditor(allowInherit: true);
        Auth.Changed += OnAnyChanged;
        Auth.ScopeProvider = () => CreateContext().BuildScope();
        Auth.VerifySslProvider = () => Settings.VerifySsl;
        Auth.SslSourceProvider = () => Settings.VerifySslOverride is null ? SslSettingSource.AppWide : SslSettingSource.Request;
        Auth.FailureActionRequested += OnFailureActionRequested;
        Auth.InheritedAuthProvider = () => CreateContext().ResolveAuth(AuthConfig.Inherit());
        Auth.ChainProvider = () => (AuthInheritance.OuterLevels(CreateContext()), "Request", Name);

        Response = new ResponseViewModel(services.Dialogs, services.Settings);
        Response.SetSaved(services.Workspace.LoadResponses(model.Id), show: true);
        Response.FailureActionRequested += OnFailureActionRequested;

        Load(model);
        _savedSnapshot = savedSnapshot ?? CurrentSnapshot();
        UpdateDirty();
        RefreshVariables();
        RefreshBreadcrumb();
    }

    public Guid RequestId { get; private set; }
    public override Guid EntityId => RequestId;
    public override SessionTabKind Kind => SessionTabKind.Request;
    public override string? Method => SelectedMethod;

    public bool IsPersisted => Services.Workspace.Find(RequestId) is not null;

    public RequestUrlEditorViewModel UrlEditor { get; }
    public RequestHeadersViewModel HeadersEditor { get; }
    public RequestBodyViewModel Body { get; }
    public RequestSettingsViewModel Settings { get; }
    public ComposerSectionsViewModel Sections { get; }
    public AuthEditorViewModel Auth { get; }
    public ResponseViewModel Response { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string SelectedMethod { get; set; } = "GET";

    [ObservableProperty] public partial bool IsSending { get; set; }
    [ObservableProperty] public partial VariableScope Variables { get; set; } = VariableScope.Empty;
    [ObservableProperty] public partial string Breadcrumb { get; set; } = "";
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    /// <summary>The URL after variable resolution, shown under the URL bar as "Sends to".</summary>
    [ObservableProperty] public partial string ResolvedUrl { get; set; } = "";

    /// <summary>Raised when the cursor should go to the URL; the view focuses it.</summary>
    public event Action? UrlFocusRequested;

    public string? ActiveEnvironmentName => Services.Workspace.ActiveEnvironment?.Name;

    public string SendButtonText => IsSending ? "Cancel" : "Send";

    // ---- load / build ----

    private void Load(HttpRequestModel model)
    {
        _loading = true;
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
        _loading = false;
        OnPropertyChanged(nameof(Method));
        RefreshPreview();
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

    private void UpdateDirty()
    {
        if (!_loading)
            IsDirty = CurrentSnapshot() != _savedSnapshot;
    }

    private RequestContext CreateContext() => Services.Workspace.CreateContext(RequestId);

    // ---- change tracking ----

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(IsSending):
                OnPropertyChanged(nameof(SendButtonText));
                OnPropertyChanged(nameof(SendButtonCommand));
                break;
            case nameof(SelectedMethod):
                OnPropertyChanged(nameof(Method));
                Sections.Method = SelectedMethod;
                break;
            case nameof(Name):
                Title = Name;
                break;
        }

        if (e.PropertyName is nameof(Name) or nameof(SelectedMethod))
            OnAnyChanged();
    }

    private void OnAnyChanged()
    {
        if (_loading)
            return;
        RefreshPreview();
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

    /// <summary>
    /// Updates the resolved URL and the Headers section from one preview of the request, so the preview is computed
    /// once per change.
    /// </summary>
    private void RefreshPreview()
    {
        try
        {
            var model = BuildModel();
            var context = CreateContext();
            var prepared = RequestPreparer.Preview(model, context);
            ResolvedUrl = prepared.Url;
            HeadersEditor.Refresh(model, context, prepared);
        }
        catch (Exception)
        {
            HeadersEditor.Clear();
            ResolvedUrl = UrlEditor.Url;
        }
        OnPropertyChanged(nameof(ActiveEnvironmentName));
    }

    // ---- variables / workspace ----

    public override void OnSettingsChanged() => Settings.OnAppSettingsChanged();

    public override void OnVariablesChanged()
    {
        RefreshVariables();
        RefreshPreview();
        Auth.RefreshInherited();
    }

    public override void OnWorkspaceChanged()
    {
        RefreshBreadcrumb();
        Auth.RefreshInherited();
        RefreshVariables();
        RefreshPreview();
    }

    private void RefreshVariables() => Variables = CreateContext().BuildScope();

    private void RefreshBreadcrumb()
    {
        if (Services.Workspace.Find(RequestId) is { } location)
        {
            Breadcrumb = string.Join(" / ", new[] { location.Collection.Name }.Concat(location.Ancestors.Select(a => a.Name)));
        }
        else
        {
            Breadcrumb = "";
        }
    }

    // ---- commands ----

    /// <summary>What the Send button does: send, or cancel the request in flight.</summary>
    public ICommand SendButtonCommand => IsSending ? CancelSendCommand : SendCommand;

    /// <summary>Ctrl+Enter: sends, or cancels the request in flight. Doesn't wait for the response.</summary>
    public void SendOrCancel()
    {
        if (SendButtonCommand.CanExecute(null))
            SendButtonCommand.Execute(null);
    }

    [RelayCommand]
    private void CancelSend() => _sendCts?.Cancel();

    /// <summary>Cancels the send and the token request in flight and frees the preview image.</summary>
    public override void Dispose()
    {
        _isClosed = true;
        CancelSend();
        Auth.Dispose();
        Response.Dispose();
    }

    [RelayCommand]
    private async Task Send()
    {
        var model = BuildModel();
        // One context for the request and its masked copy: switching environments while it's in flight changes neither.
        var context = CreateContext();
        var pair = RequestPreparer.PrepareWithMasked(model, context);
        var prepared = pair.Request;
        if (!await ConfirmSendAsync(model, context, prepared) || _isClosed)
            return;

        IsSending = true;
        Response.SetSentRequest(prepared);
        Response.BeginLoading();
        _sendCts = new CancellationTokenSource();
        try
        {
            var outcome = await Services.Sender.SendAsync(prepared, _sendCts.Token);
            // A tab closed during the send updates and records nothing; closing cancels the send.
            if (_isClosed)
                return;
            if (outcome.Cancelled)
            {
                Response.SetCancelled();
                return;
            }
            // Shown first, so a problem while recording can't keep the response off screen.
            var result = outcome.Result;
            if (result.Response is { } response)
                Response.SetResponse(response, sentWith: context.Environment);
            else
                Response.SetFailure(result.Failure ?? RequestFailure.FromMessage("Unknown error"));

            // The request this tab is now: Save As during the send made it a new one, and the response goes with that.
            var saved = Services.Sender.Record(new CompletedSend
            {
                Model = model,
                MaskedRequest = pair.Masked,
                Result = result,
                KeepResponseWith = IsPersisted ? RequestId : null,
            }, _sendCts.Token);
            if (saved is not null)
                Response.AddSaved(saved);
        }
        finally
        {
            IsSending = false;
            _sendCts.Dispose();
            _sendCts = null;
        }
    }

    /// <summary>Variables used by the request that resolve to nothing; they would be sent as literal {{name}} text.</summary>
    public IReadOnlyList<string> FindUnresolvedVariables() => UnresolvedVariableFinder.Find(BuildModel(), CreateContext());

    /// <summary>Unresolved variables and environments marked "ask before sending" need a yes first.</summary>
    private async Task<bool> ConfirmSendAsync(HttpRequestModel model, RequestContext context, PreparedRequest prepared)
    {
        var environment = Services.Workspace.ActiveEnvironment;
        var unresolved = UnresolvedVariableFinder.Find(model, context);
        if (unresolved.Count > 0)
        {
            var names = string.Join(", ", unresolved.Select(n => "{{" + n + "}}"));
            var where = environment is null ? "with no environment selected" : $"in {environment.Name}";
            if (!await Services.Dialogs.ConfirmAsync("Unresolved variables",
                    $"{names} {(unresolved.Count == 1 ? "has" : "have")} no value {where}, so the literal text would be sent.",
                    "Send anyway", destructive: false))
                return false;
        }
        if (environment is { ConfirmBeforeSend: true })
        {
            return await Services.Dialogs.ConfirmAsync($"Send to {environment.Name}?", $"{prepared.Method} {prepared.Url}",
                $"Send to {environment.Name}");
        }
        return true;
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
            _savedSnapshot = CurrentSnapshot();
            UpdateDirty();
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
        _savedSnapshot = CurrentSnapshot();
        UpdateDirty();
        RefreshBreadcrumb();
        RefreshVariables();
        Auth.RefreshInherited();
        return true;
    }

    [RelayCommand]
    private async Task ShowCode()
    {
        var prepared = RequestPreparer.Prepare(BuildModel(), CreateContext(), includeDefaultHeaders: false);
        var dialog = new CodeSnippetDialog { DataContext = new CodeSnippetViewModel(CurlGenerator.Generate(prepared), Services.Dialogs) };
        await Services.Dialogs.ShowDialogAsync<object>(dialog);
    }

    /// <summary>"Choose file" after the body's file couldn't be read: asks for another file and uses it.</summary>
    [RelayCommand]
    private async Task ChooseBodyFileAsync(string? unreadablePath)
    {
        if (await Services.Dialogs.PickFileAsync("Select file") is { } picked)
            UseChosenBodyFile(picked, unreadablePath);
    }

    /// <summary>
    /// Puts <paramref name="picked"/> in place of the file that couldn't be read. When the body has no field holding
    /// that path (it came from a variable), nothing is overwritten and the Body section is shown, so the user decides.
    /// </summary>
    /// <returns>True when a field was replaced.</returns>
    public bool UseChosenBodyFile(string picked, string? unreadablePath)
    {
        var replaced = Body.UseChosenBodyFile(picked, unreadablePath);
        if (!replaced)
            Sections.RevealBody();
        return replaced;
    }

    private void OnFailureActionRequested(object? sender, RequestFailureActionEventArgs args)
    {
        switch (args.Action)
        {
            case RequestFailureAction.TurnOffSslVerification:
                TurnOffVerificationAndResendCommand.Execute(null);
                break;
            case RequestFailureAction.OpenRequestSettings:
                Sections.RevealSettings();
                break;
            case RequestFailureAction.EditUrl:
                UrlFocusRequested?.Invoke();
                break;
            case RequestFailureAction.Retry:
                SendCommand.Execute(null);
                break;
            case RequestFailureAction.ChooseFile:
                ChooseBodyFileCommand.Execute(args.Failure.Subject);
                break;
            case RequestFailureAction.ShowHeaders:
                Sections.RevealHeaders();
                break;
            case RequestFailureAction.OpenAppSettings:
                OpenAppSettingsCommand.Execute(null);
                break;
            case RequestFailureAction.EditCallbackUrl:
                Auth.RequestFocus(AuthEditorField.CallbackUrl);
                break;
            default:
                throw new UnreachableException($"Unhandled failure action {args.Action}");
        }
    }

    /// <summary>"Turn off verification for this request": the request's own override, not the app-wide setting.</summary>
    [RelayCommand]
    private async Task TurnOffVerificationAndResendAsync()
    {
        Settings.VerifySslOverride = false;
        await SendCommand.ExecuteAsync(null);
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
        {
            _savedSnapshot = CurrentSnapshot();
            UpdateDirty();
        }
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
