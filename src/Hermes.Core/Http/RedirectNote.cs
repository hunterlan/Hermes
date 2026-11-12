namespace Hermes.Core.Http;

/// <summary>The sentence that tells a failure's facts are about the address a redirect led to, not the one typed.</summary>
internal static class RedirectNote
{
    public static string Prepend(string host, string facts) => $"The request was redirected to {host}. {facts}";
}
