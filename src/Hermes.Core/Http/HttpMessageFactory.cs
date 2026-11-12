using System.Net.Http.Headers;
using System.Text;
using Hermes.Core.Models;
using Hermes.Core.Web;

namespace Hermes.Core.Http;

public static class HttpMessageFactory
{
    private const char MaxAscii = '\u007f';

    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Length", "Content-Disposition", "Content-Encoding", "Content-Language",
        "Content-Location", "Content-MD5", "Content-Range", "Expires", "Last-Modified", "Allow",
    };

    /// <exception cref="UriFormatException">The URL is invalid.</exception>
    /// <exception cref="BodyFileException">A file referenced by the body couldn't be read.</exception>
    /// <exception cref="InvalidHeaderException">A header's name or value can't be sent.</exception>
    public static HttpRequestMessage Create(PreparedRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new UriFormatException($"Invalid URL: {request.Url}");

        var message = new HttpRequestMessage(new HttpMethod(request.Method), uri)
        {
            Content = CreateContent(request),
        };

        try
        {
            foreach (var header in request.Headers)
            {
                // A row whose name is empty (a variable that resolved to nothing) is left out, as it always was.
                if (header.Key.Length == 0)
                    continue;
                if (!IsHeaderName(header.Key))
                    throw new InvalidHeaderException(header.Key, header.Value, InvalidHeaderProblem.Name);
                // A line break in a value would end the header early and start another one.
                if (header.Value.Any(character => char.IsControl(character) && character != '\t'))
                    throw new InvalidHeaderException(header.Key, header.Value, InvalidHeaderProblem.Value);
                if (header.Value.Any(character => character > MaxAscii))
                    throw new InvalidHeaderException(header.Key, header.Value, InvalidHeaderProblem.NonAsciiValue);

                bool added;
                if (ContentHeaderNames.Contains(header.Key))
                {
                    // Length is computed from the actual content.
                    if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                        continue;
                    message.Content ??= new ByteArrayContent([]);
                    message.Content.Headers.Remove(header.Key);
                    added = message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                else
                {
                    added = message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                if (!added)
                    throw new InvalidHeaderException(header.Key, header.Value, InvalidHeaderProblem.Value);
            }
        }
        catch
        {
            // Closes the body's files; otherwise they stay locked until the GC gets to them.
            message.Dispose();
            throw;
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
                            var file = new StreamContent(OpenBodyFile(field.Value));
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
                return new StreamContent(OpenBodyFile(request.BinaryFilePath));

            default:
                return null;
        }
    }

    private static FileStream OpenBodyFile(string path)
    {
        try
        {
            return File.OpenRead(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new BodyFileException(path, ex);
        }
    }

    /// <summary>A header name is a token: letters, digits and a few symbols, no spaces or colons.</summary>
    private static bool IsHeaderName(string name) =>
        name.All(character => char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
