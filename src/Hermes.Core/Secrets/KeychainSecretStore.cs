using System.Security.Cryptography;
using System.Text;
using Hermes.Core.Storage;
using Microsoft.Identity.Client.Extensions.Msal;
using MsalStorage = Microsoft.Identity.Client.Extensions.Msal.Storage;

namespace Hermes.Core.Secrets;

/// <summary>
/// Secrets in the OS store: the macOS Keychain, the Linux keyring (libsecret) or a DPAPI-encrypted file on Windows.
/// </summary>
public sealed class KeychainSecretStore : SecretStore
{
    private readonly MsalStorage _storage;

    private KeychainSecretStore(MsalStorage storage) => _storage = storage;

    /// <summary>
    /// The keychain store for <paramref name="dataDirectory"/>, or an unprotected store when the OS has no usable
    /// keychain (e.g. Linux without a keyring). Each data folder gets its own entry, keyed by the folder's
    /// <see cref="WorkspaceIdentity">id</see> so that moving the folder keeps its secrets.
    /// Set <c>HERMES_SECRET_STORE=memory</c> to keep secrets in memory only (used by tests).
    /// </summary>
    public static SecretStore Create(string dataDirectory)
    {
        if (Environment.GetEnvironmentVariable("HERMES_SECRET_STORE") == "memory")
            return new InMemorySecretStore();

        WorkspaceIdentityState identity;
        try
        {
            identity = WorkspaceIdentity.Resolve(dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unprotected($"The data folder's id couldn't be read: {ex.Message}");
        }

        try
        {
            var storage = Open(identity.Id.ToString("N"));
            storage.VerifyPersistence();

            // A folder new to ids takes the secrets stored under the hash of its path (how entries used to be keyed);
            // a copied folder starts with a copy of the original's secrets.
            var source = identity.IsNew ? Open(PathKey(dataDirectory))
                : identity.CopiedFrom is { } original ? Open(original.ToString("N"))
                : null;
            var data = source?.ReadData() ?? [];
            if (data.Length > 0)
                storage.WriteData(data);
            // The id is recorded only once the secrets are under it (a failure is retried next start), and the
            // path-keyed entry is cleared only after that. The original of a copy keeps its own entry.
            if (identity.NeedsWrite)
                WorkspaceIdentity.Write(dataDirectory, identity.Id);
            if (identity.IsNew && data.Length > 0)
                source!.Clear();
            return new KeychainSecretStore(storage);
        }
        catch (Exception ex)
        {
            return Unprotected($"No usable keychain: {ex.Message}");
        }
    }

    private static MsalStorage Open(string key) =>
        MsalStorage.Create(new StorageCreationPropertiesBuilder($"secrets-{key}.bin", AppPaths.ConfigDirectory)
            .WithMacKeyChain("Hermes", $"secrets-{key}")
            .WithLinuxKeyring("app.hermes.secrets", MsalCacheHelper.LinuxKeyRingDefaultCollection, "Hermes secrets",
                new KeyValuePair<string, string>("product", "Hermes"),
                new KeyValuePair<string, string>("dataFolder", key))
            .Build());

    private static string PathKey(string dataDirectory) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory))))[..12].ToLowerInvariant();

    protected override byte[] Read() => _storage.ReadData();

    protected override void Write(byte[] data) => _storage.WriteData(data);
}
