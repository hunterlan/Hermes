namespace Hermes.Core.Auth;

public sealed class OAuth2TokenResult
{
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? TokenType { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Error { get; init; }

    public bool IsSuccess => AccessToken is not null;
}
