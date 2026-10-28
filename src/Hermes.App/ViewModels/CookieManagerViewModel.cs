using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Http;

namespace Hermes.App.ViewModels;

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
