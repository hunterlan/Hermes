using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>Contents of workspace.json.</summary>
public sealed class WorkspaceInfo
{
    public Guid Id { get; set; }
}

/// <summary>
/// A data folder's own id, kept in workspace.json inside it. Things stored outside the folder for it (its keychain
/// entry) are keyed by this id, so they follow the folder when it's moved, renamed or reached by another path.
/// </summary>
public static class WorkspaceIdentity
{
    public static string FileFor(string dataDirectory) => Path.Combine(dataDirectory, "workspace.json");

    /// <summary>The folder's id, or null when it has none yet.</summary>
    /// <exception cref="IOException">workspace.json exists but couldn't be read.</exception>
    public static Guid? Read(string dataDirectory)
    {
        var file = FileFor(dataDirectory);
        try
        {
            return JsonFileStore.Read(file, HermesJsonContext.Default.WorkspaceInfo) is { Id: var id } && id != Guid.Empty ? id : null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Treated like any unreadable file: a new id here would cut the folder off from its secrets.
            throw new IOException($"{file} is not valid: {ex.Message}", ex);
        }
    }

    public static void Write(string dataDirectory, Guid id) =>
        JsonFileStore.Write(FileFor(dataDirectory), new WorkspaceInfo { Id = id }, HermesJsonContext.Default.WorkspaceInfo);
}
