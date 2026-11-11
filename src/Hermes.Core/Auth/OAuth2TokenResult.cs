using Hermes.Core.Models;

namespace Hermes.Core.Auth;

public sealed class OAuth2TokenResult
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? TokenType { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Why no token was received; null on success and when the user cancelled.</summary>
    public RequestFailure? Failure { get; init; }

    /// <summary>The user cancelled, which is not a failure to report.</summary>
    public bool IsCancelled { get; init; }

    public bool IsSuccess => AccessToken is not null;
}
