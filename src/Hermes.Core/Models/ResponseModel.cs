namespace Hermes.Core.Models;

public sealed class ResponseModel
{
    public int StatusCode { get; init; }
    public string ReasonPhrase { get; init; } = "";
    public string HttpVersion { get; init; } = "";
    public IReadOnlyList<KeyValueItem> Headers { get; init; } = [];
    public byte[] Body { get; init; } = [];
    public string? ContentType { get; init; }
    public string? Charset { get; init; }
    public Uri? FinalUri { get; init; }
    public TimeSpan TimeToFirstByte { get; init; }
    public TimeSpan TotalTime { get; init; }
    public long HeadersSize { get; init; }
    public IReadOnlyList<ResponseCookie> Cookies { get; init; } = [];

    public long BodySize => Body.LongLength;
    public long TotalSize => HeadersSize + BodySize;
}
