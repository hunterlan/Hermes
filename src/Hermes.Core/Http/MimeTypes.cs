namespace Hermes.Core.Http;

public static class MimeTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".js"] = "application/javascript",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
    };

    public static string FromFileName(string fileName) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");

    /// <summary>A sensible file extension for saving a response of the given content type.</summary>
    public static string ExtensionFor(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return ".txt";
        foreach (var (ext, type) in ByExtension)
            if (contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase))
                return ext;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return ".json";
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            return ".xml";
        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? ".txt" : ".bin";
    }
}
