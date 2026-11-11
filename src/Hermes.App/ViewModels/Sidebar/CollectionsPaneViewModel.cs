using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Headers;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Secrets;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels.Sidebar;

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
            var node = new CollectionNodeViewModel(collection) { RenameCommitter = CommitRename };
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

    /// <summary>Expands the path to a node and selects it (when a tab for it becomes active).</summary>
    public void Reveal(Guid id)
    {
        if (Find(id) is not { } node)
            return;
        ExpandTo(id);
        SelectedNode = node;
    }

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

    /// <summary>"Open in tab" is hidden in single-tab mode, where every open replaces the current tab.</summary>
    public bool IsOpenInTabAvailable => _host.ShowTabs;

    /// <summary>Called when Settings → Tabs changed.</summary>
    public void OnShowTabsChanged() => OnPropertyChanged(nameof(IsOpenInTabAvailable));

    /// <summary>Single click: requests open in the preview tab; folders and collections only expand or collapse.</summary>
    [RelayCommand]
    private async Task Activate(TreeNodeViewModel? node)
    {
        if (node is RequestNodeViewModel r)
            await _host.PreviewRequestAsync(r.Model);
        else if (node is not null)
            node.IsExpanded = !node.IsExpanded;
    }

    [RelayCommand]
    private async Task Open(TreeNodeViewModel? node)
    {
        switch (node)
        {
            case RequestNodeViewModel r:
                await _host.OpenRequestAsync(r.Model);
                break;
            case FolderNodeViewModel f:
                await _host.OpenFolderAsync(f.Model);
                break;
            case CollectionNodeViewModel c:
                await _host.OpenCollectionAsync(c.Model);
                break;
        }
    }

    // ---- create ----

    [RelayCommand]
    private void NewCollection()
    {
        var collection = new CollectionModel { Name = "New Collection" };
        if (_workspace.AddCollection(collection))
            BeginRename(collection.Id);
    }

    [RelayCommand]
    private async Task AddRequest(TreeNodeViewModel? node)
    {
        if (ContainerOf(node) is not var (collection, folder))
            return;
        var request = new HttpRequestModel { Name = "New Request" };
        if (!_workspace.AddNode(collection, folder, request))
            return;
        ExpandTo(request.Id);
        if (Find(request.Id) is { } created)
        {
            created.Parent!.IsExpanded = true;
            SelectedNode = created;
        }
        await _host.OpenRequestAsync(request);
    }

    [RelayCommand]
    private void AddFolder(TreeNodeViewModel? node)
    {
        if (ContainerOf(node) is not var (collection, folder))
            return;
        var newFolder = new FolderModel { Name = "New Folder" };
        if (!_workspace.AddNode(collection, folder, newFolder))
            return;
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

        var previousName = NameOf(node);
        var saved = node switch
        {
            CollectionNodeViewModel c => Rename(c.Model.Name, name => c.Model.Name = name, newName, c.Model),
            FolderNodeViewModel f => Rename(f.Model.Name, name => f.Model.Name = name, newName, f.Root.Model),
            RequestNodeViewModel r => Rename(r.Model.Name, name => r.Model.Name = name, newName, r.Root.Model),
            _ => true,
        };
        if (!saved)
        {
            // Not on disk, so the old name stays everywhere.
            node.Name = previousName;
            return;
        }
        if (node is RequestNodeViewModel renamed)
            _host.OnRequestRenamed(renamed.Model.Id, newName);
    }

    /// <summary>Sets a new name and saves the collection; puts the old name back when that fails.</summary>
    private bool Rename(string previousName, Action<string> setName, string newName, CollectionModel collection)
    {
        setName(newName);
        if (_workspace.SaveCollection(collection))
            return true;
        setName(previousName);
        return false;
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
        var deleted = node is CollectionNodeViewModel c ? _workspace.DeleteCollection(c.Model) : _workspace.DeleteNode(node.Id);
        if (deleted)
            _host.CloseTabsFor(ids);
    }

    [RelayCommand]
    private async Task Export(TreeNodeViewModel? node)
    {
        if (node?.Root is not { } root)
            return;
        if (InheritedHeaderScan.HasTickedHeaders(root.Model) && !await _dialogs.ConfirmExportWithInheritedHeadersAsync())
            return;
        var model = ModelCloner.Clone(root.Model);
        if (SecretFields.Strip(model).Count > 0 && await _dialogs.AskIncludeSecretsAsync())
            model = root.Model;
        var fileName = $"{root.Model.Name}.postman_collection.json";
        if (await _dialogs.SaveFileAsync("Export collection (Postman v2.1)", fileName, ".json") is { } path)
            await WriteExportAsync(model, path);
    }

    /// <summary>Writes the Postman export of <paramref name="model"/> to <paramref name="path"/>. When that fails the reason is shown and this returns false.</summary>
    public async Task<bool> WriteExportAsync(CollectionModel model, string path)
    {
        var json = PostmanCollectionExporter.Export(model);
        return await UserFileWriter.WriteAsync(_dialogs, "Couldn't export the collection", path, () => File.WriteAllTextAsync(path, json));
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
