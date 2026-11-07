using Hermes.Core.Models.Auth;

namespace Hermes.Core.Models;

public sealed class HttpRequestModel : CollectionNode
{
    public string Method { get; set; } = "GET";

    /// <summary>The URL as typed, including the query string. Source of truth for enabled query params.</summary>
    public string Url { get; set; } = "";

    /// <summary>Query params including disabled ones (which are not part of <see cref="Url"/>).</summary>
    public List<KeyValueItem> QueryParams { get; set; } = [];

    /// <summary>Values for <c>:name</c> segments in the URL path.</summary>
    public List<KeyValueItem> PathVariables { get; set; } = [];

    public List<KeyValueItem> Headers { get; set; } = [];

    /// <summary>
    /// Names of headers inherited from the collection or a folder that this request doesn't send (matched ignoring
    /// case). Lets one request opt out without changing the folder.
    /// </summary>
    public List<string> SkippedInheritedHeaders { get; set; } = [];

    public RequestBody Body { get; set; } = new();
    public AuthConfig Auth { get; set; } = AuthConfig.Inherit();
    public RequestSettings Settings { get; set; } = new();

    public static HttpRequestModel CreateNew() => new() { Name = "Untitled Request" };
}
