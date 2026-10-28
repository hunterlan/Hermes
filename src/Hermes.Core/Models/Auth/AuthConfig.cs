namespace Hermes.Core.Models.Auth;

public sealed class AuthConfig
{
    public AuthType Type { get; set; } = AuthType.Inherit;
    public BasicAuth Basic { get; set; } = new();
    public BearerAuth Bearer { get; set; } = new();
    public ApiKeyAuth ApiKey { get; set; } = new();
    public OAuth2Auth OAuth2 { get; set; } = new();

    public static AuthConfig Inherit() => new() { Type = AuthType.Inherit };
    public static AuthConfig NoAuth() => new() { Type = AuthType.None };
}
