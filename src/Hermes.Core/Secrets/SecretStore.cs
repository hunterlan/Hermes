using System.Text.Json;
using Hermes.Core.Serialization;

namespace Hermes.Core.Secrets;

/// <summary>
/// Secret values kept outside the data files, grouped by owner (a collection, environment, globals or the session).
/// Everything is held as one blob, so the OS keychain is touched once per change rather than once per secret.
/// </summary>
public abstract class SecretStore
{
    private static readonly IReadOnlyDictionary<string, string> EmptySecrets = new Dictionary<string, string>();

    private readonly Lock _lock = new();
    private Dictionary<string, Dictionary<string, string>>? _owners;

    /// <summary>
    /// False when secrets can't be protected (no keychain, or it failed). Callers then keep secrets in the data files.
    /// </summary>
    public bool IsProtected { get; private set; } = true;

    /// <summary>Why secrets aren't protected, for the warning shown to the user.</summary>
    public string? UnprotectedReason { get; private set; }

    /// <summary>Raised when the store stops protecting secrets after a keychain failure.</summary>
    public event Action? ProtectionLost;

    public static SecretStore Unprotected(string reason) => new UnprotectedSecretStore(reason);

    public IReadOnlyDictionary<string, string> Get(string owner)
    {
        if (!IsProtected)
            return EmptySecrets;
        lock (_lock)
            return Owners.TryGetValue(owner, out var secrets) ? new Dictionary<string, string>(secrets) : EmptySecrets;
    }

    /// <summary>Replaces everything stored for <paramref name="owner"/>; an empty map removes the owner.</summary>
    public void Set(string owner, IReadOnlyDictionary<string, string> secrets)
    {
        if (!IsProtected)
            return;
        lock (_lock)
        {
            var owners = Owners;
            // Reading may have just failed; writing now would replace every other owner's secrets.
            if (!IsProtected)
                return;
            var current = owners.TryGetValue(owner, out var existing) ? existing : null;
            if (secrets.Count == 0 ? current is null : current is not null && SameSecrets(current, secrets))
                return;

            if (secrets.Count == 0)
                owners.Remove(owner);
            else
                owners[owner] = new Dictionary<string, string>(secrets);
            try
            {
                Write(JsonSerializer.SerializeToUtf8Bytes(owners, HermesJsonContext.Default.DictionaryStringDictionaryStringString));
            }
            catch (Exception ex)
            {
                MarkUnprotected($"Saving to the keychain failed: {ex.Message}");
            }
        }
    }

    protected abstract byte[] Read();

    protected abstract void Write(byte[] data);

    protected void MarkUnprotected(string reason)
    {
        if (!IsProtected)
            return;
        IsProtected = false;
        UnprotectedReason = reason;
        ProtectionLost?.Invoke();
    }

    private Dictionary<string, Dictionary<string, string>> Owners
    {
        get
        {
            if (_owners is not null)
                return _owners;
            try
            {
                var data = Read();
                _owners = data.Length == 0
                    ? []
                    : JsonSerializer.Deserialize(data, HermesJsonContext.Default.DictionaryStringDictionaryStringString) ?? [];
            }
            catch (Exception ex)
            {
                // Writing over secrets we couldn't read would lose them, so stop using the store instead.
                _owners = [];
                MarkUnprotected($"Reading the keychain failed: {ex.Message}");
            }
            return _owners;
        }
    }

    private static bool SameSecrets(Dictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && b.All(kv => a.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private sealed class UnprotectedSecretStore : SecretStore
    {
        public UnprotectedSecretStore(string reason) => MarkUnprotected(reason);

        protected override byte[] Read() => [];

        protected override void Write(byte[] data) { }
    }
}
