using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.CodeGen;
using Hermes.Core.Formatting;
using Hermes.Core.Headers;
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

public sealed partial class RequestTabViewModel : TabViewModelBase, IRenamable
{
    private string _savedSnapshot = "";
    private bool _loading;
    private bool _syncingUrl;
    private CancellationTokenSource? _sendCts;
    private const string AutomaticHeaderTag = "added automatically";
    private const string BodyHeaderTag = "from body";
    private const string OverridesTagPrefix = "Overrides";
    private const string ReplacesAuthTag = "Replaces auth header";
    private const string ReplacedByBodyTag = "replaced by body";

    // Preserved, not editable in the UI.
    private string? _description;
    private string? _eventsJson;
    private string? _unsupportedBodyJson;
    private List<string> _skippedInheritedHeaders = [];

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlSubstitutions))]
    public partial string Url { get; set; } = "";
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

    /// <summary>Headers of the folders and collection, outermost first; the ones sent come in the Sent request's order.</summary>
    public List<HeaderRowViewModel> InheritedHeaders { get; private set; } = [];

    /// <summary>Headers Hermes adds itself (Host, defaults, body and auth headers).</summary>
    public List<HeaderRowViewModel> AutomaticHeaders { get; private set; } = [];

    public bool HasInheritedHeaders => InheritedHeaders.Count > 0;
    public bool HasAutomaticHeaders => AutomaticHeaders.Count > 0;

    public string HeadersSummary
    {
        get
        {
            var parts = new List<string>();
            AddPart(Headers.ActiveCount, "own");
            AddPart(InheritedHeaders.Count(row => row.IsSent && !row.IsOverridden), "inherited");
            AddPart(AutomaticHeaders.Count, "added automatically");
            return string.Join(" · ", parts);

            void AddPart(int count, string label)
            {
                if (count > 0)
                    parts.Add($"{count} {label}");
            }
        }
    }
    public string ParamsSummary => Params.ActiveCount is var n and > 0 ? $"{n} active" : "";
    public string BodySummary => SelectedBodyMode.Value == BodyMode.Raw ? SelectedRawLanguage.Label : SelectedBodyMode.Label;
    /// <summary>Whether the URL has <c>{{variables}}</c> (including dynamic ones) or <c>:path</c> variables, i.e. whether "Sends to" adds information.</summary>
    public bool HasUrlSubstitutions =>
        VariableResolver.FindVariables(Url).Any() || UrlUtilities.ParsePathVariables(Url).Count > 0;
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
        _skippedInheritedHeaders = [.. model.SkippedInheritedHeaders];
        _syncingUrl = false;
        _loading = false;
        OnPropertyChanged(nameof(HasPathVariables));
        OnPropertyChanged(nameof(HasUnsupportedBody));
        OnPropertyChanged(nameof(Method));
        RefreshHeaderRows();
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
            SkippedInheritedHeaders = [.. _skippedInheritedHeaders],
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
        RefreshHeaderRows();
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

    /// <summary>
    /// Rebuilds what the Headers section shows besides the editable table: the notes on the request's own rows, the
    /// inherited rows, and the headers Hermes adds itself. Also updates the resolved URL.
    /// </summary>
    private void RefreshHeaderRows()
    {
        try
        {
            var model = BuildModel();
            var context = CreateContext();
            var prepared = RequestPreparer.Preview(model, context);
            ResolvedUrl = prepared.Url;
            var effective = context.ResolveHeaders(model);

            ShowOwnHeaderNotes(model, effective);
            InheritedHeaders = effective.Inherited.Select(header => CreateInheritedRow(header, prepared)).ToList();
            AutomaticHeaders = CreateAutomaticRows(model, prepared, context.ResolveAuth(model.Auth));
        }
        catch (Exception)
        {
            Headers.SetRowTags([]);
            InheritedHeaders = [];
            AutomaticHeaders = [];
            ResolvedUrl = Url;
        }
        OnPropertyChanged(nameof(InheritedHeaders));
        OnPropertyChanged(nameof(AutomaticHeaders));
        OnPropertyChanged(nameof(HasInheritedHeaders));
        OnPropertyChanged(nameof(HasAutomaticHeaders));
        OnPropertyChanged(nameof(HeadersSummary));
        OnPropertyChanged(nameof(ActiveEnvironmentName));
    }

    /// <summary>"Overrides X" on a row that replaces an inherited header, and a warning on one that replaces the auth header.</summary>
    private void ShowOwnHeaderNotes(HttpRequestModel model, EffectiveHeaders effective)
    {
        // The resolved rows are the model's own row instances, so rows are matched by reference.
        var resolvedByRow = effective.Own.ToDictionary<ResolvedHeader, KeyValueItem>(header => header.Item, ReferenceEqualityComparer.Instance);
        Headers.SetRowTags(model.Headers.Select(row =>
        {
            if (!resolvedByRow.TryGetValue(row, out var header))
                return ("", "");
            // The nearest level that sets the name is the last inherited row of it (outermost first).
            var overridden = header.OverridesOuter
                ? effective.Inherited.LastOrDefault(inherited => string.Equals(inherited.Key, header.Key, StringComparison.OrdinalIgnoreCase))
                : null;
            return (overridden is null ? "" : $"{OverridesTagPrefix} {overridden.Source.Name}",
                header.ReplacesAuthHeader ? ReplacesAuthTag : "");
        }).ToList());
    }

    private HeaderRowViewModel CreateInheritedRow(ResolvedHeader header, PreparedRequest prepared)
    {
        var source = header.Source;
        // Overridden by the request (its table is above) or by an inner folder; or left out by the body itself.
        var replacedByBody = header.State == HeaderState.Sent && prepared.RowsReplacedByBody.Contains(header.Item);
        var overriddenText = replacedByBody ? ReplacedByBodyTag
            : header.OverriddenBy is { Kind: not HeaderSourceKind.Request } folder ? $"overridden by {folder.Name}"
            : "overridden above";
        return new HeaderRowViewModel(header.Key, header.Value, header.ReplacesAuthHeader ? ReplacesAuthTag : "")
        {
            OverriddenTagText = overriddenText,
            IsTagWarning = header.ReplacesAuthHeader,
            SourceName = source.Name,
            IsSourceCollection = source.Kind == HeaderSourceKind.Collection,
            OpenSource = () => _ = Services.OpenContainerAsync?.Invoke(source.Id),
            CanSkip = true,
            InitiallySent = header.State != HeaderState.Skipped,
            IsOverridden = header.State == HeaderState.Overridden || replacedByBody,
            SentChanged = OnInheritedHeaderSentChanged,
        };
    }

    /// <summary>
    /// Skips (or unskips) every inherited header of that name. The rows are updated in place rather than rebuilt, so
    /// the checkbox the user just used keeps keyboard focus.
    /// </summary>
    private void OnInheritedHeaderSentChanged(HeaderRowViewModel row, bool isSent)
    {
        var name = row.Key.Trim();
        _skippedInheritedHeaders.RemoveAll(skipped => string.Equals(skipped.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (!isSent)
            _skippedInheritedHeaders.Add(name);
        if (_loading)
            return;

        try
        {
            var model = BuildModel();
            var context = CreateContext();
            var prepared = RequestPreparer.Preview(model, context);
            var effective = context.ResolveHeaders(model);
            // Same rows as before: skipping changes states, not which rows there are.
            if (effective.Inherited.Count == InheritedHeaders.Count)
            {
                for (var i = 0; i < InheritedHeaders.Count; i++)
                {
                    var resolved = effective.Inherited[i];
                    if (InheritedHeaders[i].IsOverridden)
                        continue;
                    InheritedHeaders[i].Update(resolved.State != HeaderState.Skipped, resolved.ReplacesAuthHeader ? ReplacesAuthTag : "",
                        resolved.ReplacesAuthHeader);
                }
                AutomaticHeaders = CreateAutomaticRows(model, prepared, context.ResolveAuth(model.Auth));
                OnPropertyChanged(nameof(AutomaticHeaders));
                OnPropertyChanged(nameof(HasAutomaticHeaders));
                OnPropertyChanged(nameof(HeadersSummary));
            }
            else
            {
                RefreshHeaderRows();
            }
        }
        catch (Exception)
        {
            RefreshHeaderRows();
        }
        UpdateDirty();
    }

    /// <summary>
    /// The headers that aren't set by the user. Where each comes from is recorded when the request is prepared, so
    /// nothing is guessed from header names. Host and the multipart content type are not part of the prepared
    /// request, and are added here.
    /// </summary>
    private static List<HeaderRowViewModel> CreateAutomaticRows(HttpRequestModel model, PreparedRequest prepared, EffectiveAuth auth)
    {
        var authTag = auth.InheritedFrom is { } source ? $"from auth · {source}" : "from auth";
        var rows = new List<HeaderRowViewModel>();

        if (Uri.TryCreate(prepared.Url, UriKind.Absolute, out var uri))
            rows.Add(new HeaderRowViewModel("Host", uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}", AutomaticHeaderTag));
        foreach (var header in prepared.Headers)
        {
            var tag = prepared.OriginOf(header) switch
            {
                PreparedHeaderOrigin.Auth => authTag,
                PreparedHeaderOrigin.Body => BodyHeaderTag,
                PreparedHeaderOrigin.Default => AutomaticHeaderTag,
                _ => null,
            };
            if (tag is not null)
                rows.Add(new HeaderRowViewModel(header.Key, header.Value, tag));
        }
        if (model.Body.Mode is BodyMode.FormData)
            rows.Add(new HeaderRowViewModel("Content-Type", "multipart/form-data; boundary=<calculated when request is sent>", BodyHeaderTag));
        if (model.Body.Mode != BodyMode.None)
            rows.Add(new HeaderRowViewModel("Content-Length", "<calculated when request is sent>", BodyHeaderTag));
        return rows;
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
        RefreshHeaderRows();
        Auth.RefreshInherited();
    }

    public override void OnWorkspaceChanged()
    {
        RefreshBreadcrumb();
        Auth.RefreshInherited();
        RefreshVariables();
        RefreshHeaderRows();
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
        var context = CreateContext();
        var scope = context.BuildScope();
        IEnumerable<string?> texts =
        [
            model.Url,
            .. model.PathVariables.Select(p => p.Value),
            .. context.ResolveHeaders(model).HeadersToSend.SelectMany(h => new[] { h.Key, h.Value }),
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
