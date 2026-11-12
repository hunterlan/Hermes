namespace Hermes.Core.Storage;

/// <summary>What went wrong with a file or folder, as far as the user can act on it.</summary>
public enum StorageErrorKind
{
    /// <summary>Anything not covered below; only the details say more.</summary>
    Other,

    /// <summary>The file isn't valid JSON (or not in the shape Hermes expects).</summary>
    InvalidJson,

    /// <summary>The operating system refused access (permissions, read-only media).</summary>
    PermissionDenied,

    /// <summary>No space left on the disk or the user's quota is used up.</summary>
    DiskFull,

    /// <summary>Another program holds the file open.</summary>
    FileInUse,

    /// <summary>The file, its folder or its drive isn't there.</summary>
    PathNotFound,
}
