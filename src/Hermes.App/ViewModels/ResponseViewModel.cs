using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Formatting;
using Hermes.Core.Headers;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Hermes.Core.Web;

namespace Hermes.App.ViewModels;

public sealed partial class ResponseViewModel(DialogService dialogs, SettingsService settings) : ObservableObject
{
    private const string SentSection = "sent";

    private readonly HeaderRevealState _revealState = new(dialogs.CopyTextAsync);
    private ResponseModel? _response;

    private static readonly string[] FailureViewProperties =
    [
        nameof(HasFailure), nameof(FailureHeadline), nameof(FailureFacts), nameof(FailureNothingWasSent), nameof(FailureDetails),
        nameof(FailureOriginalMessage), nameof(HasFailureDetails), nameof(HasFailureOriginalMessage), nameof(CanTurnOffVerification),
        nameof(CanOpenRequestSettings), nameof(HasFailureActions), nameof(FailureActionCaption), nameof(FailureIconKey),
    ];

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasResponse { get; set; }

    /// <summary>Why there is no response; null while there is one, or none was asked for yet.</summary>
    [ObservableProperty] public partial RequestFailure? Failure { get; set; }

    /// <summary>Whether the failure's Details are open; they start closed for each new failure.</summary>
    [ObservableProperty] public partial bool FailureDetailsExpanded { get; set; }

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

    /// <summary>The first line of the request that went out, e.g. "GET /users HTTP/1.1", shown in the "Sent request" tab.</summary>
    [ObservableProperty] public partial string SentRequestLine { get; set; } = "";

    /// <summary>The headers that went out, sensitive ones masked until revealed.</summary>
    [ObservableProperty] public partial List<HeaderRowViewModel> SentHeaderRows { get; set; } = [];

    /// <summary>The body that went out; empty for none.</summary>
    [ObservableProperty] public partial string SentBodyText { get; set; } = "";

    public bool HasSentBody => SentBodyText.Length > 0;

    // Timing bar: time to first byte vs download.
    [ObservableProperty] public partial double WaitMs { get; set; }
    [ObservableProperty] public partial double DownloadMs { get; set; }
    public string WaitText => $"Wait {WaitMs:0} ms";
    public string DownloadText => $"Download {DownloadMs:0} ms";

    public bool IsSideBySide => settings.Current.Layout == ResponseLayout.Horizontal;

    // ---- responses saved with the request ----

    public ObservableCollection<SavedResponseItem> SavedResponses { get; } = [];

    /// <summary>Set while an earlier saved response (not the latest) is on screen.</summary>
    [ObservableProperty] public partial SavedResponseItem? ViewingEarlier { get; set; }

    public bool HasSavedResponses => SavedResponses.Count > 0;
    public string SavedResponsesLabel => SavedResponses.Count switch
    {
        0 => "",
        1 => "Saved",
        var n => $"Saved · {n - 1} earlier {(n == 2 ? "response" : "responses")}",
    };

    /// <summary>Replaces the saved list; with <paramref name="show"/> the newest one is displayed (e.g. when a tab opens).</summary>
    public void SetSaved(IReadOnlyList<SavedResponse> saved, bool show)
    {
        SavedResponses.Clear();
        for (var i = 0; i < saved.Count; i++)
            SavedResponses.Add(new SavedResponseItem(saved[i], i == 0));
        OnPropertyChanged(nameof(HasSavedResponses));
        OnPropertyChanged(nameof(SavedResponsesLabel));
        ViewingEarlier = null;
        if (show && saved.Count > 0)
            Show(saved[0]);
    }

    /// <summary>Puts a just-saved response in front of the list, keeping as many as the workspace stores.</summary>
    public void AddSaved(SavedResponse saved) =>
        SetSaved([saved, .. SavedResponses.Select(i => i.Saved).Take(WorkspaceRepository.MaxSavedResponses - 1)], show: false);

    [RelayCommand]
    private void ShowSaved(SavedResponseItem item)
    {
        Show(item.Saved);
        ViewingEarlier = item.IsLatest ? null : item;
    }

