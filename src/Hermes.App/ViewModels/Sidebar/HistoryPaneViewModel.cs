using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels.Sidebar;

public sealed partial class HistoryPaneViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private readonly DialogService _dialogs;
    private readonly ITabHost _host;

    public HistoryPaneViewModel(WorkspaceService workspace, DialogService dialogs, ITabHost host)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _host = host;
        workspace.History.CollectionChanged += OnHistoryChanged;
        Rebuild();
    }

    public ObservableCollection<HistoryGroupViewModel> Groups { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; } = "";

    public bool IsEmpty => Groups.Count == 0;

    partial void OnFilterChanged(string value) => Rebuild();

    private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Groups.Clear();
        var filter = Filter.Trim();
        var today = DateTime.Today;
        var items = _workspace.History
            .Where(h => filter.Length == 0
                        || h.Request.Url.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                        || h.Request.Method.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(h => h.Timestamp);
        foreach (var group in items.GroupBy(h => h.Timestamp.LocalDateTime.Date))
        {
            var title = group.Key == today ? "Today"
                : group.Key == today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMMM d", CultureInfo.CurrentCulture);
            Groups.Add(new HistoryGroupViewModel(title, group.Select(h => new HistoryItemViewModel(h))));
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>"Open in new tab" is hidden in single-tab mode, where every open replaces the current tab.</summary>
    public bool IsOpenInTabAvailable => _host.ShowTabs;

    /// <summary>Called when Settings → Tabs changed.</summary>
    public void OnShowTabsChanged() => OnPropertyChanged(nameof(IsOpenInTabAvailable));

    [RelayCommand]
    public void Open(HistoryItemViewModel? item)
    {
        if (item is null)
            return;
        var copy = ModelCloner.Clone(item.Entry.Request);
        copy.Id = Guid.NewGuid();
        copy.Name = string.IsNullOrWhiteSpace(copy.Url) ? "Untitled Request" : copy.Url;
        _host.OpenDraft(copy);
    }

    [RelayCommand]
    private void Delete(HistoryItemViewModel? item)
    {
        if (item is not null)
            _workspace.DeleteHistory(item.Entry);
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (_workspace.History.Count == 0)
            return;
        if (await _dialogs.ConfirmAsync("Clear history?", "All history entries will be permanently deleted.", "Clear all"))
            _workspace.ClearHistory();
    }
}
