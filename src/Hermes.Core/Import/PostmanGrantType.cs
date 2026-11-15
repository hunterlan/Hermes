using Hermes.Core.Models.Auth;

namespace Hermes.Core.Import;

/// <summary>An OAuth 2.0 grant type of a Postman collection: Postman names PKCE as a grant of its own.</summary>
internal sealed record PostmanGrantType(OAuth2GrantType Grant, bool Pkce);
