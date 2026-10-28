using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Variables;
using Hermes.Core.Web;

namespace Hermes.Core.Auth;

public static class AuthApplier
{
    /// <summary>Adds the auth header / query param to <paramref name="request"/>. Explicit user headers win.</summary>
    public static void Apply(AuthConfig auth, VariableScope scope, PreparedRequest request)
    {
        string R(string s) => VariableResolver.Resolve(s, scope);

        switch (auth.Type)
        {
            case AuthType.Basic:
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{R(auth.Basic.Username)}:{R(auth.Basic.Password)}"));
                AddHeader(request, "Authorization", "Basic " + credentials);
                break;

            case AuthType.Bearer:
                AddHeader(request, "Authorization", "Bearer " + R(auth.Bearer.Token).Trim());
                break;

            case AuthType.ApiKey:
                var key = R(auth.ApiKey.Key);
                if (key.Length == 0)
                    break;
                if (auth.ApiKey.AddTo == ApiKeyLocation.Header)
                    AddHeader(request, key, R(auth.ApiKey.Value));
                else
                    request.Url = UrlUtilities.AppendQueryParam(request.Url, key, R(auth.ApiKey.Value));
                break;

            case AuthType.OAuth2:
                var token = R(auth.OAuth2.AccessToken).Trim();
                if (token.Length == 0)
                    break;
                var prefix = R(auth.OAuth2.HeaderPrefix).Trim();
                AddHeader(request, "Authorization", prefix.Length == 0 ? token : $"{prefix} {token}");
                break;
        }
    }

    private static void AddHeader(PreparedRequest request, string name, string value)
    {
        if (request.Headers.Any(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)))
            return;
        request.Headers.Add(new KeyValueItem(name, value));
    }
}
