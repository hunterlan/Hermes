using System.Text;
using Avalonia.Media.Imaging;
using Hermes.Core.Formatting;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Turns a response body into what the pane shows: text, its language, a prettified form or an image.</summary>
public static class ResponseContentDecoder
{
    /// <summary>The body as text, in the response's charset; UTF-8 when it names none or one that is unknown.</summary>
    public static string Decode(ResponseModel response)
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

    public static string DetectLanguage(string contentType, string body)
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

    public static string Prettify(string text, string language)
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

    /// <summary>The image the bytes make, or null when they are not one the app can read.</summary>
    public static Bitmap? TryDecodeImage(byte[] bytes)
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
}
