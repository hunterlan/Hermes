using Hermes.Core.Variables;

namespace Hermes.Core.Secrets;

/// <summary>
/// Final touches on the text of a request prepared with <see cref="Http.RequestPreparer.PrepareWithMasked"/>, whose
/// secret variables and auth credentials are already masked at the source.
/// </summary>
public static class SecretMasker
{
    private const string Mask = VariableScope.MaskedValue;

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
}
