using Avalonia.Threading;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.Core.Secrets;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>In-memory workspace (collections, environments, globals, history) backed by <see cref="WorkspaceRepository"/>.</summary>
public sealed class WorkspaceService : IDisposable
{
    // Fires a little after midnight so the new day has certainly started.
    private static readonly TimeSpan MidnightMargin = TimeSpan.FromSeconds(1);

    private readonly WorkspaceRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _dayChangeTimer;

    // History is written in the background, in order, so a clear always lands after the appends before it.
    private readonly BackgroundWriter _historyWriter = new();

    private EnvironmentModel? _activeEnvironment;
    private bool _disposed;
    private bool _sessionFailureReported;

    public WorkspaceService(WorkspaceRepository repository, SettingsService settings, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _historyWriter.Failed += ex => StorageProblem?.Invoke(
            StorageErrorDescriber.Describe(ex, Path.Combine(repository.Root, "history"), "History", StorageOperation.Write));
        Settings = settings;
        Collections = repository.LoadCollections();
        Environments = repository.LoadEnvironments();
        Globals = repository.LoadGlobals();
        History = new HistoryCollection(repository.LoadHistory());
        ApplyHistoryRetention();
        Settings.Changed += ApplyHistoryRetention;
        _dayChangeTimer = timeProvider.CreateTimer(_ => OnDayChanged(), null, UntilNextMidnight(), Timeout.InfiniteTimeSpan);
    }

    public SettingsService Settings { get; }
    public List<CollectionModel> Collections { get; }
    public List<EnvironmentModel> Environments { get; }
    public EnvironmentModel Globals { get; }
    public HistoryCollection History { get; }
    public IReadOnlyList<StorageError> LoadErrors => _repository.LoadErrors;
    public string DataDirectory => _repository.Root;
    public SecretStore Secrets => _repository.Secrets;

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

    /// <summary>Something couldn't be read or written. May be raised on a background thread.</summary>
    public event Action<StorageError>? StorageProblem;

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
    //
    // A write that can fail either reports through StorageProblem (the bool methods) or returns the error for the
    // caller to present (the Try methods, which tabs use to offer Retry). In-memory state changes only after the
    // write succeeded, or is put back when it didn't, so the sidebar never shows what isn't on disk.

    /// <summary>
    /// Creates a collection and saves it. When it can't be written the failure is reported and the collection is not
    /// in <see cref="Collections"/>; use <see cref="AddCollection"/> when the caller must know.
    /// </summary>
    public CollectionModel CreateCollection(string name)
    {
        var collection = new CollectionModel { Name = name };
        AddCollection(collection);
        return collection;
    }

    /// <summary>Saves <paramref name="collection"/> and adds it to the list. False, after reporting it, when it couldn't be written.</summary>
    public bool AddCollection(CollectionModel collection) => !Report(TryAddCollection(collection));

    /// <summary>Like <see cref="AddCollection"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public StorageError? TryAddCollection(CollectionModel collection)
    {
        if (TrySaveCollectionFile(collection) is { } error)
            return error;
        Collections.Add(collection);
        Collections.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        CollectionsListChanged?.Invoke();
        return null;
    }

    /// <summary>Writes <paramref name="collection"/>. False, after reporting it, when it couldn't be written.</summary>
    public bool SaveCollection(CollectionModel collection) => !Report(TrySaveCollection(collection));

    /// <summary>Like <see cref="SaveCollection"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public StorageError? TrySaveCollection(CollectionModel collection)
    {
        if (TrySaveCollectionFile(collection) is { } error)
            return error;
        CollectionChanged?.Invoke(collection);
        VariablesChanged?.Invoke();
        return null;
    }

    /// <summary>Deletes a collection. False, after reporting it, when its file couldn't be deleted; it stays in the list.</summary>
    public bool DeleteCollection(CollectionModel collection)
    {
        var error = Attempt(() => _repository.DeleteCollection(collection.Id), _repository.CollectionFile(collection.Id),
            CollectionSubject(collection), StorageOperation.Delete);
        if (Report(error))
            return false;
        Collections.Remove(collection);
        DeleteResponsesFor(collection.Items);
        CollectionsListChanged?.Invoke();
        return true;
    }

    /// <summary>Adds <paramref name="node"/> to a collection root or a folder. False, after reporting it, when it couldn't be saved.</summary>
    public bool AddNode(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1) =>
        !Report(TryAddNode(collection, folder, node, index));

