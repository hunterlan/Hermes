using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels.Sidebar;

public enum DropPosition
{
    Before,
    Inside,
    After,
}

public sealed partial class CollectionsPaneViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private readonly DialogService _dialogs;
    private readonly ITabHost _host;

    public CollectionsPaneViewModel(WorkspaceService workspace, DialogService dialogs, ITabHost host)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _host = host;
        workspace.CollectionChanged += _ => Rebuild();
        workspace.CollectionsListChanged += Rebuild;
        Rebuild();
    }

    public ObservableCollection<CollectionNodeViewModel> Collections { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial TreeNodeViewModel? SelectedNode { get; set; }

    public bool IsEmpty => Collections.Count == 0;

    partial void OnFilterChanged(string value)
    {
        foreach (var c in Collections)
        {
            c.ApplyFilter(value.Trim());
            if (value.Length > 0)
                foreach (var node in c.SelfAndDescendants().Where(n => n.IsVisible && n.CanContainChildren))
                    node.IsExpanded = true;
        }
    }

    /// <summary>Recreates the tree from the models, keeping expansion and selection.</summary>
    public void Rebuild()
    {
        var expanded = Collections.SelectMany(c => c.SelfAndDescendants()).Where(n => n.IsExpanded).Select(n => n.Id).ToHashSet();
        var selected = SelectedNode?.Id;
        Collections.Clear();
        foreach (var collection in _workspace.Collections)
        {
            var node = new CollectionNodeViewModel(collection);
            foreach (var n in node.SelfAndDescendants())
                n.IsExpanded = expanded.Contains(n.Id);
            Collections.Add(node);
        }
        if (Filter.Length > 0)
            OnFilterChanged(Filter);
        SelectedNode = selected is { } id ? Find(id) : null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public TreeNodeViewModel? Find(Guid id) =>
        Collections.SelectMany(c => c.SelfAndDescendants()).FirstOrDefault(n => n.Id == id);

    private void ExpandTo(Guid id)
    {
        for (var node = Find(id)?.Parent; node is not null; node = node.Parent)
            node.IsExpanded = true;
    }

    private void BeginRename(Guid id)
    {
        ExpandTo(id);
        if (Find(id) is { } node)
        {
            SelectedNode = node;
            node.IsRenaming = true;
        }
    }

    // ---- open ----

    [RelayCommand]
    public void Open(TreeNodeViewModel? node)
    {
        switch (node)
        {
            case RequestNodeViewModel r:
                _host.OpenRequest(r.Model);
                break;
            case FolderNodeViewModel f:
                _host.OpenFolder(f.Model);
                break;
            case CollectionNodeViewModel c:
                _host.OpenCollection(c.Model);
                break;
        }
    }

    // ---- create ----

    [RelayCommand]
    private void NewCollection()
    {
        var collection = _workspace.CreateCollection("New Collection");
        BeginRename(collection.Id);
    }

    [RelayCommand]
    private void AddRequest(TreeNodeViewModel? node)
    {
        if (ContainerOf(node) is not var (collection, folder))
            return;
        var request = new HttpRequestModel { Name = "New Request" };
        _workspace.AddNode(collection, folder, request);
        ExpandTo(request.Id);
        if (Find(request.Id) is { } created)
        {
            created.Parent!.IsExpanded = true;
            SelectedNode = created;
        }
        _host.OpenRequest(request);
    }

    [RelayCommand]
    private void AddFolder(TreeNodeViewModel? node)
    {
        if (ContainerOf(node) is not var (collection, folder))
            return;
        var newFolder = new FolderModel { Name = "New Folder" };
        _workspace.AddNode(collection, folder, newFolder);
        if (Find(newFolder.Id)?.Parent is { } parent)
            parent.IsExpanded = true;
        BeginRename(newFolder.Id);
    }

    /// <summary>The collection/folder a new item goes into: the node itself, or a request's parent.</summary>
    private static (CollectionModel Collection, FolderModel? Folder)? ContainerOf(TreeNodeViewModel? node) => node switch
    {
        CollectionNodeViewModel c => (c.Model, null),
        FolderNodeViewModel f => (f.Root.Model, f.Model),
        RequestNodeViewModel { Parent: FolderNodeViewModel pf } r => (r.Root.Model, pf.Model),
        RequestNodeViewModel r => (r.Root.Model, null),
        _ => null,
    };

    // ---- rename ----

    [RelayCommand]
    private void Rename(TreeNodeViewModel? node)
    {
        if (node is not null)
            node.IsRenaming = true;
    }

    public void CommitRename(TreeNodeViewModel node, string? newName)
    {
        node.IsRenaming = false;
        newName = newName?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == NameOf(node))
        {
            node.Name = NameOf(node);
            return;
        }

        switch (node)
        {
            case CollectionNodeViewModel c:
                c.Model.Name = newName;
                _workspace.SaveCollection(c.Model);
                break;
            case FolderNodeViewModel f:
                f.Model.Name = newName;
                _workspace.SaveCollection(f.Root.Model);
                break;
            case RequestNodeViewModel r:
                r.Model.Name = newName;
                _workspace.SaveCollection(r.Root.Model);
                _host.OnRequestRenamed(r.Model.Id, newName);
                break;
        }
    }

    private static string NameOf(TreeNodeViewModel node) => node switch
    {
        CollectionNodeViewModel c => c.Model.Name,
        FolderNodeViewModel f => f.Model.Name,
        RequestNodeViewModel r => r.Model.Name,
        _ => node.Name,
    };

    // ---- duplicate / delete / export ----

    [RelayCommand]
    private void Duplicate(TreeNodeViewModel? node)
    {
        switch (node)
        {
            case CollectionNodeViewModel c:
                var copy = ModelCloner.Clone(c.Model);
                copy.Id = Guid.NewGuid();
                copy.PostmanId = null;
                copy.Name += " Copy";
                copy.Items = copy.Items.Select(ModelCloner.Duplicate).ToList();
                _workspace.AddCollection(copy);
                break;
            case FolderNodeViewModel or RequestNodeViewModel:
                if (_workspace.Find(node.Id) is not { } location)
                    return;
                var duplicate = ModelCloner.Duplicate(location.Node);
                duplicate.Name += " Copy";
                _workspace.AddNode(location.Collection, location.Parent, duplicate, location.Container.IndexOf(location.Node) + 1);
                break;
        }
    }

    [RelayCommand]
    private async Task Delete(TreeNodeViewModel? node)
    {
        if (node is null)
            return;
        var kind = node.IsCollection ? "collection" : node.IsFolder ? "folder" : "request";
        if (!await _dialogs.ConfirmAsync($"Delete \"{node.Name}\"?",
                node.IsRequest
                    ? "This request will be permanently deleted."
                    : $"This {kind} and everything in it will be permanently deleted."))
            return;

        var ids = node.SelfAndDescendants().Select(n => n.Id).ToList();
        if (node is CollectionNodeViewModel c)
            _workspace.DeleteCollection(c.Model);
        else
            _workspace.DeleteNode(node.Id);
        _host.CloseTabsFor(ids);
    }

    [RelayCommand]
    private async Task Export(TreeNodeViewModel? node)
    {
        if (node?.Root is not { } root)
            return;
        var fileName = $"{root.Model.Name}.postman_collection.json";
        if (await _dialogs.SaveFileAsync("Export collection (Postman v2.1)", fileName, ".json") is { } path)
            await File.WriteAllTextAsync(path, PostmanCollectionExporter.Export(root.Model));
    }

    // ---- drag & drop ----

    public bool CanDrop(TreeNodeViewModel dragged, TreeNodeViewModel target, DropPosition position)
    {
        if (dragged == target || dragged.IsCollection)
            return false;
        if (target.IsCollection && position != DropPosition.Inside)
            return false;
        if (position == DropPosition.Inside && !target.CanContainChildren)
            return false;
        // Can't drop a folder into itself or its descendants.
        for (var n = target; n is not null; n = n.Parent)
            if (n == dragged)
                return false;
        return true;
    }

    public void Drop(TreeNodeViewModel dragged, TreeNodeViewModel target, DropPosition position)
    {
        if (!CanDrop(dragged, target, position))
            return;

        CollectionModel collection;
        FolderModel? folder;
        int index;
        if (position == DropPosition.Inside)
        {
            (collection, folder) = ContainerOf(target)!.Value;
            index = int.MaxValue;
            target.IsExpanded = true;
        }
        else
        {
            if (_workspace.Find(target.Id) is not { } location)
                return;
            collection = location.Collection;
            folder = location.Parent;
            index = location.Container.IndexOf(location.Node) + (position == DropPosition.After ? 1 : 0);
        }
        _workspace.MoveNode(dragged.Id, collection, folder, index);
    }
}