    private void Show(SavedResponse saved)
    {
        SetResponse(saved.Response);
        // A saved request is only text, already masked where it was saved: its rows show that text.
        var parsed = RawRequestParser.Parse(saved.SentRequest);
        ShowSentRequest(parsed.RequestLine, parsed.Headers.Select(header =>
            CreateSentRow(header.Name, header.Value, isAuthHeader: false)), parsed.Body);
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

    /// <summary>What the request tab does for "Turn off verification for this request"; set by the tab, which owns the request.</summary>
    public Func<Task>? TurnOffVerificationHandler { get; set; }

    /// <summary>What the request tab does for "Open request settings".</summary>
    public Action? OpenRequestSettingsHandler { get; set; }

    public bool HasFailure => Failure is not null;
    public string FailureHeadline => Failure?.Headline ?? "";
    public string FailureFacts => Failure?.Facts ?? "";
    public bool FailureNothingWasSent => Failure?.NothingWasSent ?? false;
    public IReadOnlyList<FailureDetail> FailureDetails => Failure?.Details ?? [];
    public string? FailureOriginalMessage => Failure?.OriginalMessage;
    public bool HasFailureDetails => Failure?.HasDetails ?? false;
    public bool HasFailureOriginalMessage => FailureOriginalMessage is not null;
    public bool CanTurnOffVerification => Failure?.Actions.Contains(RequestFailureAction.TurnOffSslVerification) ?? false;
    public bool CanOpenRequestSettings => Failure?.Actions.Contains(RequestFailureAction.OpenRequestSettings) ?? false;
    public bool HasFailureActions => Failure is { Actions.Count: > 0 };
    public string? FailureActionCaption => Failure?.ActionCaption;

    /// <summary>The resource key of the icon for the failure's kind.</summary>
    public string FailureIconKey => Failure?.Kind switch
    {
        RequestFailureKind.SecureConnectionFailed or RequestFailureKind.PlainHttpAnswered or RequestFailureKind.CertificateExpired or RequestFailureKind.CertificateSelfSigned
            or RequestFailureKind.CertificateUntrustedAuthority or RequestFailureKind.CertificateNameMismatch => "IconLock",
        _ => "IconAlert",
    };
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
                foreach (var name in FailureViewProperties)
                    OnPropertyChanged(name);
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

    [RelayCommand]
    private void SetLayout(ResponseLayout layout)
    {
        settings.Current.Layout = layout;
        settings.Update(settings.Current);
        OnPropertyChanged(nameof(IsSideBySide));
    }

    /// <summary>Shows the request as it goes out, with its real values so they can be revealed and copied.</summary>
    public void SetSentRequest(PreparedRequest request)
    {
        // The text gives the request line and the body, in the same form as the wire text and the saved copy.
        var parsed = RawRequestParser.Parse(RawRequestFormatter.Format(request));
        var rows = new List<HeaderRowViewModel>();
        // Host is added when the request has none of its own, as the formatter does.
        if (request.GetHeader("Host") is null && parsed.Headers.FirstOrDefault(header => header.Name == "Host") is { } host)
            rows.Add(CreateSentRow(host.Name, host.Value, isAuthHeader: false));
        rows.AddRange(request.Headers.Select(header =>
            CreateSentRow(header.Key, header.Value, request.OriginOf(header) == PreparedHeaderOrigin.Auth)));
        ShowSentRequest(parsed.RequestLine, rows, parsed.Body);
    }

    /// <summary>Masks the revealed values of the Sent request again, for when the user leaves the tab.</summary>
    public void HideRevealedValues()
    {
        _revealState.Clear();
        foreach (var row in SentHeaderRows)
            row.Sensitive.Hide();
    }

    private HeaderRowViewModel CreateSentRow(string name, string value, bool isAuthHeader) => new(name, value, "")
    {
        // Auth headers are credentials whatever their name (an API key may use any header).
        Sensitive = _revealState.CreateValue(SentSection, name, value, isAuthHeader || SensitiveHeaders.IsSensitiveName(name)),
    };

    private void ShowSentRequest(string requestLine, IEnumerable<HeaderRowViewModel> headerRows, string body)
    {
        SentRequestLine = requestLine;
        SentHeaderRows = headerRows.ToList();
        SentBodyText = body;
        OnPropertyChanged(nameof(HasSentBody));
    }

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
        FailureDetailsExpanded = false;
        Failure = failure;
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
        PreviewImage = isImage ? TryDecodeImage(response.Body) : null;
        oldPreview?.Dispose();

        IsTooLargeForPretty = response.BodySize > settings.Current.MaxPrettyResponseSizeMb * 1024L * 1024L;
        RawText = isImage ? $"[{contentType} image, {FormatSize(response.BodySize)}. Use Preview to view it.]" : Decode(response);
        Language = DetectLanguage(contentType, RawText);
        PrettyText = IsTooLargeForPretty || isImage ? RawText : Prettify(RawText, Language);

        if (isImage)
            ViewMode = ResponseViewMode.Preview;
        else if (ViewMode == ResponseViewMode.Preview)
            ViewMode = ResponseViewMode.Pretty;

        HasResponse = true;
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(DisplayLanguage));
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

    [RelayCommand]
    private async Task TurnOffVerification()
    {
        if (TurnOffVerificationHandler is { } handler)
            await handler();
    }

    [RelayCommand]
    private void OpenRequestSettings() => OpenRequestSettingsHandler?.Invoke();

    [RelayCommand]
    private async Task CopyFailureDetails()
    {
        if (Failure is { } failure)
            await dialogs.CopyTextAsync(failure.ToClipboardText());
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (HasResponse)
            await dialogs.CopyTextAsync(DisplayText);
    }

    [RelayCommand]
    private async Task SaveToFile()
    {
        if (_response is null)
            return;
        var ext = MimeTypes.ExtensionFor(_response.ContentType);
        if (await dialogs.SaveFileAsync("Save response", "response" + ext, ext) is { } path)
            await File.WriteAllBytesAsync(path, _response.Body);
    }

    private static string Decode(ResponseModel response)
    {
        Encoding encoding = Encoding.UTF8;
        if (response.Charset is { Length: > 0 } charset)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
            }
        }
        return encoding.GetString(response.Body);
    }

    internal static string DetectLanguage(string contentType, string body)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return "json";
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return "html";
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            return "xml";
        if (contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase))
            return "javascript";
        if (contentType.Contains("css", StringComparison.OrdinalIgnoreCase))
            return "css";

        // Servers often mislabel; sniff the body.
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            return "json";
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            return "html";
        if (trimmed.StartsWith("<"))
            return "xml";
        return "text";
    }

    private static string Prettify(string text, string language)
    {
        switch (language)
        {
            case "json":
                return JsonFormatter.TryFormat(text, out var json) ? json : text;
            case "xml":
                return XmlFormatter.TryFormat(text, out var xml) ? xml : text;
            default:
                return text;
        }
    }

    private static Bitmap? TryDecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
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
