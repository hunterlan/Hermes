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
    /// keychain (e.g. Linux without a keyring). Each data folder gets its own entry.
    /// Set <c>HERMES_SECRET_STORE=memory</c> to keep secrets in memory only (used by tests).
    /// </summary>
    public static SecretStore Create(string dataDirectory)
    {
        if (Environment.GetEnvironmentVariable("HERMES_SECRET_STORE") == "memory")
            return new InMemorySecretStore();

        var folderId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory))))[..12].ToLowerInvariant();
        var properties = new StorageCreationPropertiesBuilder($"secrets-{folderId}.bin", AppPaths.ConfigDirectory)
            .WithMacKeyChain("Hermes", $"secrets-{folderId}")
            .WithLinuxKeyring("app.hermes.secrets", MsalCacheHelper.LinuxKeyRingDefaultCollection, "Hermes secrets",
                new KeyValuePair<string, string>("product", "Hermes"),
                new KeyValuePair<string, string>("dataFolder", folderId))
            .Build();
        try
        {
            var storage = MsalStorage.Create(properties);
            storage.VerifyPersistence();
            return new KeychainSecretStore(storage);
        }
        catch (Exception ex)
        {
            return Unprotected($"No usable keychain: {ex.Message}");
        }
    }

    protected override byte[] Read() => _storage.ReadData();

    protected override void Write(byte[] data) => _storage.WriteData(data);
}
