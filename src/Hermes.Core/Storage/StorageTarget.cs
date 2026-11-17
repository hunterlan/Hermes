namespace Hermes.Core.Storage;

/// <summary>What a failed storage path is, as the caller knows it. Picks "folder" or "file" in the next step.</summary>
public enum StorageTarget
{
    /// <summary>A file, which may not exist yet when Hermes is about to create it.</summary>
    File,

    /// <summary>A folder.</summary>
    Folder,
}
