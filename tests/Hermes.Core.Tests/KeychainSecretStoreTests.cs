using Hermes.Core.Secrets;
using Hermes.Core.Storage;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Hermes.Core.Tests;

public class KeychainSecretStoreTests : IDisposable
{
    private const string RawMessage = "raw-library-text: Domain:'1' Code:'2' Error code: -25293";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid());

    public KeychainSecretStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static readonly IReadOnlyDictionary<string, string> OneSecret = new Dictionary<string, string> { ["key"] = "value" };

    private static MsalCachePersistenceException Failure() => new(RawMessage);

    private static void AssertPlainReasonAndRawDetails(StorageError problem)
    {
        Assert.DoesNotContain("raw-library-text", problem.Summary);
        Assert.DoesNotContain("raw-library-text", problem.NextStep);
        Assert.Contains(RawMessage, problem.Details);
        Assert.Contains(nameof(MsalCachePersistenceException), problem.Details);
    }

    [Fact]
    public void A_keychain_that_fails_to_open_gives_a_plain_reason_and_keeps_the_raw_message()
    {
        var store = KeychainSecretStore.Create(_dir, _ => throw Failure());

        Assert.False(store.IsProtected);
        var problem = Assert.IsType<StorageError>(store.UnprotectedProblem);
        AssertPlainReasonAndRawDetails(problem);
        Assert.Null(problem.Location);
        Assert.Contains("Operation: Open", problem.Details);
    }

    [Fact]
    public void A_keychain_that_fails_verification_gives_a_plain_reason()
    {
        var store = KeychainSecretStore.Create(_dir, _ => new FakeStorage { OnVerify = () => throw Failure() });

        AssertPlainReasonAndRawDetails(Assert.IsType<StorageError>(store.UnprotectedProblem));
    }

    [Fact]
    public void A_failed_read_says_what_failed_and_that_secrets_are_now_plain_text()
    {
        var storage = new FakeStorage();
        var store = KeychainSecretStore.Create(_dir, _ => storage);
        Assert.True(store.IsProtected);
        storage.OnRead = () => throw Failure();

        Assert.Empty(store.Get("owner"));

        Assert.False(store.IsProtected);
        var problem = Assert.IsType<StorageError>(store.UnprotectedProblem);
        Assert.StartsWith("Reading secrets from the keychain failed:", problem.Summary);
        Assert.Contains("plain text", problem.Summary);
        AssertPlainReasonAndRawDetails(problem);
        Assert.Contains("Operation: Read", problem.Details);
    }

    [Fact]
    public void A_failed_write_says_what_failed_and_that_secrets_are_now_plain_text()
    {
        var storage = new FakeStorage();
        var store = KeychainSecretStore.Create(_dir, _ => storage);
        storage.OnWrite = () => throw Failure();

        store.Set("owner", OneSecret);

        Assert.False(store.IsProtected);
        var problem = Assert.IsType<StorageError>(store.UnprotectedProblem);
        Assert.StartsWith("Saving secrets to the keychain failed:", problem.Summary);
        Assert.Contains("plain text", problem.Summary);
        AssertPlainReasonAndRawDetails(problem);
        Assert.Contains("Operation: Write", problem.Details);
    }

    [Fact]
    public void A_corrupt_id_file_names_the_file_offers_reveal_and_does_not_offer_move_aside()
    {
        var idFile = WorkspaceIdentity.FileFor(_dir);
        File.WriteAllText(idFile, "{ not json");

        var store = KeychainSecretStore.Create(_dir, _ => new FakeStorage());

        Assert.False(store.IsProtected);
        var problem = Assert.IsType<StorageError>(store.UnprotectedProblem);
        Assert.Equal(StorageErrorKind.InvalidJson, problem.Kind);
        Assert.Equal(idFile, problem.Location);
        Assert.False(problem.CanMoveAside);
        Assert.StartsWith("The data folder's id file couldn't be loaded", problem.Summary);
        Assert.Contains("old secrets are no longer found", problem.NextStep);
        Assert.Contains("JsonException", problem.Details);
    }

    [Fact]
    public void An_id_file_that_cannot_be_read_is_described_by_its_cause()
    {
        var idFile = WorkspaceIdentity.FileFor(_dir);
        var problem = KeychainErrorDescriber.DescribeIdFile(
            new UnauthorizedAccessException("Access to the path is denied."), _dir, StorageOperation.Read);

        Assert.Equal(StorageErrorKind.PermissionDenied, problem.Kind);
        Assert.Equal(idFile, problem.Location);
        Assert.Contains("Access to the path is denied.", problem.Details);
        Assert.DoesNotContain("Access to the path", problem.Summary);
    }

    [Theory]
    [InlineData(KeychainPlatform.Linux, "No keyring service seems to be running (e.g. GNOME Keyring or KWallet).", "Install or start one")]
    [InlineData(KeychainPlatform.MacOS, "The system keychain isn't available.", "Restart Hermes.")]
    [InlineData(KeychainPlatform.Windows, "The system keychain isn't available.", "Restart Hermes.")]
    public void The_reason_is_plain_for_each_platform(KeychainPlatform platform, string reason, string nextStep)
    {
        var problem = KeychainErrorDescriber.Describe(new MsalCachePersistenceException("opaque"), KeychainOperation.Open, platform);

        Assert.Equal(reason, problem.Summary);
        Assert.StartsWith(nextStep, problem.NextStep);
    }

    [Fact]
    public void Each_mapped_reason_has_a_next_step_and_keeps_the_raw_text()
    {
        Assert.Equal("libsecret isn't installed.", KeychainErrorDescriber.Describe(
            new DllNotFoundException("libsecret-1.so.0"), KeychainOperation.Open, KeychainPlatform.Linux).Summary);
        Assert.Equal("Keychain access was denied.", KeychainErrorDescriber.Describe(
            new MsalCachePersistenceException(RawMessage), KeychainOperation.Open, KeychainPlatform.MacOS).Summary);
        var windows = KeychainErrorDescriber.Describe(
            new MsalCachePersistenceException("x", new System.Security.Cryptography.CryptographicException("dpapi-raw")),
            KeychainOperation.Open, KeychainPlatform.Windows);
        Assert.Equal("Windows couldn't encrypt the secrets for your account.", windows.Summary);
        Assert.DoesNotContain("Credential Manager", windows.Message);
        Assert.Contains("dpapi-raw", windows.Details);
    }

    private sealed class FakeStorage : IKeychainStorage
    {
        public Action? OnRead { get; set; }
        public Action? OnWrite { get; set; }
        public Action? OnVerify { get; set; }

        public byte[] ReadData()
        {
            OnRead?.Invoke();
            return [];
        }

        public void WriteData(byte[] data) => OnWrite?.Invoke();

        public void Clear() { }

        public void VerifyPersistence() => OnVerify?.Invoke();
    }
}
