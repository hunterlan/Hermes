using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Ctrl+K: search requests by name, method or URL, or run any command.</summary>
public sealed partial class CommandPaletteViewModel(Func<IEnumerable<PaletteItem>> source) : ObservableObject
{
    private const int MaxResults = 50;

    private List<PaletteItem> _all = [];

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Query { get; set; } = "";
    [ObservableProperty] public partial PaletteItem? Selected { get; set; }

    public ObservableCollection<PaletteItem> Results { get; } = [];

    public void Open()
    {
        _all = source().ToList();
        Query = "";
        Filter();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        var terms = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Results.Clear();
        foreach (var item in _all.Where(i => terms.All(t => Matches(i, t))).Take(MaxResults))
            Results.Add(item);
        Selected = Results.FirstOrDefault();

        static bool Matches(PaletteItem item, string term) =>
            item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.Detail?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
            || item.Method?.Equals(term, StringComparison.OrdinalIgnoreCase) == true;
    }

    public void Move(int delta)
    {
        if (Results.Count == 0)
            return;
        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(index + delta, 0, Results.Count - 1)];
    }

    public void Run(PaletteItem? item = null)
    {
        item ??= Selected;
        if (item is null)
            return;
        Close();
        item.Run();
    }

    /// <summary>Every saved request with its location, for the palette.</summary>
    public static IEnumerable<(HttpRequestModel Request, string Path)> Requests(IEnumerable<CollectionModel> collections)
    {
        foreach (var collection in collections)
            foreach (var item in Walk(collection.Items, collection.Name))
                yield return item;

        static IEnumerable<(HttpRequestModel, string)> Walk(IEnumerable<CollectionNode> nodes, string path)
        {
            foreach (var node in nodes)
            {
                if (node is HttpRequestModel request)
                    yield return (request, path);
                else if (node is FolderModel folder)
                    foreach (var inner in Walk(folder.Items, $"{path} / {folder.Name}"))
                        yield return inner;
            }
        }
    }
}
