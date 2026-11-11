using System.Text.Json;
using Hermes.Core.Models;
using Hermes.Core.Secrets;
using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>
/// File layout under the data directory:
/// <code>
/// collections/{id}.json
/// environments/{id}.json
/// globals.json
/// history/yyyy-MM-dd.jsonl          (JSON Lines, one entry per line)
/// responses/{requestId}/{sequence}.json  (one file per saved response)
/// session.json
/// </code>
/// Secret variables and literal auth credentials go to <see cref="Secrets"/> instead of these files while it can
/// protect them; history keeps no credentials at all.
/// </summary>
public sealed class WorkspaceRepository(string root, SecretStore? secrets = null, TimeProvider? timeProvider = null)
{
    /// <inheritdoc cref="SavedResponseStore.MaxResponsesPerRequest"/>
    public const int MaxSavedResponses = SavedResponseStore.MaxResponsesPerRequest;

    /// <inheritdoc cref="SavedResponseStore.MaxBodyBytes"/>
    public const long MaxSavedBodyBytes = SavedResponseStore.MaxBodyBytes;

    public const string CollectionsFolderName = "collections";
    public const string EnvironmentsFolderName = "environments";
    public const string HistoryFolderName = "history";
    public const string ResponsesFolderName = "responses";

    private const string GlobalsOwner = "globals";
    private const string SessionOwner = "session";
    private static readonly Dictionary<string, string> NoSecrets = [];

    private readonly HistoryStore _history = new(Path.Combine(root, HistoryFolderName), timeProvider);
    private readonly SavedResponseStore _responses = new(Path.Combine(root, ResponsesFolderName));

    public string Root { get; } = root;

    public SecretStore Secrets { get; } = secrets ?? SecretStore.Unprotected("No secret store configured.");

    private string CollectionsDir => Path.Combine(Root, CollectionsFolderName);
    private string EnvironmentsDir => Path.Combine(Root, EnvironmentsFolderName);

    /// <summary>The file holding the global variables.</summary>
    public string GlobalsFile => Path.Combine(Root, "globals.json");

    /// <summary>The file holding the open tabs and drafts.</summary>
    public string SessionFile => Path.Combine(Root, "session.json");

    /// <summary>The file a collection is saved in.</summary>
    public string CollectionFile(Guid id) => Path.Combine(CollectionsDir, $"{id}.json");

    /// <summary>The file an environment is saved in.</summary>
    public string EnvironmentFile(Guid id) => Path.Combine(EnvironmentsDir, $"{id}.json");

    /// <summary>The folder holding the saved responses of a request.</summary>
    public string ResponsesFolder(Guid requestId) =>
        Path.Combine(Root, ResponsesFolderName, requestId.ToString());

    /// <summary>Files that failed to load, with the reason. Shown to the user instead of crashing.</summary>
    public List<StorageError> LoadErrors { get; } = [];

    // ---- collections ----

    public List<CollectionModel> LoadCollections() =>
        LoadAll(CollectionsDir, DataFolderCheck.CollectionsSubject, "Collection", p => JsonFileStore.Read(p, HermesJsonContext.Default.CollectionModel))
            .Select(c =>
            {
                SecretFields.Restore(c, Secrets.Get(CollectionOwner(c.Id)));
                return c;
            })
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void SaveCollection(CollectionModel collection) =>
        JsonFileStore.Write(CollectionFile(collection.Id),
            WithoutSecrets(collection, ModelCloner.Clone, SecretFields.Strip, CollectionOwner(collection.Id)),
            HermesJsonContext.Default.CollectionModel);

    public void DeleteCollection(Guid id)
    {
        DeleteFile(CollectionFile(id));
        Secrets.Set(CollectionOwner(id), NoSecrets);
    }

    // ---- environments ----

