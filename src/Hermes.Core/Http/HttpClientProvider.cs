using System.Collections.Concurrent;
using System.Net;

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
        if (!verifySsl)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return handler;
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
