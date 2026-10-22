using System.Collections.Concurrent;
using System.Net;

namespace Hermes.Core.Http;

/// <summary>
/// Hands out long-lived <see cref="HttpClient"/>s. Redirect and certificate settings live on the handler,
/// so there's one client per combination; all of them share a single cookie jar.
/// </summary>
public sealed class HttpClientProvider : IDisposable
{
    private readonly ConcurrentDictionary<(bool FollowRedirects, bool VerifySsl), HttpClient> _clients = new();

    public CookieContainer Cookies { get; private set; } = new();

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
            UseCookies = true,
            CookieContainer = Cookies,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        };
        if (!verifySsl)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return handler;
    }

    public IReadOnlyList<Cookie> GetAllCookies() => Cookies.GetAllCookies().ToList();

    public void DeleteCookie(Cookie cookie)
    {
        foreach (Cookie c in Cookies.GetAllCookies())
            if (c.Name == cookie.Name && c.Domain == cookie.Domain && c.Path == cookie.Path)
                c.Expired = true;
    }

    /// <summary>CookieContainer can't be cleared, so start over with fresh handlers.</summary>
    public void ClearCookies()
    {
        Cookies = new CookieContainer();
        var old = _clients.Values.ToList();
        _clients.Clear();
        foreach (var client in old)
            client.Dispose();
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
    }
}
