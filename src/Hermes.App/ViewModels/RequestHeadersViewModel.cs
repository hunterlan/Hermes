using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Core.Auth;
using Hermes.Core.Headers;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;

namespace Hermes.App.ViewModels;

/// <summary>
/// The Headers section of a request tab: the editable table of the request's own headers, the rows inherited from
/// folders and the collection (which can be skipped for this request), and the headers Hermes adds itself. It owns the
/// list of skipped inherited headers.
/// </summary>
public sealed class RequestHeadersViewModel : ObservableObject
{
    private const string AutomaticHeaderTag = "added automatically";
    private const string BodyHeaderTag = "from body";
    private const string OverridesTagPrefix = "Overrides";
    private const string ReplacesAuthTag = "Replaces auth header";
    private const string ReplacedByBodyTag = "replaced by body";
    private const string InheritedSection = "inherited";
    private const string AutomaticSection = "automatic";

    private readonly Func<HttpRequestModel> _buildModel;
    private readonly Func<RequestContext> _createContext;
    private readonly Func<Guid, Task> _openContainerAsync;
    private readonly HeaderRevealState _revealState;
    private List<string> _skippedInheritedHeaders = [];

    public RequestHeadersViewModel(
        Func<HttpRequestModel> buildModel,
        Func<RequestContext> createContext,
        Func<Guid, Task> openContainerAsync,
        Func<string, Task> copyText)
    {
        _buildModel = buildModel;
        _createContext = createContext;
        _openContainerAsync = openContainerAsync;
        _revealState = new HeaderRevealState(copyText);
        Headers.CopyText = copyText;
    }

    /// <summary>Raised when the user skips or unskips an inherited header, so the owner can update its dirty state.</summary>
    public event Action? Changed;

    /// <summary>The request's own headers.</summary>
    public KeyValueListViewModel Headers { get; } = new() { MasksSensitiveKeys = true };

    /// <summary>Headers of the folders and collection, outermost first; the ones sent come in the Sent request's order.</summary>
    public List<HeaderRowViewModel> InheritedRows { get; private set; } = [];

    /// <summary>Headers Hermes adds itself (Host, defaults, body and auth headers).</summary>
    public List<HeaderRowViewModel> AutomaticRows { get; private set; } = [];

    public bool HasInheritedRows => InheritedRows.Count > 0;
    public bool HasAutomaticRows => AutomaticRows.Count > 0;

    /// <summary>Names of the inherited headers this request leaves out.</summary>
    public IReadOnlyList<string> SkippedInheritedHeaders => _skippedInheritedHeaders;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            AddPart(Headers.ActiveCount, "own");
            AddPart(InheritedRows.Count(row => row.IsSent && !row.IsOverridden), "inherited");
            AddPart(AutomaticRows.Count, "added automatically");
            return string.Join(" · ", parts);

            void AddPart(int count, string label)
            {
                if (count > 0)
                    parts.Add($"{count} {label}");
            }
        }
    }

    /// <summary>Loads the table and the skip list. The rows around the table are built by <see cref="Refresh"/>.</summary>
    public void Load(IEnumerable<KeyValueItem> ownHeaders, IEnumerable<string> skippedInheritedHeaders)
    {
        Headers.Load(ownHeaders);
        _skippedInheritedHeaders = [.. skippedInheritedHeaders];
    }

    /// <summary>
    /// Rebuilds what the section shows besides the editable table: the notes on the request's own rows, the inherited
    /// rows, and the headers Hermes adds itself. <paramref name="prepared"/> is the preview of <paramref name="model"/>.
    /// </summary>
    public void Refresh(HttpRequestModel model, RequestContext context, PreparedRequest prepared)
    {
        var effective = context.ResolveHeaders(model);

        ShowOwnHeaderNotes(model, effective);
        InheritedRows = effective.Inherited.Select(header => CreateInheritedRow(header, prepared)).ToList();
        AutomaticRows = CreateAutomaticRows(model, prepared, context.ResolveAuth(model.Auth));
        NotifyRowsChanged();
    }

    /// <summary>Masks every revealed value again, for when the user leaves the tab.</summary>
    public void HideRevealedValues()
    {
        _revealState.Clear();
        foreach (var row in InheritedRows.Concat(AutomaticRows))
            row.Sensitive.Hide();
        Headers.HideRevealedValues();
    }

    /// <summary>Drops the notes and the rows, for when the request can't be previewed.</summary>
    public void Clear()
    {
        Headers.SetRowTags([]);
        InheritedRows = [];
        AutomaticRows = [];
        NotifyRowsChanged();
    }

    private void NotifyRowsChanged()
    {
        OnPropertyChanged(nameof(InheritedRows));
        OnPropertyChanged(nameof(AutomaticRows));
        OnPropertyChanged(nameof(HasInheritedRows));
        OnPropertyChanged(nameof(HasAutomaticRows));
        OnPropertyChanged(nameof(Summary));
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
            Sensitive = _revealState.CreateValue(InheritedSection, header.Key, header.Value, SensitiveHeaders.IsSensitiveName(header.Key)),
            OverriddenTagText = overriddenText,
            IsTagWarning = header.ReplacesAuthHeader,
            SourceName = source.Name,
            IsSourceCollection = source.Kind == HeaderSourceKind.Collection,
            OpenSource = () => _ = _openContainerAsync(source.Id),
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

        try
        {
            var model = _buildModel();
            var context = _createContext();
            var prepared = RequestPreparer.Preview(model, context);
            var effective = context.ResolveHeaders(model);
            // Same rows as before: skipping changes states, not which rows there are.
            if (effective.Inherited.Count == InheritedRows.Count)
            {
                for (var i = 0; i < InheritedRows.Count; i++)
                {
                    var resolved = effective.Inherited[i];
                    if (InheritedRows[i].IsOverridden)
                        continue;
                    InheritedRows[i].Update(resolved.State != HeaderState.Skipped, resolved.ReplacesAuthHeader ? ReplacesAuthTag : "",
                        resolved.ReplacesAuthHeader);
                }
                AutomaticRows = CreateAutomaticRows(model, prepared, context.ResolveAuth(model.Auth));
                OnPropertyChanged(nameof(AutomaticRows));
                OnPropertyChanged(nameof(HasAutomaticRows));
                OnPropertyChanged(nameof(Summary));
            }
            else
            {
                Refresh(model, context, prepared);
            }
        }
        catch (Exception)
        {
            Clear();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// The headers that aren't set by the user. Where each comes from is recorded when the request is prepared, so
    /// nothing is guessed from header names. Host and the multipart content type are not part of the prepared
    /// request, and are added here.
    /// </summary>
    private List<HeaderRowViewModel> CreateAutomaticRows(HttpRequestModel model, PreparedRequest prepared, EffectiveAuth auth)
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
            if (tag is null)
                continue;
            // Auth headers are credentials whatever their name (an API key may use any header).
            var isSensitive = prepared.OriginOf(header) == PreparedHeaderOrigin.Auth || SensitiveHeaders.IsSensitiveName(header.Key);
            rows.Add(new HeaderRowViewModel(header.Key, header.Value, tag)
            {
                Sensitive = _revealState.CreateValue(AutomaticSection, header.Key, header.Value, isSensitive),
            });
        }
        if (model.Body.Mode is BodyMode.FormData)
            rows.Add(new HeaderRowViewModel("Content-Type", "multipart/form-data; boundary=<calculated when request is sent>", BodyHeaderTag));
        if (model.Body.Mode != BodyMode.None)
            rows.Add(new HeaderRowViewModel("Content-Length", "<calculated when request is sent>", BodyHeaderTag));
        return rows;
    }
}
