using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Variables;
using Hermes.Core.Web;

namespace Hermes.App.ViewModels;

/// <summary>
/// The URL bar with its query params and path variables, kept in step both ways: editing the URL updates the rows,
/// and editing a param row rewrites the URL's query string.
/// </summary>
public sealed partial class RequestUrlEditorViewModel : ObservableObject
{
    /// <summary>
    /// True while the editor itself changes the URL or the rows, during a load or while one side follows the other;
    /// those changes aren't the user's edits.
    /// </summary>
    private bool _syncing;

    public RequestUrlEditorViewModel()
    {
        Params.Changed += OnParamsEdited;
        PathVariables.Changed += OnPathVariablesEdited;
    }

    /// <summary>Raised once for each edit of the URL, a param row or a path variable; not while loading.</summary>
    public event Action? Changed;

    /// <summary>Raised when the user edits the param rows; not when they follow the URL or are loaded.</summary>
    public event Action? ParamsEdited;

    /// <summary>
    /// Raised when a cURL command is pasted into the URL bar, after the text box has finished raising its events. The
    /// params aren't updated from the command's text; whoever handles it loads the request instead.
    /// </summary>
    public event EventHandler<CurlPastedEventArgs>? CurlPasted;

    public KeyValueListViewModel Params { get; } = new();
    public KeyValueListViewModel PathVariables { get; } = new(keysReadOnly: true);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlSubstitutions))]
    public partial string Url { get; set; } = "";

    /// <summary>Whether the URL has <c>{{variables}}</c> (including dynamic ones) or <c>:path</c> variables, i.e. whether "Sends to" adds information.</summary>
    public bool HasUrlSubstitutions =>
        VariableResolver.FindVariables(Url).Any() || UrlUtilities.ParsePathVariables(Url).Count > 0;

    public bool HasPathVariables => PathVariables.Rows.Count > 0;

    /// <summary>
    /// Replaces the URL and the rows without raising <see cref="Changed"/>. With no saved query params, they are
    /// read from the URL; path variables follow the URL's <c>:name</c> segments, keeping the saved values.
    /// </summary>
    public void Load(string url, IReadOnlyList<KeyValueItem> queryParams, IEnumerable<KeyValueItem> pathVariables)
    {
        _syncing = true;
        try
        {
            Url = url;
            Params.Load(queryParams.Count > 0 ? queryParams : UrlUtilities.ParseQuery(UrlUtilities.Split(url).Query));
            PathVariables.Load(UrlUtilities.MergePathVariables(url, pathVariables));
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(HasPathVariables));
    }

    partial void OnUrlChanged(string value)
    {
        if (_syncing)
            return;

        // Pasting a cURL command into the URL bar imports it, like Postman.
        if (CurlParser.LooksLikeCurl(value))
        {
            try
            {
                var imported = CurlParser.Parse(value);
                PostAfterTextBoxEvents(() => CurlPasted?.Invoke(this, new CurlPastedEventArgs(imported)));
                return;
            }
            catch (FormatException)
            {
            }
        }

        if (value.Contains('\n') || value.Contains('\r'))
        {
            PostAfterTextBoxEvents(() => Url = value.Replace("\r", "").Replace("\n", "").Trim());
            return;
        }

        _syncing = true;
        try
        {
            Params.Load(UrlUtilities.MergeQueryParams(Params.ToModels(), UrlUtilities.ParseQuery(UrlUtilities.Split(value).Query)));
            PathVariables.Load(UrlUtilities.MergePathVariables(value, PathVariables.ToModels()));
            OnPropertyChanged(nameof(HasPathVariables));
        }
        finally
        {
            _syncing = false;
        }
        Changed?.Invoke();
    }

    /// <summary>Changing the text the URL box is currently raising events for must be deferred.</summary>
    private static void PostAfterTextBoxEvents(Action action) => Dispatcher.UIThread.Post(action);

    private void OnParamsEdited()
    {
        if (_syncing)
            return;
        ParamsEdited?.Invoke();
        _syncing = true;
        try
        {
            Url = UrlUtilities.WithQuery(Url, Params.ToModels());
        }
        finally
        {
            _syncing = false;
        }
        Changed?.Invoke();
    }

    private void OnPathVariablesEdited()
    {
        if (!_syncing)
            Changed?.Invoke();
    }
}
