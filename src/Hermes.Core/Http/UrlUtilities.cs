using System.Text;
using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>
/// String-level URL helpers. They deliberately don't use <see cref="Uri"/>: URLs being edited
/// are often incomplete or contain <c>{{variables}}</c>.
/// </summary>
public static class UrlUtilities
{
    public static UrlParts Split(string url)
    {
        string? fragment = null;
        var hash = url.IndexOf('#');
        if (hash >= 0)
        {
            fragment = url[(hash + 1)..];
            url = url[..hash];
        }

        var q = url.IndexOf('?');
        return q < 0 ? new UrlParts(url, null, fragment) : new UrlParts(url[..q], url[(q + 1)..], fragment);
    }

    /// <summary>Parses the query string into rows, keeping values raw (not percent-decoded), like Postman.</summary>
    public static List<KeyValueItem> ParseQuery(string? query)
    {
        var list = new List<KeyValueItem>();
        if (string.IsNullOrEmpty(query))
            return list;
        foreach (var pair in query.Split('&'))
        {
            if (pair.Length == 0)
                continue;
            var eq = pair.IndexOf('=');
            list.Add(eq < 0 ? new KeyValueItem(pair, "") : new KeyValueItem(pair[..eq], pair[(eq + 1)..]));
        }
        return list;
    }

    public static string BuildQuery(IEnumerable<KeyValueItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (!item.Enabled || (item.Key.Length == 0 && item.Value.Length == 0))
                continue;
            if (sb.Length > 0)
                sb.Append('&');
            sb.Append(item.Key);
            if (item.Value.Length > 0)
                sb.Append('=').Append(item.Value);
        }
        return sb.ToString();
    }

    /// <summary>Replaces the query string of <paramref name="url"/> with the enabled params.</summary>
    public static string WithQuery(string url, IEnumerable<KeyValueItem> items)
    {
        var parts = Split(url);
        var query = BuildQuery(items);
        var sb = new StringBuilder(parts.Base);
        if (query.Length > 0)
            sb.Append('?').Append(query);
        if (parts.Fragment is not null)
            sb.Append('#').Append(parts.Fragment);
        return sb.ToString();
    }

    public static string AppendQueryParam(string url, string key, string value)
    {
        var parts = Split(url);
        var pair = Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
        var query = string.IsNullOrEmpty(parts.Query) ? pair : parts.Query + "&" + pair;
        return parts.Base + "?" + query + (parts.Fragment is null ? "" : "#" + parts.Fragment);
    }

    /// <summary>Returns the index where the path starts (after scheme and authority), or -1.</summary>
    private static int PathStart(string baseUrl)
    {
        var schemeEnd = baseUrl.IndexOf("://", StringComparison.Ordinal);
        var authorityStart = schemeEnd >= 0 ? schemeEnd + 3 : 0;
        return baseUrl.IndexOf('/', authorityStart);
    }

    /// <summary>Names of <c>:name</c> path segments, in order of appearance.</summary>
    public static List<string> ParsePathVariables(string url)
    {
        var result = new List<string>();
        var baseUrl = Split(url).Base;
        var start = PathStart(baseUrl);
        if (start < 0)
            return result;
        foreach (var segment in baseUrl[start..].Split('/'))
        {
            if (segment.Length > 1 && segment[0] == ':' && !result.Contains(segment[1..]))
                result.Add(segment[1..]);
        }
        return result;
    }

    public static string ReplacePathVariables(string url, IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0)
            return url;
        var parts = Split(url);
        var start = PathStart(parts.Base);
        if (start < 0)
            return url;

        var segments = parts.Base[start..].Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (s.Length > 1 && s[0] == ':' && values.TryGetValue(s[1..], out var v))
                segments[i] = v;
        }
        var rebuilt = parts.Base[..start] + string.Join('/', segments);
        if (parts.Query is not null)
            rebuilt += "?" + parts.Query;
        if (parts.Fragment is not null)
            rebuilt += "#" + parts.Fragment;
        return rebuilt;
    }

    /// <summary>Adds <c>http://</c> when the URL has no scheme, as Postman does.</summary>
    public static string EnsureScheme(string url)
    {
        url = url.Trim();
        if (url.Length == 0 || url.Contains("://", StringComparison.Ordinal))
            return url;
        return "http://" + url;
    }
}
