namespace Hermes.Core.Models;

public sealed class ResponseCookie
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Domain { get; init; } = "";
    public string Path { get; init; } = "";
    public DateTime? Expires { get; init; }
    public bool HttpOnly { get; init; }
    public bool Secure { get; init; }
}

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

/// <summary>Result of sending a request: either a response or an error (network failure, timeout, ...).</summary>
public sealed class ExecutionResult
{
    public ResponseModel? Response { get; init; }
    public string? Error { get; init; }
    public bool Cancelled { get; init; }

    /// <summary>The request after variable resolution and auth, for display / debugging.</summary>
    public string? ResolvedUrl { get; init; }
}
