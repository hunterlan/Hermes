using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Formatting;
using Hermes.Core.Http;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

public sealed partial class RequestTabViewModel : TabViewModelBase
{
    private string _savedSnapshot = "";
    private bool _loading;
    private bool _syncingUrl;
    private CancellationTokenSource? _sendCts;

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
        Auth.InheritedAuthProvider = () => CreateContext().ResolveAuth(Core.Models.AuthConfig.Inherit());

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
    [ObservableProperty] public partial bool FollowRedirects { get; set; }
    [ObservableProperty] public partial bool VerifySsl { get; set; }
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
    public bool HasSettingsOverrides => BuildModel().Settings is { FollowRedirects: not null } or { VerifySsl: not null } or { TimeoutMs: not null };
    public bool IsSettingsSectionVisible => SettingsAdded || HasSettingsOverrides;
    public bool HasUnusedSections => !IsParamsSectionVisible || !IsBodySectionVisible || !IsSettingsSectionVisible;

    public string? ActiveEnvironmentName => Services.Workspace.ActiveEnvironment?.Name;

    public List<KeyValueItem> HiddenHeaders { get; private set; } = [];

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
        var settings = Services.Settings.Current;
        FollowRedirects = model.Settings.FollowRedirects ?? settings.FollowRedirects;
        VerifySsl = model.Settings.VerifySsl ?? settings.VerifySsl;
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
        var settings = Services.Settings.Current;
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
                FollowRedirects = FollowRedirects == settings.FollowRedirects ? null : FollowRedirects,
                VerifySsl = VerifySsl == settings.VerifySsl ? null : VerifySsl,
                TimeoutMs = int.TryParse(TimeoutText, out var t) && t >= 0 ? t : null,
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
            else if (e.PropertyName is nameof(FollowRedirects) or nameof(VerifySsl) or nameof(TimeoutText))
                SettingsAdded = true;
        }

        if (e.PropertyName is nameof(Name) or nameof(SelectedMethod) or nameof(SelectedBodyMode) or nameof(SelectedRawLanguage)
            or nameof(RawBody) or nameof(BinaryFilePath) or nameof(FollowRedirects) or nameof(VerifySsl) or nameof(TimeoutText))
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
            var prepared = RequestPreparer.Prepare(model, CreateContext());
            ResolvedUrl = prepared.Url;
            var userKeys = model.Headers.Where(h => h.Enabled).Select(h => h.Key.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hidden = prepared.Headers.Where(h => !userKeys.Contains(h.Key)).ToList();

            if (Uri.TryCreate(prepared.Url, UriKind.Absolute, out var uri))
                hidden.Insert(0, new KeyValueItem("Host", uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"));
            if (model.Body.Mode is BodyMode.FormData)
                hidden.Add(new KeyValueItem("Content-Type", "multipart/form-data; boundary=<calculated when request is sent>"));
            if (model.Body.Mode != BodyMode.None)
                hidden.Add(new KeyValueItem("Content-Length", "<calculated when request is sent>"));
            HiddenHeaders = hidden;
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

    [RelayCommand]
    private async Task Send()
    {
        if (IsSending)
        {
            _sendCts?.Cancel();
            return;
        }

        var model = BuildModel();
        var prepared = RequestPreparer.Prepare(model, CreateContext());

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
                Response.SetResponse(response);
                // Responses are kept with saved requests, so reopening shows the last result and earlier ones.
                if (IsPersisted && Services.Workspace.SaveResponse(RequestId, new SavedResponse { Response = response, SentRequest = Response.SentRequestText }) is { } saved)
                    Response.SetSaved(saved, show: false);
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
                RawBody = BodyFormatter.FormatJson(RawBody);
                break;
            case RawLanguage.Xml or RawLanguage.Html:
                if (BodyFormatter.TryFormatXml(RawBody, out var xml))
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

    public override SessionTab ToSession() => new()
    {
        Kind = SessionTabKind.Request,
        EntityId = RequestId,
        RequestDraft = IsDirty || !IsPersisted ? BuildModel() : null,
        IsPinned = IsPinned,
    };

    public override string? CollectionName => Services.Workspace.Find(RequestId)?.Collection.Name;
}
