using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Variables;

namespace Hermes.Core.Secrets;

/// <summary>
/// Final touches on the text of a request prepared with <see cref="Http.RequestPreparer.PrepareWithMasked"/>, whose
/// secret variables and auth credentials are already masked at the source.
/// </summary>
public static class SecretMasker
{
    private const string Mask = VariableScope.MaskedValue;

    /// <summary>Shorter values would mask unrelated text (a secret "1" would hide every digit 1).</summary>
    private const int MinSecretLength = 4;

    private static readonly string[] CredentialHeaders = ["Authorization", "Proxy-Authorization"];

    /// <summary>
    /// Masks credential headers whatever their origin (keeping the scheme, e.g. "Bearer ••••••"): a header typed in
    /// by hand isn't a secret variable, and Basic credentials are encoded. Masks put in a URL show unescaped.
    /// </summary>
    public static string MaskCredentialHeaders(string text)
    {
        var lines = text.Replace(Uri.EscapeDataString(Mask), Mask).Split('\n');
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
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Masks every occurrence of <paramref name="secrets"/> in the URL, header values, body and form values of
    /// <paramref name="request"/>, also as they appear encoded. Catches a secret that got there other than through a
    /// secret variable or auth field: typed in by hand, or held by an ordinary variable too.
    /// </summary>
    internal static void MaskOccurrences(PreparedRequest request, IEnumerable<string> secrets)
    {
        var forms = secrets.Select(s => s.Trim()).Where(s => s.Length >= MinSecretLength)
            .SelectMany(EncodedForms).Distinct().OrderByDescending(f => f.Length).ToList();
        if (forms.Count == 0)
            return;

        string M(string text)
        {
            var masked = new StringBuilder(text);
            foreach (var form in forms)
                masked.Replace(form, Mask);
            return masked.ToString();
        }

        request.Url = M(request.Url);
        request.RawBody = M(request.RawBody);
        foreach (var h in request.Headers)
            h.Value = M(h.Value);
        foreach (var f in request.FormFields)
            f.Value = M(f.Value);
    }

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
        return forms;
    }
}
