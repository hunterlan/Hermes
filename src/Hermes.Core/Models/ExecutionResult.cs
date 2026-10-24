namespace Hermes.Core.Models;

/// <summary>Result of sending a request: either a response or an error (network failure, timeout, ...).</summary>
public sealed class ExecutionResult
{
    public ResponseModel? Response { get; init; }
    public string? Error { get; init; }
    public bool Cancelled { get; init; }

    /// <summary>The request after variable resolution and auth, for display / debugging.</summary>
    public string? ResolvedUrl { get; init; }
}
