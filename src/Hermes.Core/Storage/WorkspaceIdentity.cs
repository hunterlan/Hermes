using Hermes.Core.Serialization;

namespace Hermes.Core.Storage;

/// <summary>Contents of workspace.json.</summary>
public sealed class WorkspaceInfo
{
    public Guid Id { get; set; }

    /// <summary>Where the folder was when it was last opened; tells a moved folder from a copied one.</summary>
    public string? Path { get; set; }
}

/// <summary>A data folder's identity as found on opening it.</summary>
/// <param name="Id">The id to use from now on.</param>
/// <param name="IsNew">The folder had no id yet.</param>
/// <param name="CopiedFrom">The folder is a copy of another one that still exists; this is that folder's id.</param>
/// <param name="NeedsWrite">workspace.json has to be (re)written with <paramref name="Id"/> and the folder's path.</param>
public sealed record WorkspaceIdentityState(Guid Id, bool IsNew, Guid? CopiedFrom, bool NeedsWrite);

/// <summary>
/// A data folder's own id, kept in workspace.json inside it. Things stored outside the folder for it (its keychain
/// entry) are keyed by this id, so they follow the folder when it's moved, renamed or reached by another path; a
/// copy of the folder gets an id of its own.
/// </summary>
public static class WorkspaceIdentity
{
    public static string FileFor(string dataDirectory) => System.IO.Path.Combine(dataDirectory, "workspace.json");

    /// <exception cref="IOException">workspace.json exists but couldn't be read.</exception>
    public static WorkspaceIdentityState Resolve(string dataDirectory)
    {
        var current = System.IO.Path.GetFullPath(dataDirectory);
        if (Read(dataDirectory) is not { } info)
            return new WorkspaceIdentityState(Guid.NewGuid(), IsNew: true, CopiedFrom: null, NeedsWrite: true);
        if (info.Path is null || SamePath(info.Path, current))
            return new WorkspaceIdentityState(info.Id, IsNew: false, CopiedFrom: null, NeedsWrite: info.Path is null);

        // The folder was elsewhere last time. If that folder is still there with the same id, this is a copy.
        var original = TryRead(info.Path);
        return original?.Id == info.Id
            ? new WorkspaceIdentityState(Guid.NewGuid(), IsNew: false, CopiedFrom: info.Id, NeedsWrite: true)
            : new WorkspaceIdentityState(info.Id, IsNew: false, CopiedFrom: null, NeedsWrite: true);
    }

    public static void Write(string dataDirectory, Guid id) =>
        JsonFileStore.Write(FileFor(dataDirectory), new WorkspaceInfo { Id = id, Path = System.IO.Path.GetFullPath(dataDirectory) },
            HermesJsonContext.Default.WorkspaceInfo);

    private static WorkspaceInfo? Read(string dataDirectory)
    {
        var file = FileFor(dataDirectory);
        try
        {
            return JsonFileStore.Read(file, HermesJsonContext.Default.WorkspaceInfo) is { } info && info.Id != Guid.Empty ? info : null;
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Treated like any unreadable file: a new id here would cut the folder off from its secrets.
            throw new IOException($"{file} is not valid: {ex.Message}", ex);
        }
    }

    // The other folder is only looked at, so any failure to read it means "not there".
    private static WorkspaceInfo? TryRead(string dataDirectory)
    {
        try
        {
            return Read(dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.TrimEndingDirectorySeparator(a), System.IO.Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
