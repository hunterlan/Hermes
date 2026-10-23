using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Import;

public static class CurlGenerator
{
    public static string Generate(PreparedRequest request)
    {
        var data = DataFlags(request);

        var sb = new StringBuilder("curl");
        if (request.FollowRedirects)
            sb.Append(" --location");
        if (!request.VerifySsl)
            sb.Append(" --insecure");

        // curl sends POST when a data flag is present and GET otherwise, whatever the body mode.
        if (request.Method != (data.Count > 0 ? "POST" : "GET"))
            sb.Append(" --request ").Append(request.Method);

        sb.Append(' ').Append(Quote(request.Url));

        foreach (var h in request.Headers)
            sb.Append(" \\\n--header ").Append(Quote($"{h.Key}: {h.Value}"));
        foreach (var flag in data)
            sb.Append(" \\\n").Append(flag);
        return sb.ToString();
    }

    private static List<string> DataFlags(PreparedRequest request)
    {
        var flags = new List<string>();
        switch (request.BodyMode)
        {
            case BodyMode.Raw when request.RawBody.Length > 0:
                flags.Add("--data-raw " + Quote(request.RawBody));
                break;
            case BodyMode.UrlEncoded:
                flags.AddRange(request.FormFields.Select(f => "--data-urlencode " + Quote($"{f.Key}={f.Value}")));
                break;
            case BodyMode.FormData:
                flags.AddRange(request.FormFields
                    .Where(f => f.Kind != KeyValueKind.File || f.Value.Length > 0)
                    .Select(f => "--form " + Quote(f.Kind == KeyValueKind.File ? $"{f.Key}=@\"{f.Value}\"" : $"{f.Key}=\"{f.Value}\"")));
                break;
            case BodyMode.Binary when !string.IsNullOrEmpty(request.BinaryFilePath):
                flags.Add("--data-binary " + Quote("@" + request.BinaryFilePath));
                break;
        }
        return flags;
    }

    /// <summary>Single-quotes for POSIX shells; embedded quotes become <c>'\''</c>.</summary>
    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
