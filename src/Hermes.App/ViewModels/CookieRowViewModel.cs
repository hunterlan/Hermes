using System.Net;

namespace Hermes.App.ViewModels;

public sealed class CookieRowViewModel(Cookie cookie)
{
    public Cookie Cookie { get; } = cookie;
    public string Name => Cookie.Name;
    public string Value => Cookie.Value;
    public string Path => Cookie.Path;
    public string Expires => Cookie.Expires == DateTime.MinValue ? "Session" : Cookie.Expires.ToLocalTime().ToString("g");
    public string Flags => string.Join(", ", new[] { Cookie.HttpOnly ? "HttpOnly" : null, Cookie.Secure ? "Secure" : null }.OfType<string>());
}
