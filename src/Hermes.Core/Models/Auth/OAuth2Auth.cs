namespace Hermes.Core.Models;

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
