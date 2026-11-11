namespace Hermes.Core.Secrets;

/// <summary>Why the keychain failed, as far as the exception data tells.</summary>
public enum KeychainFailureKind
{
    /// <summary>Nothing recognisable; only the details say more.</summary>
    Unrecognised,

    /// <summary>Linux: the libsecret library isn't installed.</summary>
    LibsecretMissing,

    /// <summary>Linux: libsecret is there but no keyring service answered. A guess, as libsecret reports no clear code.</summary>
    NoKeyringService,

    /// <summary>macOS: access to the keychain was denied (errSecAuthFailed).</summary>
    AccessDenied,

    /// <summary>macOS: the keychain can't show its prompt (errSecInteractionNotAllowed).</summary>
    InteractionNotAllowed,

    /// <summary>macOS: the user cancelled the prompt (errSecUserCanceled).</summary>
    UserCanceled,

    /// <summary>macOS: the keychain doesn't exist (errSecNoSuchKeychain).</summary>
    NoSuchKeychain,

    /// <summary>Windows: DPAPI couldn't encrypt or decrypt the secrets for the account.</summary>
    EncryptionUnavailable,

    /// <summary>The keychain answered, but what it holds isn't the data Hermes stored.</summary>
    UnreadableData,
}
