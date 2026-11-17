using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Headers;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Hermes.Core.Web;

namespace Hermes.App.ViewModels;

public sealed partial class ResponseViewModel : ObservableObject, IDisposable
{
    private readonly DialogService _dialogs;
    private readonly SettingsService _settings;
    private ResponseModel? _response;

    public ResponseViewModel(DialogService dialogs, SettingsService settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        Pane = new ResponsePaneLayoutViewModel(settings);
        Sent = new SentRequestViewModel(dialogs);
        Saved = new SavedResponsesViewModel();
        Saved.ShowRequested += OnSavedResponseShowRequested;
    }

    /// <summary>How the pane is laid out: stacked or side by side, collapsed, and its split.</summary>
    public ResponsePaneLayoutViewModel Pane { get; }

    /// <summary>The request that went out, for the "Sent request" tab.</summary>
    public SentRequestViewModel Sent { get; }

    /// <summary>The responses saved with the request.</summary>
    public SavedResponsesViewModel Saved { get; }

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasResponse { get; set; }

    /// <summary>Why there is no response; null while there is one, or none was asked for yet.</summary>
    [ObservableProperty] public partial RequestFailureViewModel? Failure { get; set; }

    [ObservableProperty] public partial ResponseViewMode ViewMode { get; set; } = ResponseViewMode.Pretty;

    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string TimeText { get; set; } = "";
    [ObservableProperty] public partial string SizeText { get; set; } = "";
    [ObservableProperty] public partial string TimingDetails { get; set; } = "";
    [ObservableProperty] public partial string SizeDetails { get; set; } = "";
    [ObservableProperty] public partial bool IsSuccess { get; set; }
    [ObservableProperty] public partial bool IsRedirect { get; set; }
    [ObservableProperty] public partial bool IsClientOrServerError { get; set; }
    [ObservableProperty] public partial bool IsClientError { get; set; }
    [ObservableProperty] public partial bool IsServerError { get; set; }

    /// <summary>The environment the shown response was sent with, as it was at the time; null when unknown or none.</summary>
    [ObservableProperty] public partial string? SentWithEnvironmentName { get; set; }
    [ObservableProperty] public partial EnvironmentColor SentWithEnvironmentColor { get; set; }

    // Timing bar: time to first byte vs download.
    [ObservableProperty] public partial double WaitMs { get; set; }
    [ObservableProperty] public partial double DownloadMs { get; set; }
    public string WaitText => $"Wait {WaitMs:0} ms";
    public string DownloadText => $"Download {DownloadMs:0} ms";

    private void OnSavedResponseShowRequested(object? sender, SavedResponse saved)
    {
        SetResponse(saved.Response);
        Sent.SetSavedRequest(saved.SentRequest);
        if (saved.BodyOmitted)
            RawText = PrettyText = "[Body not saved: larger than 2 MB. Send again to see it.]";
        OnPropertyChanged(nameof(DisplayText));
    }

    [ObservableProperty] public partial string PrettyText { get; set; } = "";
    [ObservableProperty] public partial string RawText { get; set; } = "";
    [ObservableProperty] public partial string Language { get; set; } = "text";
    [ObservableProperty] public partial bool IsTooLargeForPretty { get; set; }
    [ObservableProperty] public partial Bitmap? PreviewImage { get; set; }

    public List<KeyValueItem> Headers { get; private set; } = [];
    public List<ResponseCookie> Cookies { get; private set; } = [];
    public string HeadersBadge => Headers.Count > 0 ? $" ({Headers.Count})" : "";
    public string CookiesBadge => Cookies.Count > 0 ? $" ({Cookies.Count})" : "";

    public bool IsEmpty => !HasResponse && !IsLoading && Failure is null;

    // ---- the failure on screen ----

    /// <summary>Raised when the person picks a next step on the failure. The request tab handles it, since it owns the request.</summary>
    public event EventHandler<RequestFailureActionEventArgs>? FailureActionRequested;

    public bool IsPretty => ViewMode == ResponseViewMode.Pretty;
    public bool IsRaw => ViewMode == ResponseViewMode.Raw;
    public bool IsPreview => ViewMode == ResponseViewMode.Preview;
    public bool IsTextView => ViewMode != ResponseViewMode.Preview;
    public bool HasPreviewImage => PreviewImage is not null;

    /// <summary>Text shown in the body editor for the current view mode.</summary>
    public string DisplayText => ViewMode == ResponseViewMode.Pretty && !IsTooLargeForPretty ? PrettyText : RawText;

    /// <summary>
    /// Text for the Preview pane when there is no image. Empty in the other modes, so a tab switch doesn't load a large
    /// body into the preview editor that nobody is looking at.
    /// </summary>
    public string PreviewText => IsPreview && !HasPreviewImage ? RawText : "";

