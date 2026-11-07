namespace Hermes.Core.Headers;

/// <summary>
/// Headers that usually carry credentials, so the UI shows them masked until revealed. Headers set by auth are
/// sensitive whatever their name; this covers the ones typed in by hand or inherited.
/// </summary>
public static class SensitiveHeaders
{
    /// <summary>
    /// Shown in place of a sensitive value. Always the same length and dots only, so it reveals neither the scheme
    /// nor the length of the value.
    /// </summary>
    public const string MaskText = "••••••••••••";

    private static readonly string[] CredentialHeaderNames = ["Authorization", "Proxy-Authorization"];

    private static readonly string[] SensitiveNames = [.. CredentialHeaderNames, "Cookie", "X-Api-Key"];

    /// <summary>The header name is one of the well-known credential carriers, compared ignoring case.</summary>
    public static bool IsSensitiveName(string? name) =>
        name is not null && SensitiveNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The header's value is "scheme credentials" (Authorization, Proxy-Authorization), so a scheme can be kept.</summary>
    public static bool HasAuthenticationScheme(string? name) =>
        name is not null && CredentialHeaderNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}
