namespace Hermes.Core.Models;

public enum AuthType
{
    /// <summary>Use the auth of the parent folder / collection.</summary>
    Inherit,
    None,
    Basic,
    Bearer,
    ApiKey,
    OAuth2,
}

public enum ApiKeyLocation
{
    Header,
    Query,
}

public enum OAuth2GrantType
{
    AuthorizationCode,
    ClientCredentials,
    Password,
}

public enum OAuth2ClientAuthentication
{
    /// <summary>Send client credentials as a Basic auth header.</summary>
    Header,
    /// <summary>Send client credentials in the token request body.</summary>
    Body,
}

public sealed class BasicAuth
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class BearerAuth
{
    public string Token { get; set; } = "";
}

public sealed class ApiKeyAuth
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public ApiKeyLocation AddTo { get; set; } = ApiKeyLocation.Header;
}

public sealed class OAuth2Auth
{
    public OAuth2GrantType GrantType { get; set; } = OAuth2GrantType.AuthorizationCode;
    public string AuthUrl { get; set; } = "";
    public string AccessTokenUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "";
    public string RedirectUri { get; set; } = "http://localhost:5577/callback";
    public string State { get; set; } = "";
    public bool UsePkce { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public OAuth2ClientAuthentication ClientAuthentication { get; set; } = OAuth2ClientAuthentication.Header;
    public string HeaderPrefix { get; set; } = "Bearer";

    /// <summary>The token currently in use; obtained via "Get New Access Token" or pasted by the user.</summary>
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

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
