using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Import;

public static class CurlParser
{
    private static readonly HashSet<string> FlagsWithValue =
    [
        "-X", "--request", "-H", "--header", "-d", "--data", "--data-raw", "--data-binary", "--data-ascii",
        "--data-urlencode", "-F", "--form", "--form-string", "-u", "--user", "-A", "--user-agent", "-b", "--cookie",
        "-e", "--referer", "--url", "-o", "--output", "-m", "--max-time", "--connect-timeout", "-x", "--proxy",
        "--cacert", "--cert", "--key", "-w", "--write-out", "--retry", "-T", "--upload-file", "-c", "--cookie-jar",
        "--max-redirs", "--resolve", "-K", "--config",
    ];

    public static bool LooksLikeCurl(string text) =>
        text.TrimStart().StartsWith("curl ", StringComparison.OrdinalIgnoreCase) ||
        text.TrimStart().StartsWith("curl.exe ", StringComparison.OrdinalIgnoreCase);

    public static HttpRequestModel Parse(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0 || !tokens[0].StartsWith("curl", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Not a cURL command.");

        var request = new HttpRequestModel();
        string? method = null;
        string? url = null;
        var dataParts = new List<string>();
        var urlEncodedParts = new List<string>();
        var forceGet = false;
        var head = false;

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            string? value = null;

            // --flag=value
            if (token.StartsWith("--", StringComparison.Ordinal) && token.Contains('='))
            {
                var eq = token.IndexOf('=');
                if (FlagsWithValue.Contains(token[..eq]))
                {
                    value = token[(eq + 1)..];
                    token = token[..eq];
                }
            }
            // -XPOST, -H'...' (short flag with attached value)
            else if (token.Length > 2 && token[0] == '-' && token[1] != '-' && FlagsWithValue.Contains(token[..2]))
            {
                value = token[2..];
                token = token[..2];
            }

            if (FlagsWithValue.Contains(token) && value is null)
            {
                if (i + 1 >= tokens.Count)
                    break;
                value = tokens[++i];
            }

            switch (token)
            {
                case "-X" or "--request":
                    method = value!.ToUpperInvariant();
                    break;
                case "-H" or "--header":
                    var colon = value!.IndexOf(':');
                    if (colon > 0)
                        request.Headers.Add(new KeyValueItem(value[..colon].Trim(), value[(colon + 1)..].Trim()));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    dataParts.Add(value!);
                    break;
                case "--data-urlencode":
                    urlEncodedParts.Add(value!);
                    break;
                case "-F" or "--form" or "--form-string":
                    var eq = value!.IndexOf('=');
                    var key = eq < 0 ? value : value[..eq];
                    var formValue = eq < 0 ? "" : value[(eq + 1)..];
                    var isFile = token != "--form-string" && formValue.StartsWith('@');
                    if (isFile)
                        formValue = formValue[1..].Split(';')[0];
                    request.Body.Mode = BodyMode.FormData;
                    request.Body.FormData.Add(new KeyValueItem(key, formValue) { Kind = isFile ? KeyValueKind.File : KeyValueKind.Text });
                    break;
                case "-u" or "--user":
                    var sep = value!.IndexOf(':');
                    request.Auth = new AuthConfig
                    {
                        Type = AuthType.Basic,
                        Basic = new BasicAuth
                        {
                            Username = sep < 0 ? value : value[..sep],
                            Password = sep < 0 ? "" : value[(sep + 1)..],
                        },
                    };
                    break;
                case "-A" or "--user-agent":
                    request.Headers.Add(new KeyValueItem("User-Agent", value!));
                    break;
                case "-b" or "--cookie":
                    request.Headers.Add(new KeyValueItem("Cookie", value!));
                    break;
                case "-e" or "--referer":
                    request.Headers.Add(new KeyValueItem("Referer", value!));
                    break;
                case "--url":
                    url = value;
                    break;
                case "-G" or "--get":
                    forceGet = true;
                    break;
                case "-I" or "--head":
                    head = true;
                    break;
                case "-k" or "--insecure":
                    request.Settings.VerifySsl = false;
                    break;
                case "-L" or "--location":
                    request.Settings.FollowRedirects = true;
                    break;
                default:
                    if (!token.StartsWith('-') && url is null)
                        url = token;
                    break;
            }
        }

        request.Url = url ?? "";
        var hasData = dataParts.Count > 0 || urlEncodedParts.Count > 0;

        if (forceGet && hasData)
        {
            var query = string.Join('&', dataParts.Concat(urlEncodedParts.Select(EncodeDataUrlEncode)));
            request.Url += (request.Url.Contains('?') ? "&" : "?") + query;
            hasData = false;
        }
        else if (hasData)
        {
            ApplyData(request, dataParts, urlEncodedParts);
        }

        request.Method = method ?? (head ? "HEAD" : hasData || request.Body.Mode == BodyMode.FormData ? "POST" : "GET");
        request.QueryParams = UrlUtilities.ParseQuery(UrlUtilities.Split(request.Url).Query);
        request.PathVariables = UrlUtilities.ParsePathVariables(request.Url).Select(n => new KeyValueItem(n, "")).ToList();
        request.Name = DeriveName(request);
        return request;
    }

