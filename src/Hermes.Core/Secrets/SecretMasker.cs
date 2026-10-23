using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Variables;

namespace Hermes.Core.Secrets;

/// <summary>Hides credentials in the raw request text before it is saved with a response.</summary>
public static class SecretMasker
{
    public const string Mask = "••••••";

    /// <summary>Shorter values would mask unrelated text (a secret "1" would hide every digit 1).</summary>
    private const int MinSecretLength = 4;

    /// <summary>
    /// The raw value plus how it shows up encoded: fully escaped, form-encoded (space as +) and as <see cref="Uri"/>
    /// leaves it in a URL (only spaces and a few other characters escaped).
    /// </summary>
    private static IEnumerable<string> EncodedForms(string secret)
    {
        string[] forms = [secret, Uri.EscapeDataString(secret), System.Net.WebUtility.UrlEncode(secret)];
        // A '#' would end the query, leaving only part of the secret.
        if (!secret.Contains('#') && Uri.TryCreate("http://x/?" + secret, UriKind.Absolute, out var uri))
            forms = [.. forms, uri.Query[1..]];
        return forms.Distinct();
    }

    private static readonly string[] CredentialHeaders = ["Authorization", "Proxy-Authorization"];

    /// <summary>The resolved secret variables and auth credentials that <paramref name="request"/> would send.</summary>
    public static IReadOnlyList<string> CollectSecrets(HttpRequestModel request, RequestContext context)
    {
        var scope = context.BuildScope();
        var auth = context.ResolveAuth(request.Auth).Auth;
        return scope.SecretValues
            .Concat(SecretFields.AuthValues(auth))
            .Select(v => VariableResolver.Resolve(v, scope).Trim())
            .Where(v => v.Length >= MinSecretLength)
            .Distinct()
            .OrderByDescending(v => v.Length)
            .ToList();
    }

    /// <summary>
    /// Masks credential headers (keeping the scheme, e.g. "Bearer ••••••") and every occurrence of
    /// <paramref name="secrets"/>, also as they appear encoded in a URL path, query or form body.
    /// </summary>
    public static string MaskRequestText(string text, IReadOnlyList<string> secrets)
    {
        var lines = text.Split('\n');
        // Headers run from the second line to the first empty one.
        for (var i = 1; i < lines.Length && lines[i].Length > 0; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon <= 0 || !CredentialHeaders.Contains(lines[i][..colon], StringComparer.OrdinalIgnoreCase))
                continue;
            var value = lines[i][(colon + 1)..].Trim();
            var space = value.IndexOf(' ');
            var scheme = space > 0 && value[..space].All(char.IsLetter) ? value[..space] + " " : "";
            lines[i] = $"{lines[i][..colon]}: {scheme}{Mask}";
        }

        var masked = new StringBuilder(string.Join('\n', lines));
        foreach (var secret in secrets.Where(s => s.Length >= MinSecretLength))
            foreach (var form in EncodedForms(secret))
                masked.Replace(form, Mask);
        return masked.ToString();
    }
}
