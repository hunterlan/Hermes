using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.Core.Secrets;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// In-memory workspace (collections, environments, globals, history) backed by <see cref="WorkspaceRepository"/>.
/// The entry point the app uses: each area lives in a part of its own (<see cref="WorkspaceCollections"/>,
/// <see cref="WorkspaceEnvironments"/>, <see cref="WorkspaceHistory"/>, <see cref="WorkspaceSession"/>,
/// <see cref="WorkspaceResponses"/>), and every storage problem is reported by <see cref="WorkspaceStorage"/>.
/// </summary>
public sealed class WorkspaceService : IDisposable
{
    private readonly WorkspaceRepository _repository;
    private readonly WorkspaceStorage _storage = new();
    private readonly WorkspaceResponses _responses;
    private readonly WorkspaceCollections _collections;
    private readonly WorkspaceEnvironments _environments;
    private readonly WorkspaceHistory _history;
    private readonly WorkspaceSession _session;

    public WorkspaceService(WorkspaceRepository repository, SettingsService settings, TimeProvider timeProvider)
    {
        _repository = repository;
        Settings = settings;
        _responses = new WorkspaceResponses(repository, _storage);
        _collections = new WorkspaceCollections(repository, _storage, _responses);
        _environments = new WorkspaceEnvironments(repository, _storage);
        _history = new WorkspaceHistory(repository, _storage, settings, timeProvider);
        _session = new WorkspaceSession(repository, _storage);
    }

    public SettingsService Settings { get; }
    public List<CollectionModel> Collections => _collections.Items;
    public List<EnvironmentModel> Environments => _environments.Items;
    public EnvironmentModel Globals => _environments.Globals;
    public HistoryCollection History => _history.Entries;
    public IReadOnlyList<StorageError> LoadErrors => _repository.LoadErrors;
    public string DataDirectory => _repository.Root;
    public SecretStore Secrets => _repository.Secrets;

    public EnvironmentModel? ActiveEnvironment
    {
        get => _environments.Active;
        set => _environments.Active = value;
    }

    /// <summary>A collection's structure or settings changed (rename, add, delete, move, save).</summary>
    public event Action<CollectionModel>? CollectionChanged
    {
        add => _collections.CollectionChanged += value;
        remove => _collections.CollectionChanged -= value;
    }

    public event Action? CollectionsListChanged
    {
        add => _collections.CollectionsListChanged += value;
        remove => _collections.CollectionsListChanged -= value;
    }

    public event Action? EnvironmentsChanged
    {
        add => _environments.EnvironmentsChanged += value;
        remove => _environments.EnvironmentsChanged -= value;
    }

    /// <summary>Anything that can change variable resolution: environment selection, env / globals / collection variables.</summary>
    public event Action? VariablesChanged
    {
        add
        {
            _collections.VariablesChanged += value;
            _environments.VariablesChanged += value;
        }
        remove
        {
            _collections.VariablesChanged -= value;
            _environments.VariablesChanged -= value;
        }
    }

    /// <summary>Something couldn't be read or written. May be raised on a background thread.</summary>
    public event Action<StorageError>? StorageProblem
    {
        add => _storage.StorageProblem += value;
        remove => _storage.StorageProblem -= value;
    }

    // ---- lookup ----

    public NodeLocation? Find(Guid nodeId) => _collections.Find(nodeId);

    public CollectionModel? FindCollection(Guid id) => _collections.FindCollection(id);

    public EnvironmentModel? FindEnvironment(Guid id) => _environments.Find(id);

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
    //
    // Every write below runs off the UI thread, in order, from a copy of the model taken when it is called; the
    // returned task completes back on the caller's thread once the write is done. Call them from the UI thread,
    // which owns the models and the lists.

    /// <summary>
    /// Creates a collection and saves it. When it can't be written the failure is reported and the collection is not
    /// in <see cref="Collections"/>; use <see cref="AddCollectionAsync"/> when the caller must know.
    /// </summary>
    public Task<CollectionModel> CreateCollectionAsync(string name) => _collections.CreateAsync(name);

    /// <summary>Saves <paramref name="collection"/> and adds it to the list. False, after reporting it, when it couldn't be written.</summary>
    public Task<bool> AddCollectionAsync(CollectionModel collection) => _collections.AddAsync(collection);

    /// <summary>Like <see cref="AddCollectionAsync"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public Task<StorageError?> TryAddCollectionAsync(CollectionModel collection) => _collections.TryAddAsync(collection);

    /// <summary>Writes <paramref name="collection"/>. False, after reporting it, when it couldn't be written.</summary>
    /// <param name="failure">How to word a failure when the save is part of something else, such as a rename; null for the plain wording.</param>
    public Task<bool> SaveCollectionAsync(CollectionModel collection, SaveFailureWording? failure = null) =>
        _collections.SaveAsync(collection, failure);

    /// <summary>Like <see cref="SaveCollectionAsync"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public Task<StorageError?> TrySaveCollectionAsync(CollectionModel collection, SaveFailureWording? failure = null) =>
        _collections.TrySaveAsync(collection, failure);

