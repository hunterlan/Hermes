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
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Requests;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;
using Hermes.Core.Web;

namespace Hermes.App.ViewModels;

public sealed partial class RequestTabViewModel : TabViewModelBase, IRenamable
{
    private string _savedSnapshot = "";
    private bool _loading;
    private bool _syncingUrl;
    private CancellationTokenSource? _sendCts;
    private bool _isClosed;

    // Preserved, not editable in the UI.
    private string? _description;
    private string? _eventsJson;

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public RequestTabViewModel(TabServices services, HttpRequestModel model, string? savedSnapshot = null) : base(services)
    {
        RequestId = model.Id;
        Params.Changed += OnParamsChanged;
        PathVariables.Changed += OnAnyChanged;
        HeadersEditor = new RequestHeadersViewModel(BuildModel, CreateContext,
            id => Services.OpenContainerAsync?.Invoke(id) ?? Task.CompletedTask, services.Dialogs.CopyTextAsync);
        HeadersEditor.Headers.Changed += OnAnyChanged;
        HeadersEditor.Changed += UpdateDirty;
        Body = new RequestBodyViewModel(services.Dialogs);
        Body.Changed += OnAnyChanged;
        Body.PropertyChanged += OnBodyPropertyChanged;
        Settings = new RequestSettingsViewModel(services.Settings);
        Settings.Changed += OnSettingsEdited;

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

    public KeyValueListViewModel Params { get; } = new();
    public KeyValueListViewModel PathVariables { get; } = new(keysReadOnly: true);
    public RequestHeadersViewModel HeadersEditor { get; }
    public RequestBodyViewModel Body { get; }
    public RequestSettingsViewModel Settings { get; }
    public AuthEditorViewModel Auth { get; }
    public ResponseViewModel Response { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string SelectedMethod { get; set; } = "GET";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlSubstitutions))]
    public partial string Url { get; set; } = "";

    [ObservableProperty] public partial bool IsSending { get; set; }
    [ObservableProperty] public partial VariableScope Variables { get; set; } = VariableScope.Empty;
    [ObservableProperty] public partial string Breadcrumb { get; set; } = "";
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    /// <summary>The URL after variable resolution, shown under the URL bar as "Sends to".</summary>
    [ObservableProperty] public partial string ResolvedUrl { get; set; } = "";

    // ---- composer sections: only what the request uses is on screen; the rest waits as "Not used yet" chips ----

    [ObservableProperty] public partial bool ParamsAdded { get; set; }
    [ObservableProperty] public partial bool BodyAdded { get; set; }
    [ObservableProperty] public partial bool SettingsAdded { get; set; }
    [ObservableProperty] public partial bool ParamsExpanded { get; set; } = true;
    [ObservableProperty] public partial bool HeadersExpanded { get; set; } = true;
    [ObservableProperty] public partial bool AuthExpanded { get; set; }
    [ObservableProperty] public partial bool BodyExpanded { get; set; } = true;
    [ObservableProperty] public partial bool SettingsExpanded { get; set; } = true;

    /// <summary>Raised when the Settings section should be brought on screen; the view scrolls to it.</summary>
    public event Action? SettingsRevealRequested;

    /// <summary>Raised when the Headers section should be brought on screen; the view scrolls to it.</summary>
    public event Action? HeadersRevealRequested;

    /// <summary>Raised when the Body section should be brought on screen; the view scrolls to it.</summary>
    public event Action? BodyRevealRequested;

    /// <summary>Raised when the cursor should go to the URL; the view focuses it.</summary>
    public event Action? UrlFocusRequested;

    public bool IsParamsSectionVisible => ParamsAdded || Params.ActiveCount > 0 || HasPathVariables;
    public bool IsBodySectionVisible => BodyAdded || Body.HasBody || Body.HasUnsupportedBody || SelectedMethod is "POST" or "PUT" or "PATCH";
    public bool IsSettingsSectionVisible => SettingsAdded || Settings.HasSettingsOverrides;
    public bool HasUnusedSections => !IsParamsSectionVisible || !IsBodySectionVisible || !IsSettingsSectionVisible;

    public string? ActiveEnvironmentName => Services.Workspace.ActiveEnvironment?.Name;

    public string ParamsSummary => Params.ActiveCount is var n and > 0 ? $"{n} active" : "";
    /// <summary>Whether the URL has <c>{{variables}}</c> (including dynamic ones) or <c>:path</c> variables, i.e. whether "Sends to" adds information.</summary>
    public bool HasUrlSubstitutions =>
        VariableResolver.FindVariables(Url).Any() || UrlUtilities.ParsePathVariables(Url).Count > 0;
    public bool HasPathVariables => PathVariables.Rows.Count > 0;
    public string SendButtonText => IsSending ? "Cancel" : "Send";

    // ---- load / build ----

    private void Load(HttpRequestModel model)
    {
        _loading = true;
        _syncingUrl = true;
        RequestId = model.Id;
        Name = model.Name;
        Title = model.Name;
        SelectedMethod = model.Method;
        Url = model.Url;
        Params.Load(model.QueryParams.Count > 0 ? model.QueryParams : UrlUtilities.ParseQuery(UrlUtilities.Split(model.Url).Query));
        PathVariables.Load(UrlUtilities.MergePathVariables(model.Url, model.PathVariables));
        HeadersEditor.Load(model.Headers, model.SkippedInheritedHeaders);
        Body.Load(model.Body);
        Auth.Load(model.Auth);
        Settings.Load(model.Settings);
        _description = model.Description;
        _eventsJson = model.EventsJson;
        _syncingUrl = false;
        _loading = false;
        OnPropertyChanged(nameof(HasPathVariables));
        OnPropertyChanged(nameof(Method));
        RefreshPreview();
        NotifySections();
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
            Url = Url,
            QueryParams = Params.ToModels(),
            PathVariables = PathVariables.ToModels(),
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
                NotifySections();
                break;
            case nameof(Name):
                Title = Name;
                break;
            case nameof(ParamsAdded) or nameof(BodyAdded) or nameof(SettingsAdded):
                NotifySections();
                break;
        }

        if (e.PropertyName is nameof(Name) or nameof(SelectedMethod))
            OnAnyChanged();
    }

    /// <summary>A section the user edits stays on screen even if it becomes empty again.</summary>
    private void OnBodyPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_loading && e.PropertyName is nameof(RequestBodyViewModel.SelectedBodyMode))
            BodyAdded = true;
    }

    /// <summary>A section the user edits stays on screen even if it becomes empty again.</summary>
    private void OnSettingsEdited()
    {
        SettingsAdded = true;
        OnAnyChanged();
    }

    private void OnAnyChanged()
    {
        if (_loading)
            return;
        RefreshPreview();
        UpdateDirty();
        NotifySections();
    }

    partial void OnUrlChanged(string value)
    {
        if (_syncingUrl || _loading)
            return;

        // Pasting a cURL command into the URL bar imports it, like Postman.
        if (CurlParser.LooksLikeCurl(value))
        {
            try
            {
                var imported = CurlParser.Parse(value);
                imported.Id = RequestId;
                imported.Name = Name;
                Dispatch(() =>
                {
                    Load(imported);
                    UpdateDirty();
                });
                return;
            }
            catch (FormatException)
            {
            }
        }

        if (value.Contains('\n') || value.Contains('\r'))
        {
            Dispatch(() => Url = value.Replace("\r", "").Replace("\n", "").Trim());
            return;
        }

        _syncingUrl = true;
        try
        {
            Params.Load(UrlUtilities.MergeQueryParams(Params.ToModels(), UrlUtilities.ParseQuery(UrlUtilities.Split(value).Query)));
            PathVariables.Load(UrlUtilities.MergePathVariables(value, PathVariables.ToModels()));
            OnPropertyChanged(nameof(HasPathVariables));
            NotifySections();
        }
        finally
        {
            _syncingUrl = false;
        }
        OnAnyChanged();
    }

    /// <summary>Changing the text the editor is currently raising events for must be deferred.</summary>
    private static void Dispatch(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    private void OnParamsChanged()
    {
        NotifySections();
        if (_syncingUrl || _loading)
            return;
        ParamsAdded = true;
        _syncingUrl = true;
        try
        {
            Url = UrlUtilities.WithQuery(Url, Params.ToModels());
        }
        finally
        {
            _syncingUrl = false;
        }
        OnAnyChanged();
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
            ResolvedUrl = Url;
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

    /// <summary>A "Not used yet" chip: brings the section on screen, expanded.</summary>
    [RelayCommand]
    private void AddSection(string section)
    {
        switch (section)
        {
            case "params":
                ParamsAdded = ParamsExpanded = true;
                break;
            case "body":
                BodyAdded = BodyExpanded = true;
                break;
            case "settings":
                SettingsAdded = SettingsExpanded = true;
                break;
        }
    }

    /// <summary>Shows the Settings section, expanded, and asks the view to bring it on screen.</summary>
    private void RevealSettings()
    {
        SettingsAdded = SettingsExpanded = true;
        SettingsRevealRequested?.Invoke();
    }

    private void RevealHeaders()
    {
        HeadersExpanded = true;
        HeadersRevealRequested?.Invoke();
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
        {
            BodyAdded = BodyExpanded = true;
            BodyRevealRequested?.Invoke();
        }
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
                RevealSettings();
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
                RevealHeaders();
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

    private void NotifySections()
    {
        OnPropertyChanged(nameof(IsParamsSectionVisible));
        OnPropertyChanged(nameof(IsBodySectionVisible));
        OnPropertyChanged(nameof(IsSettingsSectionVisible));
        OnPropertyChanged(nameof(HasUnusedSections));
        OnPropertyChanged(nameof(ParamsSummary));
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
