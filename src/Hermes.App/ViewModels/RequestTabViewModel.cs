using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Views.Dialogs;
using Hermes.Core.CodeGen;
using Hermes.Core.Formatting;
using Hermes.Core.Http;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Requests;
using Hermes.Core.Secrets;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;
using Hermes.Core.Web;

namespace Hermes.App.ViewModels;

public sealed partial class RequestTabViewModel : TabViewModelBase
{
    private string _savedSnapshot = "";
    private bool _loading;
    private bool _syncingUrl;
    private CancellationTokenSource? _sendCts;
    private const string AutomaticHeaderTag = "added automatically";

    // Preserved, not editable in the UI.
    private string? _description;
    private string? _eventsJson;
    private string? _unsupportedBodyJson;

    public static IReadOnlyList<string> Methods { get; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public static IReadOnlyList<Option<BodyMode>> BodyModes { get; } =
    [
        new("none", BodyMode.None),
        new("form-data", BodyMode.FormData),
        new("x-www-form-urlencoded", BodyMode.UrlEncoded),
        new("raw", BodyMode.Raw),
        new("binary", BodyMode.Binary),
    ];

    public static IReadOnlyList<Option<RawLanguage>> RawLanguages { get; } =
    [
        new("Text", RawLanguage.Text),
        new("JavaScript", RawLanguage.JavaScript),
        new("JSON", RawLanguage.Json),
        new("HTML", RawLanguage.Html),
        new("XML", RawLanguage.Xml),
    ];

    public RequestTabViewModel(TabServices services, HttpRequestModel model, string? savedSnapshot = null) : base(services)
    {
        RequestId = model.Id;
        Params.Changed += OnParamsChanged;
        PathVariables.Changed += OnAnyChanged;
        Headers.Changed += OnAnyChanged;
        FormData.Changed += OnAnyChanged;
        UrlEncoded.Changed += OnAnyChanged;
        FormData.PickFile = () => services.Dialogs.PickFileAsync("Select file");

        Auth = services.CreateAuthEditor(allowInherit: true);
        Auth.Changed += OnAnyChanged;
        Auth.ScopeProvider = () => CreateContext().BuildScope();
        Auth.VerifySslProvider = () => VerifySsl;
        Auth.InheritedAuthProvider = () => CreateContext().ResolveAuth(AuthConfig.Inherit());
        Auth.ChainProvider = () => (AuthInheritance.OuterLevels(CreateContext()), "Request", Name);

        Response = new ResponseViewModel(services.Dialogs, services.Settings);
        Response.SetSaved(services.Workspace.LoadResponses(model.Id), show: true);

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
    public KeyValueListViewModel Headers { get; } = new();
    public KeyValueListViewModel FormData { get; } = new(allowFiles: true);
    public KeyValueListViewModel UrlEncoded { get; } = new();
    public AuthEditorViewModel Auth { get; }
    public ResponseViewModel Response { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string SelectedMethod { get; set; } = "GET";
    [ObservableProperty] public partial string Url { get; set; } = "";
    [ObservableProperty] public partial Option<BodyMode> SelectedBodyMode { get; set; } = BodyModes[0];
    [ObservableProperty] public partial Option<RawLanguage> SelectedRawLanguage { get; set; } = RawLanguages[2];
    [ObservableProperty] public partial string RawBody { get; set; } = "";
    [ObservableProperty] public partial string? BinaryFilePath { get; set; }
    /// <summary>The request's own setting; null follows the app setting.</summary>
    [ObservableProperty] public partial bool? FollowRedirectsOverride { get; set; }
    [ObservableProperty] public partial bool? VerifySslOverride { get; set; }
    [ObservableProperty] public partial string TimeoutText { get; set; } = "";

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

    public bool IsParamsSectionVisible => ParamsAdded || Params.ActiveCount > 0 || HasPathVariables;
    public bool IsBodySectionVisible => BodyAdded || HasBody || HasUnsupportedBody || SelectedMethod is "POST" or "PUT" or "PATCH";
    /// <summary>The effective setting, shown by the toggle; turning the toggle makes it an override.</summary>
    public bool FollowRedirects
    {
        get => FollowRedirectsOverride ?? Services.Settings.Current.FollowRedirects;
        set => FollowRedirectsOverride = value;
    }

    public bool VerifySsl
    {
        get => VerifySslOverride ?? Services.Settings.Current.VerifySsl;
        set => VerifySslOverride = value;
    }

    public bool HasSettingsOverrides => FollowRedirectsOverride is not null || VerifySslOverride is not null || TimeoutMs is not null;

    private int? TimeoutMs => int.TryParse(TimeoutText, out var t) && t >= 0 ? t : null;
    public bool IsSettingsSectionVisible => SettingsAdded || HasSettingsOverrides;
    public bool HasUnusedSections => !IsParamsSectionVisible || !IsBodySectionVisible || !IsSettingsSectionVisible;

    public string? ActiveEnvironmentName => Services.Workspace.ActiveEnvironment?.Name;

    public List<HeaderRowViewModel> HiddenHeaders { get; private set; } = [];

    public string HeadersSummary => (Headers.ActiveCount, HiddenHeaders.Count) switch
    {
        (0, 0) => "",
        (var mine, 0) => $"{mine} yours",
        (0, var auto) => $"{auto} added automatically",
        var (mine, auto) => $"{mine} yours · {auto} added automatically",
    };
    public string ParamsSummary => Params.ActiveCount is var n and > 0 ? $"{n} active" : "";
    public string BodySummary => SelectedBodyMode.Value == BodyMode.Raw ? SelectedRawLanguage.Label : SelectedBodyMode.Label;
    public bool HasPathVariables => PathVariables.Rows.Count > 0;
    public bool HasBody => SelectedBodyMode.Value != BodyMode.None;
    public bool IsBodyNone => SelectedBodyMode.Value == BodyMode.None;
    public bool IsBodyFormData => SelectedBodyMode.Value == BodyMode.FormData;
    public bool IsBodyUrlEncoded => SelectedBodyMode.Value == BodyMode.UrlEncoded;
    public bool IsBodyRaw => SelectedBodyMode.Value == BodyMode.Raw;
    public bool IsBodyBinary => SelectedBodyMode.Value == BodyMode.Binary;
    public bool HasUnsupportedBody => _unsupportedBodyJson is not null && IsBodyNone;
    public string RawEditorLanguage => SelectedRawLanguage.Value switch
    {
        RawLanguage.Json => "json",
        RawLanguage.Xml => "xml",
        RawLanguage.Html => "html",
        RawLanguage.JavaScript => "javascript",
        _ => "text",
    };
    public string SendButtonText => IsSending ? "Cancel" : "Send";
    public string BinaryFileLabel => string.IsNullOrEmpty(BinaryFilePath) ? "No file selected" : Path.GetFileName(BinaryFilePath);

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
        PathVariables.Load(MergePathVariables(model.Url, model.PathVariables));
        Headers.Load(model.Headers);
        SelectedBodyMode = BodyModes.First(m => m.Value == model.Body.Mode);
        SelectedRawLanguage = RawLanguages.First(l => l.Value == model.Body.RawLanguage);
        RawBody = model.Body.Raw;
        FormData.Load(model.Body.FormData);
        UrlEncoded.Load(model.Body.UrlEncoded);
        BinaryFilePath = model.Body.BinaryFilePath;
        Auth.Load(model.Auth);
        FollowRedirectsOverride = model.Settings.FollowRedirects;
        VerifySslOverride = model.Settings.VerifySsl;
        TimeoutText = model.Settings.TimeoutMs?.ToString() ?? "";
        _description = model.Description;
        _eventsJson = model.EventsJson;
        _unsupportedBodyJson = model.Body.UnsupportedJson;
        _syncingUrl = false;
        _loading = false;
        OnPropertyChanged(nameof(HasPathVariables));
        OnPropertyChanged(nameof(HasUnsupportedBody));
        OnPropertyChanged(nameof(Method));
        RefreshHiddenHeaders();
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
            Headers = Headers.ToModels(),
            Body = new RequestBody
            {
                Mode = SelectedBodyMode.Value,
                Raw = RawBody,
                RawLanguage = SelectedRawLanguage.Value,
                FormData = FormData.ToModels(),
                UrlEncoded = UrlEncoded.ToModels(),
                BinaryFilePath = BinaryFilePath,
                UnsupportedJson = _unsupportedBodyJson,
            },
            Auth = Auth.ToModel(),
            Settings = new RequestSettings
            {
                FollowRedirects = FollowRedirectsOverride,
                VerifySsl = VerifySslOverride,
                TimeoutMs = TimeoutMs,
            },
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
            case nameof(SelectedBodyMode):
                OnPropertyChanged(nameof(HasBody));
                OnPropertyChanged(nameof(IsBodyNone));
                OnPropertyChanged(nameof(IsBodyFormData));
                OnPropertyChanged(nameof(IsBodyUrlEncoded));
                OnPropertyChanged(nameof(IsBodyRaw));
                OnPropertyChanged(nameof(IsBodyBinary));
                OnPropertyChanged(nameof(HasUnsupportedBody));
                break;
            case nameof(SelectedRawLanguage):
                OnPropertyChanged(nameof(RawEditorLanguage));
                break;
            case nameof(IsSending):
                OnPropertyChanged(nameof(SendButtonText));
                OnPropertyChanged(nameof(SendButtonCommand));
                break;
            case nameof(BinaryFilePath):
                OnPropertyChanged(nameof(BinaryFileLabel));
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

        // A section the user edits stays on screen even if it becomes empty again.
        if (!_loading)
        {
            if (e.PropertyName is nameof(SelectedBodyMode))
                BodyAdded = true;
            else if (e.PropertyName is nameof(FollowRedirectsOverride) or nameof(VerifySslOverride) or nameof(TimeoutText))
                SettingsAdded = true;
        }

        if (e.PropertyName is nameof(Name) or nameof(SelectedMethod) or nameof(SelectedBodyMode) or nameof(SelectedRawLanguage)
            or nameof(RawBody) or nameof(BinaryFilePath) or nameof(FollowRedirectsOverride) or nameof(VerifySslOverride)
            or nameof(TimeoutText))
            OnAnyChanged();
    }

    private void OnAnyChanged()
    {
        if (_loading)
            return;
        RefreshHiddenHeaders();
        UpdateDirty();
        NotifySections();
    }

    partial void OnFollowRedirectsOverrideChanged(bool? value) => OnPropertyChanged(nameof(FollowRedirects));

    partial void OnVerifySslOverrideChanged(bool? value) => OnPropertyChanged(nameof(VerifySsl));

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
            Params.Load(MergeQueryParams(Params.ToModels(), UrlUtilities.ParseQuery(UrlUtilities.Split(value).Query)));
            PathVariables.Load(MergePathVariables(value, PathVariables.ToModels()));
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
    /// Enabled rows are replaced by what's in the URL; disabled rows (which aren't in the URL) keep their position.
    /// Descriptions are carried over by position.
    /// </summary>
    internal static List<KeyValueItem> MergeQueryParams(List<KeyValueItem> existing, List<KeyValueItem> parsed)
    {
        var result = new List<KeyValueItem>();
        var enabledExisting = existing.Where(e => e.Enabled).ToList();
        var next = 0;
        foreach (var row in existing)
        {
            if (!row.Enabled)
            {
                result.Add(row);
                continue;
            }
            if (next < parsed.Count)
            {
                var p = parsed[next];
                p.Description = enabledExisting.ElementAtOrDefault(next)?.Description;
                result.Add(p);
                next++;
            }
        }
        for (; next < parsed.Count; next++)
            result.Add(parsed[next]);
        return result;
    }

    private static List<KeyValueItem> MergePathVariables(string url, IEnumerable<KeyValueItem> existing)
    {
        var known = existing.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First());
        return UrlUtilities.ParsePathVariables(url)
            .Select(name => known.TryGetValue(name, out var item)
                ? new KeyValueItem(name, item.Value) { Description = item.Description }
                : new KeyValueItem(name, ""))
            .ToList();
    }

    /// <summary>Headers Hermes adds automatically (defaults, content type, auth) that the user didn't set, and the resolved URL.</summary>
    private void RefreshHiddenHeaders()
    {
        try
        {
            var model = BuildModel();
            var prepared = RequestPreparer.Preview(model, CreateContext());
            ResolvedUrl = prepared.Url;
            var userKeys = model.Headers.Where(h => h.Enabled).Select(h => h.Key.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hidden = prepared.Headers.Where(h => !userKeys.Contains(h.Key)).ToList();

            if (Uri.TryCreate(prepared.Url, UriKind.Absolute, out var uri))
                hidden.Insert(0, new KeyValueItem("Host", uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"));
            if (model.Body.Mode is BodyMode.FormData)
                hidden.Add(new KeyValueItem("Content-Type", "multipart/form-data; boundary=<calculated when request is sent>"));
            if (model.Body.Mode != BodyMode.None)
                hidden.Add(new KeyValueItem("Content-Length", "<calculated when request is sent>"));
            HiddenHeaders = hidden.Select(header => new HeaderRowViewModel(header.Key, header.Value, AutomaticHeaderTag)).ToList();
        }
        catch (Exception)
        {
            HiddenHeaders = [];
            ResolvedUrl = Url;
        }
        OnPropertyChanged(nameof(HiddenHeaders));
        OnPropertyChanged(nameof(HeadersSummary));
        OnPropertyChanged(nameof(ActiveEnvironmentName));
    }

    // ---- variables / workspace ----

    // The toggles show the app setting where the request has no override.
    public override void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(FollowRedirects));
        OnPropertyChanged(nameof(VerifySsl));
    }

    public override void OnVariablesChanged()
    {
        RefreshVariables();
        RefreshHiddenHeaders();
        Auth.RefreshInherited();
    }

    public override void OnWorkspaceChanged()
    {
        RefreshBreadcrumb();
        Auth.RefreshInherited();
        RefreshVariables();
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

    [RelayCommand]
    private async Task Send()
    {
        var model = BuildModel();
        // One context for the request and its masked copy: switching environments while it's in flight changes neither.
        var context = CreateContext();
        var (prepared, masked) = RequestPreparer.PrepareWithMasked(model, context);
        if (!await ConfirmSendAsync(prepared))
            return;

        IsSending = true;
        Response.SetSentRequest(prepared);
        Response.BeginLoading();
        _sendCts = new CancellationTokenSource();
        try
        {
            var result = await Services.Executor.ExecuteAsync(prepared, _sendCts.Token);
            if (result.Cancelled)
                Response.SetCancelled();
            else if (result.Response is { } response)
            {
                Response.SetResponse(response, sentWith: context.Environment);
                // Responses are kept with saved requests, so reopening shows the last result and earlier ones.
                // The saved copy has credentials masked; the one on screen stays exact.
                if (IsPersisted && Services.Workspace.SaveResponse(RequestId, new SavedResponse
                {
                    Response = response,
                    SentRequest = SecretMasker.MaskCredentialHeaders(RawRequestFormatter.Format(masked)),
                }) is { } saved)
                    Response.AddSaved(saved);
            }
            else
                Response.SetError(result.Error ?? "Unknown error");

            if (!result.Cancelled)
            {
                Services.Workspace.AddHistory(new HistoryEntry
                {
                    Request = model,
                    StatusCode = result.Response?.StatusCode,
                    DurationMs = (long)(result.Response?.TotalTime.TotalMilliseconds ?? 0),
                });
            }
        }
        finally
        {
            IsSending = false;
            _sendCts.Dispose();
            _sendCts = null;
        }
    }

    /// <summary>Variables used by the request that resolve to nothing; they would be sent as literal {{name}} text.</summary>
    public IReadOnlyList<string> FindUnresolvedVariables()
    {
        var model = BuildModel();
        var scope = CreateContext().BuildScope();
        IEnumerable<string?> texts =
        [
            model.Url,
            .. model.PathVariables.Select(p => p.Value),
            .. model.Headers.Where(h => h.Enabled).SelectMany(h => new[] { h.Key, h.Value }),
            .. model.Body.Mode switch
            {
                BodyMode.Raw => [model.Body.Raw],
                BodyMode.FormData => model.Body.FormData.Where(f => f.Enabled).SelectMany(f => new[] { f.Key, f.Value }),
                BodyMode.UrlEncoded => model.Body.UrlEncoded.Where(f => f.Enabled).SelectMany(f => new[] { f.Key, f.Value }),
                _ => [],
            },
        ];
        return texts.SelectMany(VariableResolver.FindVariables)
            .Select(m => m.Name)
            .Where(name => !VariableResolver.CanResolve(name, scope) || scope.TryGet(name, out var value) && value.Length == 0)
            .Distinct()
            .ToList();
    }

    /// <summary>Unresolved variables and environments marked "ask before sending" need a yes first.</summary>
    private async Task<bool> ConfirmSendAsync(PreparedRequest prepared)
    {
        var environment = Services.Workspace.ActiveEnvironment;
        var unresolved = FindUnresolvedVariables();
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

    public override async Task<bool> SaveAsync()
    {
        if (Services.Workspace.Find(RequestId) is { } location)
        {
            var model = BuildModel();
            var index = location.Container.IndexOf(location.Node);
            location.Container[index] = model;
            Services.Workspace.SaveCollection(location.Collection);
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

        if (!keepId)
            RequestId = Guid.NewGuid();
        if (target.IsNewCollection)
            Services.Workspace.AddCollection(target.Collection);
        Name = target.Name;
        var model = BuildModel();
        Services.Workspace.AddNode(target.Collection, target.Folder, model);
        _savedSnapshot = CurrentSnapshot();
        UpdateDirty();
        RefreshBreadcrumb();
        RefreshVariables();
        Auth.RefreshInherited();
        return true;
    }

    [RelayCommand]
    private void Beautify()
    {
        switch (SelectedRawLanguage.Value)
        {
            case RawLanguage.Json:
                RawBody = JsonFormatter.Format(RawBody);
                break;
            case RawLanguage.Xml or RawLanguage.Html:
                if (XmlFormatter.TryFormat(RawBody, out var xml))
                    RawBody = xml;
                break;
        }
    }

    [RelayCommand]
    private async Task SelectBinaryFile()
    {
        if (await Services.Dialogs.PickFileAsync("Select file") is { } path)
            BinaryFilePath = path;
    }

    [RelayCommand]
    private async Task ShowCode()
    {
        var prepared = RequestPreparer.Prepare(BuildModel(), CreateContext(), includeDefaultHeaders: false);
        var dialog = new CodeSnippetDialog { DataContext = new CodeSnippetViewModel(CurlGenerator.Generate(prepared), Services.Dialogs) };
        await Services.Dialogs.ShowDialogAsync<object>(dialog);
    }

    [RelayCommand]
    private void SetBodyMode(BodyMode mode) => SelectedBodyMode = BodyModes.First(m => m.Value == mode);

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

    /// <summary>Drops the request's own settings, so it follows the app settings again.</summary>
    [RelayCommand]
    private void UseDefaultSettings()
    {
        FollowRedirectsOverride = null;
        VerifySslOverride = null;
        TimeoutText = "";
    }

    private void NotifySections()
    {
        OnPropertyChanged(nameof(IsParamsSectionVisible));
        OnPropertyChanged(nameof(IsBodySectionVisible));
        OnPropertyChanged(nameof(HasSettingsOverrides));
        OnPropertyChanged(nameof(IsSettingsSectionVisible));
        OnPropertyChanged(nameof(HasUnusedSections));
        OnPropertyChanged(nameof(ParamsSummary));
        OnPropertyChanged(nameof(BodySummary));
    }

    [RelayCommand]
    private void StartRename() => IsRenaming = true;

    [RelayCommand]
    private void EndRename()
    {
        IsRenaming = false;
        if (string.IsNullOrWhiteSpace(Name))
            Name = "Untitled Request";
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