    /// <summary>Deletes a collection. False, after reporting it, when its file couldn't be deleted; it stays in the list.</summary>
    public Task<bool> DeleteCollectionAsync(CollectionModel collection) => _collections.DeleteAsync(collection);

    /// <summary>Adds <paramref name="node"/> to a collection root or a folder. False, after reporting it, when it couldn't be saved.</summary>
    public Task<bool> AddNodeAsync(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1) =>
        _collections.AddNodeAsync(collection, folder, node, index);

    /// <summary>Like <see cref="AddNodeAsync"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public Task<StorageError?> TryAddNodeAsync(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1) =>
        _collections.TryAddNodeAsync(collection, folder, node, index);

    /// <summary>Deletes a node. False, after reporting it, when the collection couldn't be saved; the node stays.</summary>
    public Task<bool> DeleteNodeAsync(Guid nodeId) => _collections.DeleteNodeAsync(nodeId);

    /// <summary>
    /// Moves a node into <paramref name="targetFolder"/> (or the collection root) at <paramref name="index"/>.
    /// Refuses to move a folder into itself or its descendants. False when refused, or when the move couldn't be
    /// saved (reported, and the node is back where it was).
    /// </summary>
    public Task<bool> MoveNodeAsync(Guid nodeId, CollectionModel targetCollection, FolderModel? targetFolder, int index) =>
        _collections.MoveNodeAsync(nodeId, targetCollection, targetFolder, index);

    // ---- environments ----

    /// <summary>
    /// Creates an environment and saves it. When it can't be written the failure is reported and the environment is not
    /// in <see cref="Environments"/>; use <see cref="AddEnvironmentAsync"/> when the caller must know.
    /// </summary>
    public Task<EnvironmentModel> CreateEnvironmentAsync(string name) => _environments.CreateAsync(name);

    /// <summary>Saves <paramref name="environment"/> and adds it to the list. False, after reporting it, when it couldn't be written.</summary>
    public Task<bool> AddEnvironmentAsync(EnvironmentModel environment) => _environments.AddAsync(environment);

    /// <summary>Like <see cref="AddEnvironmentAsync"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public Task<StorageError?> TryAddEnvironmentAsync(EnvironmentModel environment) => _environments.TryAddAsync(environment);

    /// <summary>Writes <paramref name="environment"/> (or the globals). False, after reporting it, when it couldn't be written.</summary>
    public Task<bool> SaveEnvironmentAsync(EnvironmentModel environment) => _environments.SaveAsync(environment);

    /// <summary>Like <see cref="SaveEnvironmentAsync"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public Task<StorageError?> TrySaveEnvironmentAsync(EnvironmentModel environment) => _environments.TrySaveAsync(environment);

    /// <summary>Deletes an environment. False, after reporting it, when its file couldn't be deleted; it stays in the list.</summary>
    public Task<bool> DeleteEnvironmentAsync(EnvironmentModel environment) => _environments.DeleteAsync(environment);

    // ---- history ----

    public void AddHistory(HistoryEntry entry) => _history.Add(entry);

    public void DeleteHistory(HistoryEntry entry) => _history.Delete(entry);

    public void ClearHistory() => _history.Clear();

    /// <summary>Completes when queued history writes are on disk (or have failed).</summary>
    public Task FlushHistoryAsync() => _history.FlushAsync();

    // ---- saved responses ----

    /// <summary>Saved responses for a request, newest first. Files that can't be read are reported and skipped.</summary>
    public IReadOnlyList<SavedResponse> LoadResponses(Guid requestId) => _responses.Load(requestId);

    /// <summary>Saves a response with its request; returns what was stored, or null (after reporting it) if it couldn't be written.</summary>
    public SavedResponse? SaveResponse(Guid requestId, SavedResponse saved) => _responses.Save(requestId, saved);

    // ---- session ----

    public SessionState LoadSession() => _session.Load();

    /// <summary>
    /// Saves the session, off the UI thread like the other writes. It runs on a timer, so a failure is reported once
    /// and stays quiet until a save has worked again; the session is only a convenience and never worth interrupting
    /// the user every few seconds.
    /// </summary>
    public Task SaveSessionAsync(SessionState session) => _session.SaveAsync(session);

    /// <summary>Like <see cref="SaveSessionAsync"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public Task<StorageError?> TrySaveSessionAsync(SessionState session) => _session.TrySaveAsync(session);

    /// <summary>Completes when the writes queued so far (collections, environments, session) are done.</summary>
    public Task FlushWritesAsync() => _storage.FlushAsync();

    /// <summary>
    /// Stops the history timer and waits for the queued writes, so nothing asked for is lost. Blocking is deliberate:
    /// it runs as the app exits, when the process may end as soon as this returns. The writes never need the UI
    /// thread, so waiting on it can't deadlock.
    /// </summary>
    public void Dispose()
    {
        _history.Dispose();
        _storage.FlushAsync().Wait();
    }
}
