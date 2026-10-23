using System.Collections.ObjectModel;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Secrets;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>Location of a node inside a collection.</summary>
public sealed record NodeLocation(CollectionModel Collection, IReadOnlyList<FolderModel> Ancestors, List<CollectionNode> Container, CollectionNode Node)
{
    public FolderModel? Parent => Ancestors.Count > 0 ? Ancestors[^1] : null;
}

/// <summary>In-memory workspace (collections, environments, globals, history) backed by <see cref="WorkspaceRepository"/>.</summary>
public sealed class WorkspaceService
{
    private readonly WorkspaceRepository _repository;

    public WorkspaceService(WorkspaceRepository repository, SettingsService settings)
    {
        _repository = repository;
        Settings = settings;
        Collections = repository.LoadCollections();
        Environments = repository.LoadEnvironments();
        Globals = repository.LoadGlobals();
        History = new ObservableCollection<HistoryEntry>(repository.LoadHistory(settings.Current.HistoryRetentionDays));
    }

    public SettingsService Settings { get; }
    public List<CollectionModel> Collections { get; }
    public List<EnvironmentModel> Environments { get; }
    public EnvironmentModel Globals { get; }
    public ObservableCollection<HistoryEntry> History { get; }
    public IReadOnlyList<string> LoadErrors => _repository.LoadErrors;
    public string DataDirectory => _repository.Root;
    public SecretStore Secrets => _repository.Secrets;

    private EnvironmentModel? _activeEnvironment;

    public EnvironmentModel? ActiveEnvironment
    {
        get => _activeEnvironment;
        set
        {
            if (_activeEnvironment == value)
                return;
            _activeEnvironment = value;
            VariablesChanged?.Invoke();
        }
    }

    /// <summary>A collection's structure or settings changed (rename, add, delete, move, save).</summary>
    public event Action<CollectionModel>? CollectionChanged;
    public event Action? CollectionsListChanged;
    public event Action? EnvironmentsChanged;

    /// <summary>Anything that can change variable resolution: environment selection, env / globals / collection variables.</summary>
    public event Action? VariablesChanged;

    // ---- lookup ----

