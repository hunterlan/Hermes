namespace Hermes.Core.Storage;

/// <summary>What Hermes was doing when it failed; picks the wording of the message.</summary>
public enum StorageOperation
{
    /// <summary>Loading: "couldn't be loaded".</summary>
    Read,

    /// <summary>Saving: "couldn't be saved".</summary>
    Write,

    /// <summary>Opening a folder to work in: "can't be used".</summary>
    Use,
}
