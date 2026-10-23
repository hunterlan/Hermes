using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Formatting;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public enum ResponseViewMode
{
    Pretty,
    Raw,
    Preview,
}

public sealed partial class ResponseViewModel(DialogService dialogs, SettingsService settings) : ObservableObject
{
    private ResponseModel? _response;

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasResponse { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }
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

    /// <summary>The exact request that went out (after variables and auth), shown in the "Sent request" tab.</summary>
    [ObservableProperty] public partial string SentRequestText { get; set; } = "";

    // Timing bar: time to first byte vs download.
    [ObservableProperty] public partial double WaitMs { get; set; }
    [ObservableProperty] public partial double DownloadMs { get; set; }
    public string WaitText => $"Wait {WaitMs:0} ms";
    public string DownloadText => $"Download {DownloadMs:0} ms";

    public bool IsSideBySide => settings.Current.Layout == ResponseLayout.Horizontal;

    [ObservableProperty] public partial string PrettyText { get; set; } = "";
    [ObservableProperty] public partial string RawText { get; set; } = "";
    [ObservableProperty] public partial string Language { get; set; } = "text";
    [ObservableProperty] public partial bool IsTooLargeForPretty { get; set; }
    [ObservableProperty] public partial Bitmap? PreviewImage { get; set; }

    public List<KeyValueItem> Headers { get; private set; } = [];
    public List<ResponseCookie> Cookies { get; private set; } = [];
    public string HeadersBadge => Headers.Count > 0 ? $" ({Headers.Count})" : "";
    public string CookiesBadge => Cookies.Count > 0 ? $" ({Cookies.Count})" : "";

    public bool IsEmpty => !HasResponse && !IsLoading && Error is null;
    public bool IsPretty => ViewMode == ResponseViewMode.Pretty;
    public bool IsRaw => ViewMode == ResponseViewMode.Raw;
    public bool IsPreview => ViewMode == ResponseViewMode.Preview;
    public bool IsTextView => ViewMode != ResponseViewMode.Preview;
    public bool HasPreviewImage => PreviewImage is not null;

    /// <summary>Text shown in the body editor for the current view mode.</summary>
    public string DisplayText => ViewMode == ResponseViewMode.Pretty && !IsTooLargeForPretty ? PrettyText : RawText;

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
                break;
            case nameof(HasResponse) or nameof(IsLoading) or nameof(Error):
                OnPropertyChanged(nameof(IsEmpty));
                break;
            case nameof(PreviewImage):
                OnPropertyChanged(nameof(HasPreviewImage));
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

    public void SetSentRequest(PreparedRequest request) => SentRequestText = RawRequestFormatter.Format(request);

    public void BeginLoading()
    {
        IsLoading = true;
        Error = null;
    }

    public void SetCancelled()
    {
        IsLoading = false;
    }

    public void SetError(string error)
    {
        IsLoading = false;
        HasResponse = false;
        _response = null;
        Error = error;
    }

    public void SetResponse(ResponseModel response)
    {
        _response = response;
        IsLoading = false;
        Error = null;

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
        PreviewImage?.Dispose();
        PreviewImage = isImage ? TryDecodeImage(response.Body) : null;

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
        Error = null;
        PrettyText = RawText = "";
        PreviewImage = null;
        OnPropertyChanged(nameof(DisplayText));
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
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(text);
                    return BodyFormatter.FormatJson(text);
                }
                catch (System.Text.Json.JsonException)
                {
                    return text;
                }
            case "xml":
                return BodyFormatter.TryFormatXml(text, out var xml) ? xml : text;
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
