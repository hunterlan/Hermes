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

    public string? GetHeader(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase))?.Value;
}
