using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Http;

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

public sealed class CookieDomainViewModel(string domain, IEnumerable<CookieRowViewModel> cookies)
{
    public string Domain { get; } = domain;
    public List<CookieRowViewModel> Cookies { get; } = cookies.ToList();
}

/// <summary>Lists the shared cookie jar grouped by domain, with delete.</summary>
public sealed partial class CookieManagerViewModel : ObservableObject
{
    private readonly HttpClientProvider _http;

    public CookieManagerViewModel(HttpClientProvider http)
    {
        _http = http;
        Refresh();
    }

    public ObservableCollection<CookieDomainViewModel> Domains { get; } = [];

    public bool IsEmpty => Domains.Count == 0;

    private void Refresh()
    {
        Domains.Clear();
        foreach (var group in _http.GetAllCookies().Where(c => !c.Expired).GroupBy(c => c.Domain.TrimStart('.')).OrderBy(g => g.Key))
            Domains.Add(new CookieDomainViewModel(group.Key, group.Select(c => new CookieRowViewModel(c))));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Delete(CookieRowViewModel row)
    {
        _http.DeleteCookie(row.Cookie);
        Refresh();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _http.ClearCookies();
        Refresh();
    }
}
