using System.Text;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Web;

namespace Hermes.Core.Import;

public static class CurlParser
{
    /// <summary>
    /// curl options that take a value (from <c>curl --help all</c>). The list has to be complete: an option missing
    /// here would be taken to have no value, and its value would be read as the URL.
    /// </summary>
    private static readonly HashSet<string> FlagsWithValue =
    [
        "--abstract-unix-socket", "--alt-svc", "--aws-sigv4", "--cacert", "--capath", "-E", "--cert",
        "--cert-type", "--ciphers", "-K", "--config", "--connect-timeout", "--connect-to", "-C",
        "--continue-at", "-b", "--cookie", "-c", "--cookie-jar", "--create-file-mode", "--crlfile", "--curves",
        "-d", "--data", "--data-ascii", "--data-binary", "--data-raw", "--data-urlencode", "--delegation",
        "--dns-interface", "--dns-ipv4-addr", "--dns-ipv6-addr", "--dns-servers", "--doh-url", "-D",
        "--dump-header", "--egd-file", "--engine", "--etag-compare", "--etag-save", "--expect100-timeout", "-F",
        "--form", "--form-string", "--ftp-account", "--ftp-alternative-to-user", "--ftp-method", "-P",
        "--ftp-port", "--ftp-ssl-ccc-mode", "--happy-eyeballs-timeout-ms", "--haproxy-clientip", "-H",
        "--header", "--hostpubmd5", "--hostpubsha256", "--hsts", "--interface", "--ipfs-gateway", "--json",
        "--keepalive-time", "--key", "--key-type", "--krb", "--libcurl", "--limit-rate", "--local-port",
        "--login-options", "--mail-auth", "--mail-from", "--mail-rcpt", "--max-filesize", "--max-redirs", "-m",
        "--max-time", "--netrc-file", "--noproxy", "--oauth2-bearer", "-o", "--output", "--output-dir",
        "--parallel-max", "--pass", "--pinnedpubkey", "--preproxy", "--proto", "--proto-default",
        "--proto-redir", "-x", "--proxy", "--proxy-cacert", "--proxy-capath", "--proxy-cert",
        "--proxy-cert-type", "--proxy-ciphers", "--proxy-crlfile", "--proxy-header", "--proxy-key",
        "--proxy-key-type", "--proxy-pass", "--proxy-pinnedpubkey", "--proxy-service-name",
        "--proxy-tls13-ciphers", "--proxy-tlsauthtype", "--proxy-tlspassword", "--proxy-tlsuser", "-U",
        "--proxy-user", "--proxy1.0", "--pubkey", "-Q", "--quote", "--random-file", "-r", "--range", "--rate",
        "-e", "--referer", "-X", "--request", "--request-target", "--resolve", "--retry", "--retry-delay",
        "--retry-max-time", "--sasl-authzid", "--service-name", "--socks4", "--socks4a", "--socks5",
        "--socks5-gssapi-service", "--socks5-hostname", "-Y", "--speed-limit", "-y", "--speed-time", "--stderr",
        "-t", "--telnet-option", "--tftp-blksize", "-z", "--time-cond", "--tls-max", "--tls13-ciphers",
        "--tlsauthtype", "--tlspassword", "--tlsuser", "--trace", "--trace-ascii", "--trace-config",
        "--unix-socket", "-T", "--upload-file", "--url", "--url-query", "-u", "--user", "-A", "--user-agent",
        "--variable", "-w", "--write-out", "--ech", "--ssl-sessions", "--upload-flags", "--knownhosts",
        "--ip-tos", "--vlan-priority",
    ];

