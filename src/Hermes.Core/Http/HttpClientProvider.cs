using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Hermes.Core.Http;

/// <summary>
/// Hands out long-lived <see cref="HttpClient"/>s. Redirect and certificate settings live on the handler,
/// so there's one client per combination; all of them share a single cookie jar (or none, with <c>useCookies: false</c>).
/// Clients are never replaced while the provider lives, so requests in flight are never cut off.
/// </summary>
public sealed class HttpClientProvider(bool useCookies = true) : IDisposable
{
    private readonly ConcurrentDictionary<(bool FollowRedirects, bool VerifySsl), HttpClient> _clients = new();

    public CookieContainer Cookies { get; } = new();

    /// <summary>What failed certificate checks looked like, for the executor to explain a failed request.</summary>
    public CertificateFailureLog CertificateFailures { get; } = new();

    public HttpClient Get(bool followRedirects, bool verifySsl) =>
        _clients.GetOrAdd((followRedirects, verifySsl), key => new HttpClient(CreateHandler(key.FollowRedirects, key.VerifySsl))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        });

    private SocketsHttpHandler CreateHandler(bool followRedirects, bool verifySsl)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = useCookies,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        if (useCookies)
            handler.CookieContainer = Cookies;
        handler.SslOptions.RemoteCertificateValidationCallback = verifySsl ? ValidateAndRecord : (_, _, _, _) => true;
        return handler;
    }

    /// <summary>
    /// Accepts exactly what the default validation accepts, and notes what the certificate looked like when it
    /// doesn't, so a failed request can say why in words.
    /// </summary>
    private bool ValidateAndRecord(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors policyErrors)
    {
        var host = sender switch
        {
            SslStream stream => stream.TargetHostName,
            HttpRequestMessage message => message.RequestUri?.IdnHost,
            _ => null,
        };
        if (host is null)
            return policyErrors == SslPolicyErrors.None;

        if (policyErrors == SslPolicyErrors.None)
            CertificateFailures.Clear(host);
        else if (CertificateFacts.Capture(host, certificate, chain, policyErrors) is { } facts)
            CertificateFailures.Record(facts);
        return policyErrors == SslPolicyErrors.None;
    }

    /// <summary>Cookies in the jar, without those deleted or past their expiry.</summary>
    public IReadOnlyList<Cookie> GetAllCookies() => Cookies.GetAllCookies().Where(c => !c.Expired).ToList();

    public void DeleteCookie(Cookie cookie)
    {
        foreach (Cookie c in Cookies.GetAllCookies())
            if (c.Name == cookie.Name && c.Domain == cookie.Domain && c.Path == cookie.Path)
                c.Expired = true;
    }

    /// <summary>
    /// Expires every cookie. CookieContainer can't be emptied, but expired cookies are never sent; replacing the
    /// container instead would mean replacing (and disposing) clients that requests may still be using.
    /// </summary>
    public void ClearCookies()
    {
        foreach (Cookie c in Cookies.GetAllCookies())
            c.Expired = true;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
    }
}