    public List<EnvironmentModel> LoadEnvironments() =>
        LoadAll(EnvironmentsDir, DataFolderCheck.EnvironmentsSubject, "Environment", p => JsonFileStore.Read(p, HermesJsonContext.Default.EnvironmentModel))
            .Select(e =>
            {
                SecretFields.Restore(e, Secrets.Get(EnvironmentOwner(e.Id)));
                return e;
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void SaveEnvironment(EnvironmentModel environment) =>
        JsonFileStore.Write(EnvironmentFile(environment.Id),
            WithoutSecrets(environment, ModelCloner.Clone, SecretFields.Strip, EnvironmentOwner(environment.Id)),
            HermesJsonContext.Default.EnvironmentModel);

    public void DeleteEnvironment(Guid id)
    {
        DeleteFile(EnvironmentFile(id));
        Secrets.Set(EnvironmentOwner(id), NoSecrets);
    }

    public EnvironmentModel LoadGlobals()
    {
        try
        {
            var globals = JsonFileStore.Read(GlobalsFile, HermesJsonContext.Default.EnvironmentModel)
                          ?? new EnvironmentModel { Name = "Globals" };
            SecretFields.Restore(globals, Secrets.Get(GlobalsOwner));
            return globals;
        }
        catch (Exception ex)
        {
            LoadErrors.Add(StorageErrorDescriber.Describe(ex, GlobalsFile, "Global variables"));
            return new EnvironmentModel { Name = "Globals" };
        }
    }

    public void SaveGlobals(EnvironmentModel globals) =>
        JsonFileStore.Write(GlobalsFile, WithoutSecrets(globals, ModelCloner.Clone, SecretFields.Strip, GlobalsOwner),
            HermesJsonContext.Default.EnvironmentModel);

    // ---- history ----

    public List<HistoryEntry> LoadHistory() => _history.Load(LoadErrors);

    /// <inheritdoc cref="HistoryStore.Prune"/>
    public void PruneHistory(int retentionDays) => _history.Prune(retentionDays);

    /// <summary>Appends the entry without literal credentials; the in-memory <paramref name="entry"/> is left as is.</summary>
    public void AppendHistory(HistoryEntry entry)
    {
        var request = ModelCloner.Clone(entry.Request);
        SecretFields.Strip(request);
        entry = new HistoryEntry
        {
            Id = entry.Id,
            Timestamp = entry.Timestamp,
            Request = request,
            StatusCode = entry.StatusCode,
            DurationMs = entry.DurationMs,
        };
        _history.Append(entry);
    }

    public void DeleteHistoryEntry(HistoryEntry entry) => _history.Delete(entry);

    public void ClearHistory() => _history.Clear();

    // ---- saved responses ----

    /// <inheritdoc cref="SavedResponseStore.Load"/>
    public SavedResponses LoadResponses(Guid requestId) => _responses.Load(requestId);

    /// <inheritdoc cref="SavedResponseStore.Save"/>
    public SavedResponse SaveResponse(Guid requestId, SavedResponse saved) => _responses.Save(requestId, saved);

    public void DeleteResponses(Guid requestId) => _responses.Delete(requestId);

    // ---- session ----

    public SessionState LoadSession()
    {
        try
        {
            var session = JsonFileStore.Read(SessionFile, HermesJsonContext.Default.SessionState) ?? new SessionState();
            SecretFields.Restore(session, Secrets.Get(SessionOwner));
            return session;
        }
        catch (Exception ex)
        {
            LoadErrors.Add(StorageErrorDescriber.Describe(ex, SessionFile, "Session (open tabs and drafts)"));
            return new SessionState();
        }
    }

    public void SaveSession(SessionState session) =>
        JsonFileStore.Write(SessionFile, WithoutSecrets(session, CloneSession, SecretFields.Strip, SessionOwner),
            HermesJsonContext.Default.SessionState);

    private static SessionState CloneSession(SessionState session) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(session, HermesJsonContext.Default.SessionState),
            HermesJsonContext.Default.SessionState)!;

    // ---- secrets ----

    private static string CollectionOwner(Guid id) => $"collection/{id}";
    private static string EnvironmentOwner(Guid id) => $"environment/{id}";

    /// <summary>
    /// The copy of <paramref name="model"/> to write: without its secrets once they're in the store, or the model
    /// itself (secrets included) when the store can't protect them.
    /// </summary>
    private T WithoutSecrets<T>(T model, Func<T, T> clone, Func<T, Dictionary<string, string>> strip, string owner)
    {
        if (!Secrets.IsProtected)
            return model;
        var copy = clone(model);
        Secrets.Set(owner, strip(copy));
        return Secrets.IsProtected ? copy : model;
    }

    // ---- helpers ----

    /// <param name="folderSubject">How <paramref name="dir"/> is named when it can't be listed.</param>
    /// <param name="label">What the files hold (<c>Collection</c>, <c>Environment</c>), to name the one that failed.</param>
    private List<T> LoadAll<T>(string dir, string folderSubject, string label, Func<string, T?> read) where T : class
    {
        var list = new List<T>();
        if (!Directory.Exists(dir))
            return list;
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.json").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadErrors.Add(StorageErrorDescriber.Describe(ex, dir, $"{folderSubject} \"{dir}\""));
            return list;
        }
        foreach (var file in files)
        {
            try
            {
                if (read(file) is { } item)
                    list.Add(item);
            }
            catch (Exception ex)
            {
                var name = ex is JsonException ? JsonNameProbe.TryReadName(file) : null;
                var subject = name is null ? $"{label} file \"{Path.GetFileName(file)}\"" : $"{label} \"{name}\"";
                LoadErrors.Add(StorageErrorDescriber.Describe(ex, file, subject));
            }
        }
        return list;
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
