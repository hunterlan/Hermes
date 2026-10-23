using System.Globalization;
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
/// history/yyyy-MM-dd.json
/// responses/{requestId}.json
/// session.json
/// </code>
/// Secret variables and literal auth credentials go to <see cref="Secrets"/> instead of these files while it can
/// protect them; history keeps no credentials at all.
/// </summary>
public sealed class WorkspaceRepository(string root, SecretStore? secrets = null)
{
    private const int MaxHistoryEntriesPerDay = 500;

    /// <summary>The latest response plus five earlier ones are kept per request.</summary>
    public const int MaxSavedResponses = 6;

    /// <summary>Bodies above this are not stored with saved responses.</summary>
    public const long MaxSavedBodyBytes = 2 * 1024 * 1024;

    private readonly object _historyLock = new();

    public string Root { get; } = root;

    public SecretStore Secrets { get; } = secrets ?? SecretStore.Unprotected("No secret store configured.");

    private string CollectionsDir => Path.Combine(Root, "collections");
    private string EnvironmentsDir => Path.Combine(Root, "environments");
    private string HistoryDir => Path.Combine(Root, "history");
    private string ResponsesDir => Path.Combine(Root, "responses");
    private string GlobalsFile => Path.Combine(Root, "globals.json");
    private string SessionFile => Path.Combine(Root, "session.json");

    /// <summary>Files that failed to load, with the reason. Shown to the user instead of crashing.</summary>
    public List<string> LoadErrors { get; } = [];

    // ---- collections ----

    public List<CollectionModel> LoadCollections() =>
        LoadAll(CollectionsDir, p => JsonFileStore.Read(p, HermesJsonContext.Default.CollectionModel))
            .Select(c =>
            {
                SecretFields.Restore(c, Secrets.Get(CollectionOwner(c.Id)));
                return c;
            })
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void SaveCollection(CollectionModel collection) =>
        JsonFileStore.Write(Path.Combine(CollectionsDir, $"{collection.Id}.json"),
            WithoutSecrets(collection, ModelCloner.Clone, SecretFields.Strip, CollectionOwner(collection.Id)),
            HermesJsonContext.Default.CollectionModel);

    public void DeleteCollection(Guid id)
    {
        DeleteFile(Path.Combine(CollectionsDir, $"{id}.json"));
        Secrets.Set(CollectionOwner(id), NoSecrets);
    }

    // ---- environments ----

    public List<EnvironmentModel> LoadEnvironments() =>
        LoadAll(EnvironmentsDir, p => JsonFileStore.Read(p, HermesJsonContext.Default.EnvironmentModel))
            .Select(e =>
            {
                SecretFields.Restore(e, Secrets.Get(EnvironmentOwner(e.Id)));
                return e;
            })
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public void SaveEnvironment(EnvironmentModel environment) =>
        JsonFileStore.Write(Path.Combine(EnvironmentsDir, $"{environment.Id}.json"),
            WithoutSecrets(environment, ModelCloner.Clone, SecretFields.Strip, EnvironmentOwner(environment.Id)),
            HermesJsonContext.Default.EnvironmentModel);

