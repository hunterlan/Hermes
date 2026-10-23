using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Import;

public static class CurlParser
{
    /// <summary>
    /// curl options that take a value (from curl's manual). The list has to be complete enough: an unknown option is
    /// taken to have no value, so its value would be read as the URL.
    /// </summary>
    private static readonly HashSet<string> FlagsWithValue =
    [
        "-X", "--request", "-H", "--header", "-d", "--data", "--data-raw", "--data-binary", "--data-ascii",
        "--data-urlencode", "--json", "-F", "--form", "--form-string", "-u", "--user", "-A", "--user-agent",
        "-b", "--cookie", "-c", "--cookie-jar", "-e", "--referer", "--url", "--url-query", "-o", "--output",
        "-m", "--max-time", "--connect-timeout", "-x", "--proxy", "-U", "--proxy-user", "--proxy-header",
        "--preproxy", "--noproxy", "--cacert", "--capath", "-E", "--cert", "--cert-type", "--key", "--key-type",
        "--pass", "--crlfile", "--pinnedpubkey", "--ciphers", "--tls-max", "--oauth2-bearer", "--aws-sigv4",
        "--sasl-authzid", "--login-options", "-w", "--write-out", "--retry", "--retry-delay", "--retry-max-time",
        "-T", "--upload-file", "--max-redirs", "--max-filesize", "--resolve", "--connect-to", "-K", "--config",
        "-Y", "--speed-limit", "-y", "--speed-time", "--limit-rate", "-r", "--range", "-z", "--time-cond",
        "-D", "--dump-header", "-C", "--continue-at", "-Q", "--quote", "--interface", "--local-port",
        "--dns-servers", "--keepalive-time", "--expect100-timeout", "--happy-eyeballs-timeout-ms", "--trace",
        "--trace-ascii", "--stderr", "--unix-socket", "--abstract-unix-socket", "--variable", "--request-target",
        "--mail-from", "--mail-rcpt", "--netrc-file", "--hostpubmd5", "--pubkey", "-P", "--ftp-port",
    ];

    /// <summary>Data options whose value is a file name when it starts with <c>@</c> (unlike --data-raw).</summary>
    private static readonly HashSet<string> DataFlagsReadingFiles = ["-d", "--data", "--data-binary", "--data-ascii", "--json"];

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
        string? explicitUrl = null;
        var bareWords = new List<string>();
        var urlQueries = new List<string>();
        var dataParts = new List<DataPart>();
        var urlEncodedParts = new List<string>();
        var forceGet = false;
        var head = false;
        var json = false;

        foreach (var (flag, value) in Options(tokens.Skip(1).ToList()))
        {
            switch (flag)
            {
                case null:
                    bareWords.Add(value!);
                    break;
                case "-X" or "--request":
                    method = value!.ToUpperInvariant();
                    break;
                case "-H" or "--header":
                    var colon = value!.IndexOf(':');
                    if (colon > 0)
                        request.Headers.Add(new KeyValueItem(value[..colon].Trim(), value[(colon + 1)..].Trim()));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    dataParts.Add(new DataPart(value!, DataFlagsReadingFiles.Contains(flag)));
                    break;
                case "--json":
                    dataParts.Add(new DataPart(value!, ReadsFile: true));
                    json = true;
                    break;
                case "--data-urlencode":
                    urlEncodedParts.Add(value!);
                    break;
                case "-F" or "--form" or "--form-string":
                    var eq = value!.IndexOf('=');
                    var key = eq < 0 ? value : value[..eq];
                    var formValue = eq < 0 ? "" : value[(eq + 1)..];
                    var isFile = flag != "--form-string" && formValue.StartsWith('@');
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
                case "--oauth2-bearer":
                    request.Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = value! } };
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
                    explicitUrl = value;
                    break;
                case "--url-query":
                    urlQueries.Add(value!);
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
            }
        }

        // --json is --data plus JSON Content-Type and Accept headers, unless -H gives them (before or after it).
        if (json)
        {
            AddHeaderIfMissing(request, "Content-Type", "application/json");
            AddHeaderIfMissing(request, "Accept", "application/json");
        }

        // A word that looks like a URL wins over others, in case an unlisted option's value was taken for a word.
        request.Url = explicitUrl ?? bareWords.FirstOrDefault(LooksLikeUrl) ?? bareWords.FirstOrDefault() ?? "";
        if (urlQueries.Count > 0)
            request.Url += (request.Url.Contains('?') ? "&" : "?") + string.Join('&', urlQueries);
        var hasData = dataParts.Count > 0 || urlEncodedParts.Count > 0;

        if (forceGet && hasData)
        {
            var query = string.Join('&', dataParts.Select(d => d.Value).Concat(urlEncodedParts.Select(EncodeDataUrlEncode)));
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

    /// <param name="ReadsFile">The option reads a file when the value starts with <c>@</c>.</param>
    private readonly record struct DataPart(string Value, bool ReadsFile)
    {
        public string? FilePath => ReadsFile && Value.Length > 1 && Value[0] == '@' && Value != "@-" ? ExpandHome(Value[1..]) : null;
    }

    /// <summary>
    /// Splits arguments into options with their values, and bare words (flag null). Handles <c>--flag=value</c>,
    /// values attached to short flags (<c>-XPOST</c>) and combined short flags (<c>-sSL</c>, <c>-sXPOST</c>).
    /// </summary>
    private static IEnumerable<(string? Flag, string? Value)> Options(List<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Next() => i + 1 < args.Count ? args[++i] : null;

            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
            {
                var eq = arg.IndexOf('=');
                if (eq > 0 && FlagsWithValue.Contains(arg[..eq]))
                    yield return (arg[..eq], arg[(eq + 1)..]);
                else if (FlagsWithValue.Contains(arg))
                {
                    if (Next() is { } value)
                        yield return (arg, value);
                }
                else
                    yield return (arg, null);
            }
            else if (arg.Length > 1 && arg[0] == '-')
            {
                for (var j = 1; j < arg.Length; j++)
                {
                    var flag = "-" + arg[j];
                    if (!FlagsWithValue.Contains(flag))
                    {
                        yield return (flag, null);
                        continue;
                    }
                    var value = j + 1 < arg.Length ? arg[(j + 1)..] : Next();
                    if (value is not null)
                        yield return (flag, value);
                    break;
                }
            }
            else
            {
                yield return (null, arg);
            }
        }
    }

    private static bool LooksLikeUrl(string word) =>
        word.Contains("://", StringComparison.Ordinal) || word.StartsWith("{{", StringComparison.Ordinal) ||
        word.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || word.Contains('.') || word.Contains('/');

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;

    private static void AddHeaderIfMissing(HttpRequestModel request, string name, string value)
    {
        if (!request.Headers.Any(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
            request.Headers.Add(new KeyValueItem(name, value));
    }

    private static void ApplyData(HttpRequestModel request, List<DataPart> dataParts, List<string> urlEncodedParts)
    {
        // A body read from a file (-d @payload.json) is sent from that file, which is read when the request is sent.
        // Several parts can't be expressed that way and stay text, as written.
        if (dataParts is [{ FilePath: { } path }] && urlEncodedParts.Count == 0)
        {
            request.Body.Mode = BodyMode.Binary;
            request.Body.BinaryFilePath = path;
            return;
        }

        var contentType = request.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
        var isForm = contentType is null || contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
        var joined = string.Join('&', dataParts.Select(d => d.Value).Concat(urlEncodedParts.Select(EncodeDataUrlEncode)));

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
