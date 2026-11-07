namespace Hermes.Core.Http;

/// <summary>A raw request text split into its parts.</summary>
/// <param name="RequestLine">The first line, e.g. "GET /users HTTP/1.1".</param>
/// <param name="Headers">The header lines, in order.</param>
/// <param name="Body">Everything after the first empty line; empty for none.</param>
public sealed record ParsedRawRequest(string RequestLine, IReadOnlyList<RawRequestHeader> Headers, string Body);