    public NodeLocation? Find(Guid nodeId)
    {
        foreach (var collection in Collections)
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

    public CollectionModel? FindCollection(Guid id) => Collections.FirstOrDefault(c => c.Id == id);

    public EnvironmentModel? FindEnvironment(Guid id) =>
        id == Globals.Id ? Globals : Environments.FirstOrDefault(e => e.Id == id);

    /// <summary>Builds the context for sending a request (persisted or not).</summary>
    public RequestContext CreateContext(Guid? requestId)
    {
        var location = requestId is { } id ? Find(id) : null;
        return new RequestContext
        {
            Collection = location?.Collection,
            Folders = location?.Ancestors ?? [],
            Environment = ActiveEnvironment,
            Globals = Globals,
            Settings = Settings.Current,
        };
    }

    /// <summary>Context for a folder or collection settings tab (auth inheritance / variables).</summary>
    public RequestContext CreateContextForContainer(CollectionModel collection, FolderModel? folder)
    {
        var ancestors = new List<FolderModel>();
        if (folder is not null && Find(folder.Id) is { } location)
            ancestors.AddRange(location.Ancestors);
        return new RequestContext
        {
            Collection = collection,
            Folders = ancestors,
            Environment = ActiveEnvironment,
            Globals = Globals,
            Settings = Settings.Current,
        };
    }

    // ---- collections ----

    public CollectionModel CreateCollection(string name)
    {
        var collection = new CollectionModel { Name = name };
        AddCollection(collection);
        return collection;
    }

    public void AddCollection(CollectionModel collection)
    {
        Collections.Add(collection);
        Collections.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        _repository.SaveCollection(collection);
        CollectionsListChanged?.Invoke();
    }

    public void SaveCollection(CollectionModel collection)
    {
        _repository.SaveCollection(collection);
        CollectionChanged?.Invoke(collection);
        VariablesChanged?.Invoke();
    }

    public void DeleteCollection(CollectionModel collection)
    {
        Collections.Remove(collection);
        _repository.DeleteCollection(collection.Id);
        DeleteResponsesFor(collection.Items);
        CollectionsListChanged?.Invoke();
    }

    /// <summary>Adds <paramref name="node"/> to a collection root or a folder.</summary>
    public void AddNode(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1)
    {
        var items = folder?.Items ?? collection.Items;
        if (index < 0 || index > items.Count)
            items.Add(node);
        else
            items.Insert(index, node);
        SaveCollection(collection);
    }

    public void DeleteNode(Guid nodeId)
    {
        if (Find(nodeId) is not { } location)
            return;
        location.Container.Remove(location.Node);
        SaveCollection(location.Collection);
        DeleteResponsesFor([location.Node]);
    }

    private void DeleteResponsesFor(IEnumerable<CollectionNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is HttpRequestModel request)
                _repository.DeleteResponses(request.Id);
            else if (node is FolderModel folder)
                DeleteResponsesFor(folder.Items);
        }
    }

    /// <summary>
    /// Moves a node into <paramref name="targetFolder"/> (or the collection root) at <paramref name="index"/>.
    /// Refuses to move a folder into itself or its descendants.
    /// </summary>
    public bool MoveNode(Guid nodeId, CollectionModel targetCollection, FolderModel? targetFolder, int index)
    {
        if (Find(nodeId) is not { } source)
            return false;
        if (source.Node is FolderModel movingFolder && targetFolder is not null &&
            (targetFolder == movingFolder || IsDescendant(movingFolder, targetFolder)))
            return false;

        var targetItems = targetFolder?.Items ?? targetCollection.Items;
        var oldIndex = source.Container.IndexOf(source.Node);
        source.Container.RemoveAt(oldIndex);
        if (ReferenceEquals(source.Container, targetItems) && oldIndex < index)
            index--;
        index = Math.Clamp(index, 0, targetItems.Count);
        targetItems.Insert(index, source.Node);

        SaveCollection(targetCollection);
        if (source.Collection != targetCollection)
            SaveCollection(source.Collection);
        return true;
    }

    private static bool IsDescendant(FolderModel ancestor, FolderModel candidate) =>
        ancestor.Items.OfType<FolderModel>().Any(f => f == candidate || IsDescendant(f, candidate));

    // ---- environments ----

    public EnvironmentModel CreateEnvironment(string name)
    {
        var env = new EnvironmentModel { Name = name };
        AddEnvironment(env);
        return env;
    }

    public void AddEnvironment(EnvironmentModel environment)
    {
        Environments.Add(environment);
        Environments.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        _repository.SaveEnvironment(environment);
        EnvironmentsChanged?.Invoke();
    }

    public void SaveEnvironment(EnvironmentModel environment)
    {
        if (environment == Globals)
            _repository.SaveGlobals(environment);
        else
            _repository.SaveEnvironment(environment);
        EnvironmentsChanged?.Invoke();
        VariablesChanged?.Invoke();
    }

    public void DeleteEnvironment(EnvironmentModel environment)
    {
        Environments.Remove(environment);
        _repository.DeleteEnvironment(environment.Id);
        if (ActiveEnvironment == environment)
            ActiveEnvironment = null;
        EnvironmentsChanged?.Invoke();
    }

    // ---- history ----

    public void AddHistory(HistoryEntry entry)
    {
        History.Insert(0, entry);
        _ = Task.Run(() =>
        {
            try { _repository.AppendHistory(entry); }
            catch (IOException) { /* history is best effort */ }
        });
    }

    public void DeleteHistory(HistoryEntry entry)
    {
        History.Remove(entry);
        _repository.DeleteHistoryEntry(entry);
    }

    public void ClearHistory()
    {
        History.Clear();
        _repository.ClearHistory();
    }

    // ---- saved responses ----

    public List<SavedResponse> LoadResponses(Guid requestId) => _repository.LoadResponses(requestId);

    /// <summary>Saves a response with its request; returns the stored list (newest first), or null if it couldn't be written.</summary>
    public List<SavedResponse>? SaveResponse(Guid requestId, SavedResponse saved)
    {
        try
        {
            return _repository.SaveResponse(requestId, saved);
        }
        catch (IOException)
        {
            return null;
        }
    }

    // ---- session ----

    public SessionState LoadSession() => _repository.LoadSession();

    public void SaveSession(SessionState session) => _repository.SaveSession(session);
}