    public void DeleteEnvironment(Guid id)
    {
        DeleteFile(Path.Combine(EnvironmentsDir, $"{id}.json"));
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
            LoadErrors.Add($"{GlobalsFile}: {ex.Message}");
            return new EnvironmentModel { Name = "Globals" };
        }
    }

    public void SaveGlobals(EnvironmentModel globals) =>
        JsonFileStore.Write(GlobalsFile, WithoutSecrets(globals, ModelCloner.Clone, SecretFields.Strip, GlobalsOwner),
            HermesJsonContext.Default.EnvironmentModel);

    // ---- history ----

    public List<HistoryEntry> LoadHistory(int retentionDays)
    {
        if (!Directory.Exists(HistoryDir))
            return [];

        var cutoff = DateTime.Today.AddDays(-Math.Max(1, retentionDays));
        var result = new List<HistoryEntry>();
        foreach (var file in Directory.EnumerateFiles(HistoryDir, "*.json"))
        {
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            if (day < cutoff)
            {
                DeleteFile(file);
                continue;
            }
            try
            {
                result.AddRange(JsonFileStore.Read(file, HermesJsonContext.Default.ListHistoryEntry) ?? []);
            }
            catch (Exception ex)
            {
                LoadErrors.Add($"{file}: {ex.Message}");
            }
        }
        return result.OrderByDescending(h => h.Timestamp).ToList();
    }

    /// <summary>Writes the entry without literal credentials; the in-memory <paramref name="entry"/> is left as is.</summary>
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
        lock (_historyLock)
        {
            var file = HistoryFileFor(entry.Timestamp);
            List<HistoryEntry> entries;
            try
            {
                entries = JsonFileStore.Read(file, HermesJsonContext.Default.ListHistoryEntry) ?? [];
            }
            catch
            {
                entries = [];
            }
            entries.Add(entry);
            if (entries.Count > MaxHistoryEntriesPerDay)
                entries.RemoveRange(0, entries.Count - MaxHistoryEntriesPerDay);
            JsonFileStore.Write(file, entries, HermesJsonContext.Default.ListHistoryEntry);
        }
    }

    public void DeleteHistoryEntry(HistoryEntry entry)
    {
        lock (_historyLock)
        {
            var file = HistoryFileFor(entry.Timestamp);
            var entries = JsonFileStore.Read(file, HermesJsonContext.Default.ListHistoryEntry) ?? [];
            entries.RemoveAll(e => e.Id == entry.Id);
            JsonFileStore.Write(file, entries, HermesJsonContext.Default.ListHistoryEntry);
        }
    }

    public void ClearHistory()
    {
        lock (_historyLock)
        {
            if (Directory.Exists(HistoryDir))
                Directory.Delete(HistoryDir, recursive: true);
        }
    }

    private string HistoryFileFor(DateTimeOffset timestamp) =>
        Path.Combine(HistoryDir, timestamp.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    // ---- saved responses ----

    /// <summary>Saved responses for a request, newest first.</summary>
    public List<SavedResponse> LoadResponses(Guid requestId)
    {
        try
        {
            return JsonFileStore.Read(ResponsesFileFor(requestId), HermesJsonContext.Default.ListSavedResponse) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Adds a response in front and keeps the newest <see cref="MaxSavedResponses"/>. Returns the stored list.</summary>
    public List<SavedResponse> SaveResponse(Guid requestId, SavedResponse saved)
    {
        if (saved.Response.BodySize > MaxSavedBodyBytes)
        {
            saved = new SavedResponse
            {
                Timestamp = saved.Timestamp,
                SentRequest = saved.SentRequest,
                BodyOmitted = true,
                Response = WithoutBody(saved.Response),
            };
        }
        var responses = LoadResponses(requestId);
        responses.Insert(0, saved);
        if (responses.Count > MaxSavedResponses)
            responses.RemoveRange(MaxSavedResponses, responses.Count - MaxSavedResponses);
        JsonFileStore.Write(ResponsesFileFor(requestId), responses, HermesJsonContext.Default.ListSavedResponse);
        return responses;
    }

    public void DeleteResponses(Guid requestId) => DeleteFile(ResponsesFileFor(requestId));

    private string ResponsesFileFor(Guid requestId) => Path.Combine(ResponsesDir, $"{requestId}.json");

    private static ResponseModel WithoutBody(ResponseModel r) => new()
    {
        StatusCode = r.StatusCode,
        ReasonPhrase = r.ReasonPhrase,
        HttpVersion = r.HttpVersion,
        Headers = r.Headers,
        ContentType = r.ContentType,
        Charset = r.Charset,
        FinalUri = r.FinalUri,
        TimeToFirstByte = r.TimeToFirstByte,
        TotalTime = r.TotalTime,
        HeadersSize = r.HeadersSize,
        Cookies = r.Cookies,
    };

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
            LoadErrors.Add($"{SessionFile}: {ex.Message}");
            return new SessionState();
        }
    }

    public void SaveSession(SessionState session) =>
        JsonFileStore.Write(SessionFile, WithoutSecrets(session, CloneSession, SecretFields.Strip, SessionOwner),
            HermesJsonContext.Default.SessionState);

    private static SessionState CloneSession(SessionState session) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(session, HermesJsonContext.Default.SessionState),
            HermesJsonContext.Default.SessionState)!;

    // ---- settings (stored in the config dir, not the data dir, because they point at the data dir) ----

    public static AppSettings LoadSettings()
    {
        try
        {
            return JsonFileStore.Read(AppPaths.SettingsFile, HermesJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings) =>
        JsonFileStore.Write(AppPaths.SettingsFile, settings, HermesJsonContext.Default.AppSettings);

    // ---- secrets ----

    private const string GlobalsOwner = "globals";
    private const string SessionOwner = "session";
    private static readonly Dictionary<string, string> NoSecrets = [];

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

    private List<T> LoadAll<T>(string dir, Func<string, T?> read) where T : class
    {
        var list = new List<T>();
        if (!Directory.Exists(dir))
            return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                if (read(file) is { } item)
                    list.Add(item);
            }
            catch (Exception ex)
            {
                LoadErrors.Add($"{file}: {ex.Message}");
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
