using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Import;

public static class CurlGenerator
{
    public static string Generate(PreparedRequest request)
    {
        var sb = new StringBuilder("curl");
        if (request.FollowRedirects)
            sb.Append(" --location");
        if (!request.VerifySsl)
            sb.Append(" --insecure");

        var defaultMethod = request.BodyMode == BodyMode.None || request.BodyMode == BodyMode.Binary && request.BinaryFilePath is null
            ? "GET"
            : "POST";
        if (request.Method != defaultMethod)
            sb.Append(" --request ").Append(request.Method);

        sb.Append(' ').Append(Quote(request.Url));

        foreach (var h in request.Headers)
            sb.Append(" \\\n--header ").Append(Quote($"{h.Key}: {h.Value}"));

        switch (request.BodyMode)
        {
            case BodyMode.Raw when request.RawBody.Length > 0:
                sb.Append(" \\\n--data-raw ").Append(Quote(request.RawBody));
                break;
            case BodyMode.UrlEncoded:
                foreach (var f in request.FormFields)
                    sb.Append(" \\\n--data-urlencode ").Append(Quote($"{f.Key}={f.Value}"));
                break;
            case BodyMode.FormData:
                foreach (var f in request.FormFields)
                    sb.Append(" \\\n--form ").Append(Quote(f.Kind == KeyValueKind.File ? $"{f.Key}=@\"{f.Value}\"" : $"{f.Key}=\"{f.Value}\""));
                break;
            case BodyMode.Binary when request.BinaryFilePath is not null:
                sb.Append(" \\\n--data-binary ").Append(Quote("@" + request.BinaryFilePath));
                break;
        }
        return sb.ToString();
    }

    /// <summary>Single-quotes for POSIX shells; embedded quotes become <c>'\''</c>.</summary>
    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
