namespace Hermes.Core.Models.Auth;

public enum OAuth2ClientAuthentication
{
    /// <summary>Send client credentials as a Basic auth header.</summary>
    Header,
    /// <summary>Send client credentials in the token request body.</summary>
    Body,
}
