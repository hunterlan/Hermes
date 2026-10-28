using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Hermes.App.ViewModels.Sidebar;

/// <summary>A node in the collections tree (collection, folder or request).</summary>
public abstract partial class TreeNodeViewModel : ObservableObject
{
    protected TreeNodeViewModel(TreeNodeViewModel? parent) => Parent = parent;

    public TreeNodeViewModel? Parent { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial bool IsExpanded { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsRenaming { get; set; }

    /// <summary>Highlighted while something is dragged over it.</summary>
    [ObservableProperty] public partial bool IsDropTarget { get; set; }

    public abstract Guid Id { get; }

    public virtual bool IsCollection => false;
    public virtual bool IsFolder => false;
    public virtual bool IsRequest => false;
    public bool CanContainChildren => IsCollection || IsFolder;

    public CollectionNodeViewModel Root => this as CollectionNodeViewModel ?? Parent!.Root;

    /// <summary>Applies a search filter; returns whether this node (or a descendant) matches.</summary>
    public bool ApplyFilter(string filter)
    {
        var childMatch = false;
        foreach (var child in Children)
            childMatch |= child.ApplyFilter(filter);
        var selfMatch = filter.Length == 0 || Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
        IsVisible = selfMatch || childMatch;
        return IsVisible;
    }

    public IEnumerable<TreeNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.SelfAndDescendants())
                yield return node;
    }
}