    /// <summary>
    /// curl options without a value. Most of them change how curl transfers, which a request in Hermes has no use
    /// for, so they are accepted and ignored. Anything in neither list is reported as an unknown option.
    /// </summary>
    private static readonly HashSet<string> FlagsWithoutValue =
    [
        "--anyauth", "-a", "--append", "--basic", "--ca-native", "--cert-status", "--compressed",
        "--compressed-ssh", "--create-dirs", "--crlf", "--digest", "-q", "--disable", "--disable-eprt",
        "--disable-epsv", "--disallow-username-in-url", "--doh-cert-status", "--doh-insecure", "-f", "--fail",
        "--fail-early", "--fail-with-body", "--false-start", "--form-escape", "--ftp-create-dirs", "--ftp-pasv",
        "--ftp-pret", "--ftp-skip-pasv-ip", "--ftp-ssl-ccc", "--ftp-ssl-control", "-G", "--get", "-g",
        "--globoff", "--haproxy-protocol", "-I", "--head", "--http0.9", "-0", "--http1.0", "--http1.1",
        "--http2", "--http2-prior-knowledge", "--http3", "--http3-only", "--ignore-content-length", "-i",
        "--include", "-k", "--insecure", "-4", "--ipv4", "-6", "--ipv6", "-j", "--junk-session-cookies", "-l",
        "--list-only", "-L", "--location", "--location-trusted", "--mail-rcpt-allowfails", "-M", "--manual",
        "--metalink", "--negotiate", "-n", "--netrc", "--netrc-optional", "-:", "--next", "--no-alpn", "-N",
        "--no-buffer", "--no-clobber", "--no-keepalive", "--no-npn", "--no-progress-meter", "--no-sessionid",
        "--ntlm", "--ntlm-wb", "-Z", "--parallel", "--parallel-immediate", "--path-as-is", "--post301",
        "--post302", "--post303", "-#", "--progress-bar", "--proxy-anyauth", "--proxy-basic",
        "--proxy-ca-native", "--proxy-digest", "--proxy-http2", "--proxy-insecure", "--proxy-negotiate",
        "--proxy-ntlm", "--proxy-ssl-allow-beast", "--proxy-ssl-auto-client-cert", "--proxy-tlsv1", "-p",
        "--proxytunnel", "--raw", "-J", "--remote-header-name", "-O", "--remote-name", "--remote-name-all",
        "-R", "--remote-time", "--remove-on-error", "--retry-all-errors", "--retry-connrefused", "--sasl-ir",
        "-S", "--show-error", "-s", "--silent", "--socks5-basic", "--socks5-gssapi", "--socks5-gssapi-nec",
        "--ssl", "--ssl-allow-beast", "--ssl-auto-client-cert", "--ssl-no-revoke", "--ssl-reqd",
        "--ssl-revoke-best-effort", "-2", "--sslv2", "-3", "--sslv3", "--styled-output",
        "--suppress-connect-headers", "--tcp-fastopen", "--tcp-nodelay", "--tftp-no-options", "-1", "--tlsv1",
        "--tlsv1.0", "--tlsv1.1", "--tlsv1.2", "--tlsv1.3", "--tr-encoding", "--trace-ids", "--trace-time",
        "-B", "--use-ascii", "-v", "--verbose", "-V", "--version", "--xattr", "-h", "--help", "--follow",
        "--out-null", "--tls-earlydata", "--dump-ca-embed", "--skip-existing",
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
            throw new ImportException("This isn't a cURL command; it has to start with curl");

        var request = new HttpRequestModel();
        string? method = null;
        string? explicitUrl = null;
        var bareWords = new List<string>();
        var unknownOptions = new List<string>();
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
                default:
                    if (!IsKnownOption(flag))
                        unknownOptions.Add(flag);
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
        // An unknown option may take a value, which is then one of the bare words. That only matters when no word looks
        // like a URL: the choice is then a guess, so say which option is to blame instead of importing a wrong URL.
        if (unknownOptions.Count > 0 && explicitUrl is null && !bareWords.Any(LooksLikeUrl))
            throw new ImportException($"The cURL command uses the option {unknownOptions[0]}, which Hermes doesn't know, so it can't tell which word is the URL");
        if (request.Url.Length == 0)
            throw new ImportException("The cURL command has no URL");
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
                    yield return (arg, Next() ?? throw MissingValue(arg));
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
                    yield return (flag, j + 1 < arg.Length ? arg[(j + 1)..] : Next() ?? throw MissingValue(flag));
                    break;
                }
            }
            else
            {
                yield return (null, arg);
            }
        }
    }

    private static ImportException MissingValue(string flag) =>
        new($"The cURL command ends after {flag}, which needs a value");

    /// <summary>An option curl has, with or without a value. <c>--no-x</c> turns the on/off option <c>--x</c> off.</summary>
    private static bool IsKnownOption(string? flag) =>
        flag is not null &&
        (FlagsWithoutValue.Contains(flag) || FlagsWithValue.Contains(flag) ||
         (flag.StartsWith("--no-", StringComparison.Ordinal) && FlagsWithoutValue.Contains("--" + flag[5..])));

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

    private static ImportException UnterminatedQuote(string quote) =>
        new($"The cURL command has a {quote} quote that is never closed");

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
                    if (i >= input.Length)
                        throw UnterminatedQuote("'");
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
                    if (i >= input.Length)
                        throw UnterminatedQuote("'");
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
                    if (i >= input.Length)
                        throw UnterminatedQuote("\"");
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
