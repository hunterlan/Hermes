namespace Hermes.Core.Secrets;

/// <summary>What Hermes was doing with the keychain when it failed; picks the wording of the message.</summary>
public enum KeychainOperation
{
    /// <summary>Opening the keychain at start-up.</summary>
    Open,

    /// <summary>Reading the stored secrets while Hermes runs.</summary>
    Read,

    /// <summary>Saving secrets while Hermes runs.</summary>
    Write,
}
