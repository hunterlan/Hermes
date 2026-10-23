using System.Text;
using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>Renders a prepared request as the HTTP/1.1 text that goes on the wire, for the "Sent request" view.</summary>
public static class RawRequestFormatter
{
    public static string Format(PreparedRequest request)
    {
        var sb = new StringBuilder();
        var hasUri = Uri.TryCreate(request.Url, UriKind.Absolute, out var uri);
        sb.Append(request.Method).Append(' ').Append(hasUri ? uri!.PathAndQuery : request.Url).Append(" HTTP/1.1\n");

        if (hasUri && request.GetHeader("Host") is null)
            sb.Append("Host: ").Append(uri!.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}").Append('\n');
        foreach (var header in request.Headers)
            sb.Append(header.Key).Append(": ").Append(header.Value).Append('\n');

        var body = Body(request);
        if (body.Length > 0)
            sb.Append('\n').Append(body);
        return sb.ToString();
    }

    private static string Body(PreparedRequest request) => request.BodyMode switch
    {
        BodyMode.Raw => request.RawBody,
        BodyMode.UrlEncoded => string.Join("&", request.FormFields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}")),
        BodyMode.FormData => string.Join("\n", request.FormFields.Select(f =>
            f.Kind == KeyValueKind.File ? $"--boundary\n{f.Key}: <file {Path.GetFileName(f.Value)}>" : $"--boundary\n{f.Key}: {f.Value}")),
        BodyMode.Binary => request.BinaryFilePath is { } path ? $"<binary file {Path.GetFileName(path)}>" : "",
        _ => "",
    };
}
