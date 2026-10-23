using System.Net.Http.Headers;
using System.Text;
using Hermes.Core.Models;

namespace Hermes.Core.Http;

public static class HttpMessageFactory
{
    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Length", "Content-Disposition", "Content-Encoding", "Content-Language",
        "Content-Location", "Content-MD5", "Content-Range", "Expires", "Last-Modified", "Allow",
    };

    /// <exception cref="UriFormatException">The URL is invalid.</exception>
    /// <exception cref="IOException">A file referenced by the body couldn't be read.</exception>
    public static HttpRequestMessage Create(PreparedRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new UriFormatException($"Invalid URL: {request.Url}");

        var message = new HttpRequestMessage(new HttpMethod(request.Method), uri)
        {
            Content = CreateContent(request),
        };

        foreach (var header in request.Headers)
        {
            if (ContentHeaderNames.Contains(header.Key))
            {
                // Length is computed from the actual content.
                if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;
                message.Content ??= new ByteArrayContent([]);
                message.Content.Headers.Remove(header.Key);
                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        return message;
    }

    private static HttpContent? CreateContent(PreparedRequest request)
    {
        switch (request.BodyMode)
        {
            case BodyMode.Raw:
                return new ByteArrayContent(Encoding.UTF8.GetBytes(request.RawBody));

            case BodyMode.UrlEncoded:
                var encoded = string.Join('&', request.FormFields.Select(f =>
                    Uri.EscapeDataString(f.Key) + "=" + Uri.EscapeDataString(f.Value)));
                return new ByteArrayContent(Encoding.UTF8.GetBytes(encoded));

            case BodyMode.FormData:
                var multipart = new MultipartFormDataContent();
                try
                {
                    foreach (var field in request.FormFields)
                    {
                        if (field.Kind == KeyValueKind.File)
                        {
                            if (field.Value.Length == 0)
                                continue;
                            var file = new StreamContent(File.OpenRead(field.Value));
                            file.Headers.ContentType = new MediaTypeHeaderValue(MimeTypes.FromFileName(field.Value));
                            multipart.Add(file, Quote(field.Key), Quote(Path.GetFileName(field.Value)));
                        }
                        else
                        {
                            multipart.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(field.Value)), Quote(field.Key));
                        }
                    }
                }
                catch
                {
                    // Closes the files already opened; otherwise they stay locked until the GC gets to them.
                    multipart.Dispose();
                    throw;
                }
                return multipart;

            case BodyMode.Binary:
                if (string.IsNullOrEmpty(request.BinaryFilePath))
                    return null;
                return new StreamContent(File.OpenRead(request.BinaryFilePath));

            default:
                return null;
        }
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