    private static void ApplyData(HttpRequestModel request, List<string> dataParts, List<string> urlEncodedParts)
    {
        var contentType = request.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
        var isForm = contentType is null || contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
        var joined = string.Join('&', dataParts.Concat(urlEncodedParts.Select(EncodeDataUrlEncode)));

        if (isForm && !LooksLikeJson(joined))
        {
            request.Body.Mode = BodyMode.UrlEncoded;
            foreach (var pair in joined.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair[..eq];
                var value = eq < 0 ? "" : pair[(eq + 1)..];
                request.Body.UrlEncoded.Add(new KeyValueItem(Unescape(key), Unescape(value)));
            }
            // Hermes adds it automatically for urlencoded bodies.
            request.Headers.RemoveAll(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            return;
        }

        request.Body.Mode = BodyMode.Raw;
        request.Body.Raw = joined;
        request.Body.RawLanguage =
            contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true || (contentType is null && LooksLikeJson(joined)) ? RawLanguage.Json
            : contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ? RawLanguage.Xml
            : contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true ? RawLanguage.Html
            : contentType?.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true ? RawLanguage.JavaScript
            : RawLanguage.Text;
    }

    private static bool LooksLikeJson(string s)
    {
        var t = s.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[');
    }

    private static string Unescape(string s)
    {
        try
        {
            return Uri.UnescapeDataString(s.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return s;
        }
    }

    /// <summary>--data-urlencode "name=value" encodes only the value part.</summary>
    private static string EncodeDataUrlEncode(string s)
    {
        var eq = s.IndexOf('=');
        return eq < 0 ? Uri.EscapeDataString(s) : s[..eq] + "=" + Uri.EscapeDataString(s[(eq + 1)..]);
    }

    private static string DeriveName(HttpRequestModel request)
    {
        var baseUrl = UrlUtilities.Split(request.Url).Base;
        return baseUrl.Length == 0 ? "Imported request" : baseUrl;
    }

    /// <summary>
    /// POSIX-shell-like tokenizer: single quotes, double quotes, backslash escapes, and line continuations
    /// (<c>\</c>, <c>^</c> for cmd.exe and <c>`</c> for PowerShell).
    /// </summary>
    internal static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        var i = 0;

        while (i < input.Length)
        {
            var c = input[i];

            if (c is '\\' or '^' or '`' && i + 1 < input.Length && input[i + 1] is '\n' or '\r')
            {
                i++;
                while (i < input.Length && input[i] is '\r' or '\n')
                    i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
                i++;
                continue;
            }

            inToken = true;
            switch (c)
            {
                case '\'':
                    i++;
                    while (i < input.Length && input[i] != '\'')
                        current.Append(input[i++]);
                    i++;
                    break;

                case '$' when i + 1 < input.Length && input[i + 1] == '\'':
                    // Bash ANSI-C quoting: $'...'
                    i += 2;
                    while (i < input.Length && input[i] != '\'')
                    {
                        if (input[i] == '\\' && i + 1 < input.Length)
                        {
                            i++;
                            current.Append(input[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var x => x });
                        }
                        else
                        {
                            current.Append(input[i]);
                        }
                        i++;
                    }
                    i++;
                    break;

                case '"':
                    i++;
                    while (i < input.Length && input[i] != '"')
                    {
                        if (input[i] == '\\' && i + 1 < input.Length && input[i + 1] is '"' or '\\' or '$' or '`')
                            i++;
                        current.Append(input[i++]);
                    }
                    i++;
                    break;

                case '\\' when i + 1 < input.Length:
                    current.Append(input[i + 1]);
                    i += 2;
                    break;

                default:
                    current.Append(c);
                    i++;
                    break;
            }
        }
        if (inToken)
            tokens.Add(current.ToString());
        return tokens;
    }
}
