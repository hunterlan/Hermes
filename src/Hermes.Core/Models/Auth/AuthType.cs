namespace Hermes.Core.Models.Auth;

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
