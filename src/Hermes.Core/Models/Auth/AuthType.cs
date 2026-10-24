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
