namespace Hermes.Core.Secrets;

/// <summary>The part of the OS keychain library that <see cref="KeychainSecretStore"/> uses; tests replace it.</summary>
internal interface IKeychainStorage
{
    byte[] ReadData();

    void WriteData(byte[] data);

    void Clear();

    void VerifyPersistence();
}