    /// <summary>Like <see cref="AddNode"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public StorageError? TryAddNode(CollectionModel collection, FolderModel? folder, CollectionNode node, int index = -1)
    {
        var items = folder?.Items ?? collection.Items;
        var before = items.ToList();
        if (index < 0 || index > items.Count)
            items.Add(node);
        else
            items.Insert(index, node);
        var error = TrySaveCollection(collection);
        if (error is not null)
            Restore(items, before);
        return error;
    }

    /// <summary>Deletes a node. False, after reporting it, when the collection couldn't be saved; the node stays.</summary>
    public bool DeleteNode(Guid nodeId)
    {
        if (Find(nodeId) is not { } location)
            return true;
        var before = location.Container.ToList();
        location.Container.Remove(location.Node);
        if (Report(TrySaveCollection(location.Collection)))
        {
            Restore(location.Container, before);
            return false;
        }
        DeleteResponsesFor([location.Node]);
        return true;
    }

    // The node is already gone from the collection; saved responses that stay behind are only wasted space.
    private void DeleteResponsesFor(IEnumerable<CollectionNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is HttpRequestModel request)
                Report(Attempt(() => _repository.DeleteResponses(request.Id), _repository.ResponsesFolder(request.Id),
                    "Saved responses", StorageOperation.Delete));
            else if (node is FolderModel folder)
                DeleteResponsesFor(folder.Items);
        }
    }

    /// <summary>
    /// Moves a node into <paramref name="targetFolder"/> (or the collection root) at <paramref name="index"/>.
    /// Refuses to move a folder into itself or its descendants. False when refused, or when the move couldn't be
    /// saved (reported, and the node is back where it was).
    /// </summary>
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

        if (Report(TrySaveCollection(targetCollection)))
        {
            Restore(source.Container, sourceBefore);
            Restore(targetItems, targetBefore);
            return false;
        }
        if (source.Collection != targetCollection && Report(TrySaveCollection(source.Collection)))
        {
            Restore(source.Container, sourceBefore);
            Restore(targetItems, targetBefore);
            // The target's file already has the node; write it back without, so it isn't on disk twice.
            TrySaveCollection(targetCollection);
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

    private StorageError? TrySaveCollectionFile(CollectionModel collection) =>
        Attempt(() => _repository.SaveCollection(collection), _repository.CollectionFile(collection.Id),
            CollectionSubject(collection), StorageOperation.Write);

    private static string CollectionSubject(CollectionModel collection) => $"Collection \"{collection.Name}\"";

    // ---- environments ----

    /// <summary>
    /// Creates an environment and saves it. When it can't be written the failure is reported and the environment is not
    /// in <see cref="Environments"/>; use <see cref="AddEnvironment"/> when the caller must know.
    /// </summary>
    public EnvironmentModel CreateEnvironment(string name)
    {
        var env = new EnvironmentModel { Name = name };
        AddEnvironment(env);
        return env;
    }

    /// <summary>Saves <paramref name="environment"/> and adds it to the list. False, after reporting it, when it couldn't be written.</summary>
    public bool AddEnvironment(EnvironmentModel environment) => !Report(TryAddEnvironment(environment));

    /// <summary>Like <see cref="AddEnvironment"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public StorageError? TryAddEnvironment(EnvironmentModel environment)
    {
        var error = Attempt(() => _repository.SaveEnvironment(environment), _repository.EnvironmentFile(environment.Id),
            EnvironmentSubject(environment), StorageOperation.Write);
        if (error is not null)
            return error;
        Environments.Add(environment);
        Environments.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        EnvironmentsChanged?.Invoke();
        return null;
    }

    /// <summary>Writes <paramref name="environment"/> (or the globals). False, after reporting it, when it couldn't be written.</summary>
    public bool SaveEnvironment(EnvironmentModel environment) => !Report(TrySaveEnvironment(environment));

    /// <summary>Like <see cref="SaveEnvironment"/>, but returns the error instead of reporting it. Null when it worked.</summary>
    public StorageError? TrySaveEnvironment(EnvironmentModel environment)
    {
        var error = environment == Globals
            ? Attempt(() => _repository.SaveGlobals(environment), _repository.GlobalsFile, "Global variables", StorageOperation.Write)
            : Attempt(() => _repository.SaveEnvironment(environment), _repository.EnvironmentFile(environment.Id),
                EnvironmentSubject(environment), StorageOperation.Write);
        if (error is not null)
            return error;
        EnvironmentsChanged?.Invoke();
        VariablesChanged?.Invoke();
        return null;
    }

    /// <summary>Deletes an environment. False, after reporting it, when its file couldn't be deleted; it stays in the list.</summary>
    public bool DeleteEnvironment(EnvironmentModel environment)
    {
        var error = Attempt(() => _repository.DeleteEnvironment(environment.Id), _repository.EnvironmentFile(environment.Id),
            EnvironmentSubject(environment), StorageOperation.Delete);
        if (Report(error))
            return false;
        Environments.Remove(environment);
        if (ActiveEnvironment == environment)
            ActiveEnvironment = null;
        EnvironmentsChanged?.Invoke();
        return true;
    }

    private static string EnvironmentSubject(EnvironmentModel environment) => $"Environment \"{environment.Name}\"";

    // ---- reporting ----

    /// <summary>Runs a write and describes the failure of a file operation. Null when it worked.</summary>
    private static StorageError? Attempt(Action write, string path, string subject, StorageOperation operation)
    {
        try
        {
            write();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StorageErrorDescriber.Describe(ex, path, subject, operation);
        }
    }

    /// <summary>Raises <see cref="StorageProblem"/> for <paramref name="error"/> if there is one. True when there was.</summary>
    private bool Report(StorageError? error)
    {
        if (error is null)
            return false;
        StorageProblem?.Invoke(error);
        return true;
    }

    // ---- history ----

    public void AddHistory(HistoryEntry entry)
    {
        History.Insert(0, entry);
        _historyWriter.Enqueue(() => _repository.AppendHistory(entry));
    }

    public void DeleteHistory(HistoryEntry entry)
    {
        History.Remove(entry);
        _historyWriter.Enqueue(() => _repository.DeleteHistoryEntry(entry));
    }

    public void ClearHistory()
    {
        History.Clear();
        _historyWriter.Enqueue(_repository.ClearHistory);
    }

    /// <summary>
    /// Drops history older than the retention setting from the sidebar now and deletes its files on the history
    /// writer, after the appends already queued. Must run on the UI thread, which owns <see cref="History"/>.
    /// </summary>
    private void ApplyHistoryRetention()
    {
        var retentionDays = Settings.Current.HistoryRetentionDays;
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var oldestKeptDay = HistoryRetention.OldestKeptDay(retentionDays, today);
        History.RemoveWhere(entry => HistoryRetention.IsExpired(entry.Timestamp, oldestKeptDay));
        _historyWriter.Enqueue(() => _repository.PruneHistory(retentionDays));
    }

    private TimeSpan UntilNextMidnight()
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        return now.Date.AddDays(1) - now + MidnightMargin;
    }

    // The timer fires on a thread-pool thread; History belongs to the UI thread.
    private void OnDayChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;
            ApplyHistoryRetention();
            _dayChangeTimer.Change(UntilNextMidnight(), Timeout.InfiniteTimeSpan);
        });
    }

    public void Dispose()
    {
        _disposed = true;
        Settings.Changed -= ApplyHistoryRetention;
        _dayChangeTimer.Dispose();
    }

    /// <summary>Completes when queued history writes are on disk (or have failed).</summary>
    public Task FlushHistoryAsync() => _historyWriter.FlushAsync();

    // ---- saved responses ----

    /// <summary>Saved responses for a request, newest first. Files that can't be read are reported and skipped.</summary>
    public IReadOnlyList<SavedResponse> LoadResponses(Guid requestId)
    {
        var loaded = _repository.LoadResponses(requestId);
        foreach (var error in loaded.Errors)
            StorageProblem?.Invoke(error);
        return loaded.Items;
    }

    /// <summary>Saves a response with its request; returns what was stored, or null (after reporting it) if it couldn't be written.</summary>
    public SavedResponse? SaveResponse(Guid requestId, SavedResponse saved)
    {
        try
        {
            return _repository.SaveResponse(requestId, saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StorageProblem?.Invoke(StorageErrorDescriber.Describe(ex, Path.Combine(_repository.Root, "responses", requestId.ToString()),
                "The response", StorageOperation.Write));
            return null;
        }
    }

    // ---- session ----

    public SessionState LoadSession() => _repository.LoadSession();

    /// <summary>
    /// Saves the session. It runs on a timer, so a failure is reported once and stays quiet until a save has worked
    /// again; the session is only a convenience and never worth interrupting the user every few seconds.
    /// </summary>
    public void SaveSession(SessionState session)
    {
        var error = Attempt(() => _repository.SaveSession(session), _repository.SessionFile,
            "Session (open tabs and drafts)", StorageOperation.Write);
        if (error is null)
        {
            _sessionFailureReported = false;
            return;
        }
        if (_sessionFailureReported)
            return;
        _sessionFailureReported = true;
        Report(error);
    }
}