    /// <summary>Syntax highlighting for the current view mode; Raw is plain text like in Postman.</summary>
    public string DisplayLanguage => ViewMode == ResponseViewMode.Pretty && !IsTooLargeForPretty ? Language : "text";

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(ViewMode):
                OnPropertyChanged(nameof(IsPretty));
                OnPropertyChanged(nameof(IsRaw));
                OnPropertyChanged(nameof(IsPreview));
                OnPropertyChanged(nameof(IsTextView));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(DisplayLanguage));
                OnPropertyChanged(nameof(PreviewText));
                break;
            case nameof(RawText):
                OnPropertyChanged(nameof(PreviewText));
                break;
            case nameof(HasResponse) or nameof(IsLoading):
                OnPropertyChanged(nameof(IsEmpty));
                break;
            case nameof(Failure):
                OnPropertyChanged(nameof(IsEmpty));
                break;
            case nameof(PreviewImage):
                OnPropertyChanged(nameof(HasPreviewImage));
                OnPropertyChanged(nameof(PreviewText));
                break;
            case nameof(WaitMs) or nameof(DownloadMs):
                OnPropertyChanged(nameof(WaitText));
                OnPropertyChanged(nameof(DownloadText));
                break;
        }
    }

    [RelayCommand]
    private void SetViewMode(ResponseViewMode mode) => ViewMode = mode;

    public void BeginLoading()
    {
        IsLoading = true;
        Failure = null;
    }

    public void SetCancelled()
    {
        IsLoading = false;
    }

    public void SetFailure(RequestFailure failure)
    {
        IsLoading = false;
        HasResponse = false;
        _response = null;
        Failure = new RequestFailureViewModel(failure, _dialogs.CopyTextAsync);
        // A result is never hidden: the pane opens to show why there is none.
        Pane.Expand();
    }

    /// <param name="sentWith">The environment the request was resolved with; null for none or a saved response, which doesn't record it.</param>
    public void SetResponse(ResponseModel response, EnvironmentModel? sentWith = null)
    {
        _response = response;
        IsLoading = false;
        Failure = null;
        SentWithEnvironmentName = sentWith?.Name;
        SentWithEnvironmentColor = sentWith?.Color ?? EnvironmentColor.None;

        StatusText = $"{response.StatusCode} {response.ReasonPhrase}";
        IsSuccess = response.StatusCode is >= 200 and < 300;
        IsRedirect = response.StatusCode is >= 300 and < 400;
        IsClientOrServerError = response.StatusCode >= 400;
        IsClientError = response.StatusCode is >= 400 and < 500;
        IsServerError = response.StatusCode >= 500;
        WaitMs = response.TimeToFirstByte.TotalMilliseconds;
        DownloadMs = Math.Max(0, (response.TotalTime - response.TimeToFirstByte).TotalMilliseconds);
        TimeText = FormatDuration(response.TotalTime);
        TimingDetails = $"Time to first byte: {FormatDuration(response.TimeToFirstByte)}\nDownload: {FormatDuration(response.TotalTime - response.TimeToFirstByte)}\nTotal: {FormatDuration(response.TotalTime)}";
        SizeText = FormatSize(response.TotalSize);
        SizeDetails = $"Headers: {FormatSize(response.HeadersSize)}\nBody: {FormatSize(response.BodySize)}\n{response.HttpVersion}";

        Headers = response.Headers.ToList();
        Cookies = response.Cookies.ToList();
        OnPropertyChanged(nameof(Headers));
        OnPropertyChanged(nameof(Cookies));
        OnPropertyChanged(nameof(HeadersBadge));
        OnPropertyChanged(nameof(CookiesBadge));

        var contentType = response.ContentType ?? "";
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && !contentType.Contains("svg");
        var oldPreview = PreviewImage;
        PreviewImage = isImage ? ResponseContentDecoder.TryDecodeImage(response.Body) : null;
        oldPreview?.Dispose();

        IsTooLargeForPretty = response.BodySize > _settings.Current.MaxPrettyResponseSizeMb * 1024L * 1024L;
        RawText = isImage ? $"[{contentType} image, {FormatSize(response.BodySize)}. Use Preview to view it.]" : ResponseContentDecoder.Decode(response);
        Language = ResponseContentDecoder.DetectLanguage(contentType, RawText);
        PrettyText = IsTooLargeForPretty || isImage ? RawText : ResponseContentDecoder.Prettify(RawText, Language);

        if (isImage)
            ViewMode = ResponseViewMode.Preview;
        else if (ViewMode == ResponseViewMode.Preview)
            ViewMode = ResponseViewMode.Pretty;

        HasResponse = true;
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DisplayLanguage));
        // A result is never hidden: the pane opens when one arrives.
        Pane.Expand();
    }

    public void Clear()
    {
        _response = null;
        HasResponse = false;
        IsLoading = false;
        Failure = null;
        PrettyText = RawText = "";
        var oldPreview = PreviewImage;
        PreviewImage = null;
        oldPreview?.Dispose();
        OnPropertyChanged(nameof(DisplayText));
    }

    /// <summary>Releases the preview image; the view model is not used afterwards.</summary>
    public void Dispose()
    {
        Pane.Dispose();
        var oldPreview = PreviewImage;
        PreviewImage = null;
        oldPreview?.Dispose();
    }

    [RelayCommand]
    private void RunFailureAction(RequestFailureAction action)
    {
        if (Failure is { } failure)
            FailureActionRequested?.Invoke(this, new RequestFailureActionEventArgs(action, failure.Model));
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (HasResponse)
            await _dialogs.CopyTextAsync(DisplayText);
    }

    [RelayCommand]
    private async Task SaveToFile()
    {
        if (_response is null)
            return;
        var ext = MimeTypes.ExtensionFor(_response.ContentType);
        if (await _dialogs.SaveFileAsync("Save response", "response" + ext, ext) is { } path)
            await WriteToFileAsync(path);
    }

    /// <summary>Writes the response body to <paramref name="path"/>. When that fails the reason is shown and this returns false.</summary>
    public async Task<bool> WriteToFileAsync(string path)
    {
        if (_response is not { } response)
            return false;
        return await UserFileWriter.WriteAsync(_dialogs, "Couldn't save the response", path,
            () => File.WriteAllBytesAsync(path, response.Body));
    }

    public static string FormatDuration(TimeSpan time) =>
        time.TotalMilliseconds < 1000 ? $"{time.TotalMilliseconds:0} ms" : $"{time.TotalSeconds:0.00} s";

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.##} KB",
        _ => $"{bytes / (1024.0 * 1024):0.##} MB",
    };
}
