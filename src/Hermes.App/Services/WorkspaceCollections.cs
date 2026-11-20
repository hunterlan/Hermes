using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The collections of the workspace and the nodes in them.
/// A write that can fail either reports through <see cref="WorkspaceStorage"/> (the bool methods) or returns the
/// error for the caller to present (the Try methods, which tabs use to offer Retry). In-memory state changes only
/// after the write succeeded, or is put back when it didn't, so the sidebar never shows what isn't on disk.
/// The file is written off the UI thread from a copy taken when the write is asked for, so later edits can't race it.
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

    /// <inheritdoc cref="WorkspaceService.CreateCollectionAsync"/>
    public async Task<CollectionModel> CreateAsync(string name)
    {
        var collection = new CollectionModel { Name = name };
        await AddAsync(collection);
        return collection;
    }

    /// <inheritdoc cref="WorkspaceService.AddCollectionAsync"/>
    public async Task<bool> AddAsync(CollectionModel collection) => !_storage.Report(await TryAddAsync(collection));

    /// <inheritdoc cref="WorkspaceService.TryAddCollectionAsync"/>
    public async Task<StorageError?> TryAddAsync(CollectionModel collection)
    {
        if (await TrySaveFileAsync(collection) is { } error)
            return error;
        Items.Add(collection);
        Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        CollectionsListChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.SaveCollectionAsync"/>
    public async Task<bool> SaveAsync(CollectionModel collection, SaveFailureWording? failure = null) =>
        !_storage.Report(await TrySaveAsync(collection, failure));

    /// <inheritdoc cref="WorkspaceService.TrySaveCollectionAsync"/>
    public async Task<StorageError?> TrySaveAsync(CollectionModel collection, SaveFailureWording? failure = null)
    {
        if (await TrySaveFileAsync(collection, failure) is { } error)
            return error;
        CollectionChanged?.Invoke(collection);
        VariablesChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.DeleteCollectionAsync"/>
    public async Task<bool> DeleteAsync(CollectionModel collection)
    {
        var id = collection.Id;
        var error = await _storage.AttemptAsync(() => _repository.DeleteCollection(id),
            _repository.CollectionFile(id), StorageSubjects.Collection(collection), StorageOperation.Delete);
        if (_storage.Report(error))
            return false;
        Items.Remove(collection);
        var deletedResponses = _responses.DeleteForAsync(collection.Items);
        CollectionsListChanged?.Invoke();
        await deletedResponses;
        return true;
    }

    /// <inheritdoc cref="WorkspaceService.AddNodeAsync"/>
    public async Task<bool> AddNodeAsync(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1) =>
        !_storage.Report(await TryAddNodeAsync(collection, folder, node, index));

    /// <inheritdoc cref="WorkspaceService.TryAddNodeAsync"/>
    public async Task<StorageError?> TryAddNodeAsync(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1)
    {
        var items = folder?.Items ?? collection.Items;
        var before = items.ToList();
        if (index < 0 || index > items.Count)
            items.Add(node);
        else
            items.Insert(index, node);
        var error = await TrySaveAsync(collection);
        if (error is not null)
            Restore(items, before);
        return error;
    }

    /// <inheritdoc cref="WorkspaceService.DeleteNodeAsync"/>
    public async Task<bool> DeleteNodeAsync(Guid nodeId)
    {
        if (Find(nodeId) is not { } location)
            return true;
        var before = location.Container.ToList();
        location.Container.Remove(location.Node);
        if (_storage.Report(await TrySaveAsync(location.Collection)))
        {
            Restore(location.Container, before);
            return false;
        }
        await _responses.DeleteForAsync([location.Node]);
        return true;
    }

    /// <inheritdoc cref="WorkspaceService.MoveNodeAsync"/>
    public async Task<bool> MoveNodeAsync(Guid nodeId, CollectionModel targetCollection, FolderModel? targetFolder, int index)
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

        if (_storage.Report(await TrySaveAsync(targetCollection)))
        {
            Restore(source.Container, sourceBefore);
            Restore(targetItems, targetBefore);
            return false;
        }
        if (source.Collection != targetCollection && _storage.Report(await TrySaveAsync(source.Collection)))
        {
            Restore(source.Container, sourceBefore);
            Restore(targetItems, targetBefore);
            // The target's file already has the node; write it back without, so it isn't on disk twice.
            await TrySaveAsync(targetCollection);
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

    // The copy is taken here, on the UI thread; the queue writes it later.
    private Task<StorageError?> TrySaveFileAsync(CollectionModel collection, SaveFailureWording? failure = null)
    {
        var snapshot = ModelCloner.Clone(collection);
        return _storage.AttemptAsync(() => _repository.SaveCollection(snapshot), _repository.CollectionFile(collection.Id),
            failure?.Subject ?? StorageSubjects.Collection(collection), StorageOperation.Write, failure?.FailedAction);
    }
}
