using MsalStorage = Microsoft.Identity.Client.Extensions.Msal.Storage;

namespace Hermes.Core.Secrets;

/// <summary>The keychain library's storage behind <see cref="IKeychainStorage"/>.</summary>
internal sealed class MsalKeychainStorage(MsalStorage storage) : IKeychainStorage
{
    public byte[] ReadData() => storage.ReadData();

    public void WriteData(byte[] data) => storage.WriteData(data);

    public void Clear() => storage.Clear();

    public void VerifyPersistence() => storage.VerifyPersistence();
}
