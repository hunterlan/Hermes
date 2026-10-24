namespace Hermes.App.ViewModels;

public sealed class CookieDomainViewModel(string domain, IEnumerable<CookieRowViewModel> cookies)
{
    public string Domain { get; } = domain;
    public List<CookieRowViewModel> Cookies { get; } = cookies.ToList();
}
