namespace Hermes.App.ViewModels.Sidebar;

public sealed class HistoryGroupViewModel(string title, IEnumerable<HistoryItemViewModel> items)
{
    public string Title { get; } = title;
    public List<HistoryItemViewModel> Items { get; } = items.ToList();
}
