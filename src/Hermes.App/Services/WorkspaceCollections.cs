using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The collections of the workspace and the nodes in them.
/// A write that can fail either reports through <see cref="WorkspaceStorage"/> (the bool methods) or returns the
/// error for the caller to present (the Try methods, which tabs use to offer Retry). In-memory state changes only
/// after the write succeeded, or is put back when it didn't, so the sidebar never shows what isn't on disk.
/// </summary>
internal sealed class WorkspaceCollections
{
    private readonly WorkspaceRepository _repository;
    private readonly WorkspaceStorage _storage;
    private readonly WorkspaceResponses _responses;

    public WorkspaceCollections(WorkspaceRepository repository, WorkspaceStorage storage, WorkspaceResponses responses)
    {
        _repository = repository;
        _storage = storage;
        _responses = responses;
        Items = repository.LoadCollections();
    }

    public List<CollectionModel> Items { get; }

    /// <summary>A collection's structure or settings changed (rename, add, delete, move, save).</summary>
    public event Action<CollectionModel>? CollectionChanged;
    public event Action? CollectionsListChanged;

    /// <summary>A collection was saved, so its variables may have changed.</summary>
    public event Action? VariablesChanged;

    // ---- lookup ----

    public NodeLocation? Find(Guid nodeId)
    {
        foreach (var collection in Items)
            if (Find(collection, collection.Items, [], nodeId) is { } location)
                return location;
        return null;
    }

    private static NodeLocation? Find(CollectionModel collection, List<CollectionNode> items, List<FolderModel> ancestors, Guid id)
    {
        foreach (var node in items)
        {
            if (node.Id == id)
                return new NodeLocation(collection, ancestors.ToList(), items, node);
            if (node is FolderModel folder)
            {
                ancestors.Add(folder);
                var found = Find(collection, folder.Items, ancestors, id);
                ancestors.RemoveAt(ancestors.Count - 1);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    public CollectionModel? FindCollection(Guid id) => Items.FirstOrDefault(c => c.Id == id);

    // ---- writes ----

    /// <inheritdoc cref="WorkspaceService.CreateCollection"/>
    public CollectionModel Create(string name)
    {
        var collection = new CollectionModel { Name = name };
        Add(collection);
        return collection;
    }

    /// <inheritdoc cref="WorkspaceService.AddCollection"/>
    public bool Add(CollectionModel collection) => !_storage.Report(TryAdd(collection));

    /// <inheritdoc cref="WorkspaceService.TryAddCollection"/>
    public StorageError? TryAdd(CollectionModel collection)
    {
        if (TrySaveFile(collection) is { } error)
            return error;
        Items.Add(collection);
        Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        CollectionsListChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.SaveCollection"/>
    public bool Save(CollectionModel collection, SaveFailureWording? failure = null) =>
        !_storage.Report(TrySave(collection, failure));

    /// <inheritdoc cref="WorkspaceService.TrySaveCollection"/>
    public StorageError? TrySave(CollectionModel collection, SaveFailureWording? failure = null)
    {
        if (TrySaveFile(collection, failure) is { } error)
            return error;
        CollectionChanged?.Invoke(collection);
        VariablesChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.DeleteCollection"/>
    public bool Delete(CollectionModel collection)
    {
        var error = WorkspaceStorage.Attempt(() => _repository.DeleteCollection(collection.Id),
            _repository.CollectionFile(collection.Id), StorageSubjects.Collection(collection), StorageOperation.Delete);
        if (_storage.Report(error))
            return false;
        Items.Remove(collection);
        _responses.DeleteFor(collection.Items);
        CollectionsListChanged?.Invoke();
        return true;
    }

    /// <inheritdoc cref="WorkspaceService.AddNode"/>
    public bool AddNode(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1) =>
        !_storage.Report(TryAddNode(collection, folder, node, index));

    /// <inheritdoc cref="WorkspaceService.TryAddNode"/>
    public StorageError? TryAddNode(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1)
    {
        var items = folder?.Items ?? collection.Items;
        var before = items.ToList();
        if (index < 0 || index > items.Count)
            items.Add(node);
        else
            items.Insert(index, node);
        var error = TrySave(collection);
        if (error is not null)
            Restore(items, before);
        return error;
    }

    /// <inheritdoc cref="WorkspaceService.DeleteNode"/>
    public bool DeleteNode(Guid nodeId)
    {
        if (Find(nodeId) is not { } location)
            return true;
        var before = location.Container.ToList();
        location.Container.Remove(location.Node);
        if (_storage.Report(TrySave(location.Collection)))
        {
            Restore(location.Container, before);
            return false;
        }
        _responses.DeleteFor([location.Node]);
        return true;
    }

    /// <inheritdoc cref="WorkspaceService.MoveNode"/>
    public bool MoveNode(Guid nodeId, CollectionModel targetCollection, FolderModel? targetFolder, int index)
    {
        if (Find(nodeId) is not { } source)
            return false;
        if (source.Node is FolderModel movingFolder && targetFolder is not null &&
            (targetFolder == movingFolder || IsDescendant(movingFolder, targetFolder)))
            return false;

        var targetItems = targetFolder?.Items ?? targetCollection.Items;
        var sourceBefore = source.Container.ToList();
        var targetBefore = targetItems.ToList();
        var oldIndex = source.Container.IndexOf(source.Node);
        source.Container.RemoveAt(oldIndex);
        if (ReferenceEquals(source.Container, targetItems) && oldIndex < index)
            index--;
        index = Math.Clamp(index, 0, targetItems.Count);
        targetItems.Insert(index, source.Node);

        if (_storage.Report(TrySave(targetCollection)))
        {
            Restore(source.Container, sourceBefore);
            Restore(targetItems, targetBefore);
            return false;
        }
        if (source.Collection != targetCollection && _storage.Report(TrySave(source.Collection)))
        {
            Restore(source.Container, sourceBefore);
            Restore(targetItems, targetBefore);
            // The target's file already has the node; write it back without, so it isn't on disk twice.
            TrySave(targetCollection);
            return false;
        }
        return true;
    }

    private static void Restore(List<CollectionNode> items, List<CollectionNode> before)
    {
        items.Clear();
        items.AddRange(before);
    }

    private static bool IsDescendant(FolderModel ancestor, FolderModel candidate) =>
        ancestor.Items.OfType<FolderModel>().Any(f => f == candidate || IsDescendant(f, candidate));

    private StorageError? TrySaveFile(CollectionModel collection, SaveFailureWording? failure = null) =>
        WorkspaceStorage.Attempt(() => _repository.SaveCollection(collection), _repository.CollectionFile(collection.Id),
            failure?.Subject ?? StorageSubjects.Collection(collection), StorageOperation.Write, failure?.FailedAction);
}
