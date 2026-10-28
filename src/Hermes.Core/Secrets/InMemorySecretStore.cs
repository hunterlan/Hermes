namespace Hermes.Core.Secrets;

/// <summary>Keeps secrets in memory only. Used by tests.</summary>
public sealed class InMemorySecretStore : SecretStore
{
    private byte[] _data = [];

    protected override byte[] Read() => _data;

    protected override void Write(byte[] data) => _data = data;
}
