using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>A request with variables resolved and auth applied, ready to be sent or turned into a code snippet.</summary>
public sealed class PreparedRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<KeyValueItem> Headers { get; } = [];
    public BodyMode BodyMode { get; set; }
    public string RawBody { get; set; } = "";
    public List<KeyValueItem> FormFields { get; } = [];
    public string? BinaryFilePath { get; set; }
    public bool FollowRedirects { get; set; } = true;
    public bool VerifySsl { get; set; } = true;

    /// <summary>0 = no timeout.</summary>
    public int TimeoutMs { get; set; }

    // Headers that were not set by the user, by instance (rows can repeat a name). Absent means Configured.
    private readonly Dictionary<KeyValueItem, PreparedHeaderOrigin> _headerOrigins = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Header rows (as written on the request, a folder or the collection) that were left out because the body sets
    /// the header itself, such as Content-Type for multipart form-data. Compared by instance.
    /// </summary>
    public HashSet<KeyValueItem> RowsReplacedByBody { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Why <paramref name="header"/> (one of <see cref="Headers"/>) was added.</summary>
    public PreparedHeaderOrigin OriginOf(KeyValueItem header) =>
        _headerOrigins.GetValueOrDefault(header, PreparedHeaderOrigin.Configured);

    /// <summary>Adds a header that the user didn't set, remembering why.</summary>
    public void AddAutomaticHeader(KeyValueItem header, PreparedHeaderOrigin origin)
    {
        Headers.Add(header);
        _headerOrigins[header] = origin;
    }

    /// <summary>Marks the headers from <paramref name="firstIndex"/> on as added for <paramref name="origin"/>.</summary>
    public void MarkHeadersFrom(int firstIndex, PreparedHeaderOrigin origin)
    {
        for (var i = firstIndex; i < Headers.Count; i++)
            _headerOrigins[Headers[i]] = origin;
    }

    public string? GetHeader(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))?.Value;
}
