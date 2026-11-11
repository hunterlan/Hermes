using System.Security.Cryptography;
using System.Text.Json;
using Hermes.Core.Secrets;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Hermes.Core.Tests;

public class KeychainFailureClassifierTests
{
    private static Exception Persistence(string message, Exception? inner = null) =>
        new MsalCachePersistenceException(message, inner!);

    // The library's own error type is internal, so it is built the way the library builds it.
    private static Exception Interop(string message, int errorCode)
    {
        var type = typeof(MsalCachePersistenceException).Assembly.GetType("Microsoft.Identity.Extensions.InteropException")!;
        return (Exception)Activator.CreateInstance(type, message, errorCode)!;
    }

    [Theory]
    [InlineData(-25293, KeychainFailureKind.AccessDenied)]
    [InlineData(-25308, KeychainFailureKind.InteractionNotAllowed)]
    [InlineData(-128, KeychainFailureKind.UserCanceled)]
    [InlineData(-25294, KeychainFailureKind.NoSuchKeychain)]
    public void A_macOS_status_in_the_message_is_mapped(int status, KeychainFailureKind expected)
    {
        var failure = Persistence($"Something went wrong. Error code: {status}");

        Assert.Equal(expected, KeychainFailureClassifier.Classify(failure, KeychainPlatform.MacOS));
    }

    [Theory]
    [InlineData(-25293, KeychainFailureKind.AccessDenied)]
    [InlineData(-25308, KeychainFailureKind.InteractionNotAllowed)]
    [InlineData(-128, KeychainFailureKind.UserCanceled)]
    [InlineData(-25294, KeychainFailureKind.NoSuchKeychain)]
    public void A_macOS_status_on_the_inner_interop_exception_is_mapped(int status, KeychainFailureKind expected)
    {
        var failure = Persistence("Persistence check failed. Inspect inner exception for details", Interop("Opaque", status));

        Assert.Equal(expected, KeychainFailureClassifier.Classify(failure, KeychainPlatform.MacOS));
    }

    [Theory]
    [InlineData("KeyChain authorization/authentication failed.", KeychainFailureKind.AccessDenied)]
    [InlineData("KeyChain - user interaction is required.", KeychainFailureKind.InteractionNotAllowed)]
    [InlineData("KeyChain - interaction with the Security Server is not allowed.", KeychainFailureKind.InteractionNotAllowed)]
    [InlineData("KeyChain - user cancelled the operation.", KeychainFailureKind.UserCanceled)]
    [InlineData("The keychain does not exist.", KeychainFailureKind.NoSuchKeychain)]
    public void A_macOS_message_without_a_number_is_mapped_by_its_text(string message, KeychainFailureKind expected) =>
        Assert.Equal(expected, KeychainFailureClassifier.Classify(Persistence(message), KeychainPlatform.MacOS));

    [Fact]
    public void An_unknown_macOS_status_is_unrecognised() =>
        Assert.Equal(KeychainFailureKind.Unrecognised,
            KeychainFailureClassifier.Classify(Persistence("Error code: -1"), KeychainPlatform.MacOS));

    [Fact]
    public void A_missing_libsecret_is_found_directly_or_as_an_inner_exception()
    {
        var missing = new DllNotFoundException("Unable to load shared library 'libsecret-1.so.0'");

        Assert.Equal(KeychainFailureKind.LibsecretMissing, KeychainFailureClassifier.Classify(missing, KeychainPlatform.Linux));
        Assert.Equal(KeychainFailureKind.LibsecretMissing,
            KeychainFailureClassifier.Classify(Persistence("Persistence check failed.", missing), KeychainPlatform.Linux));
    }

    [Fact]
    public void Any_other_Linux_failure_is_a_missing_keyring_service() =>
        Assert.Equal(KeychainFailureKind.NoKeyringService, KeychainFailureClassifier.Classify(
            Persistence("An error was encountered while saving secret to keyring. Domain:'1' Code:'2'."), KeychainPlatform.Linux));

    [Fact]
    public void A_DPAPI_failure_on_Windows_is_an_encryption_problem() =>
        Assert.Equal(KeychainFailureKind.EncryptionUnavailable, KeychainFailureClassifier.Classify(
            Persistence("Persistence check failed.", new CryptographicException("Key not valid for use in specified state.")),
            KeychainPlatform.Windows));

    [Fact]
    public void An_unreadable_blob_is_unreadable_data_on_every_platform() =>
        Assert.Equal(KeychainFailureKind.UnreadableData,
            KeychainFailureClassifier.Classify(new JsonException("bad"), KeychainPlatform.Linux));

    [Fact]
    public void An_unknown_failure_is_unrecognised_on_Windows_and_elsewhere()
    {
        Assert.Equal(KeychainFailureKind.Unrecognised,
            KeychainFailureClassifier.Classify(new InvalidOperationException("?"), KeychainPlatform.Windows));
        Assert.Equal(KeychainFailureKind.Unrecognised,
            KeychainFailureClassifier.Classify(new InvalidOperationException("?"), KeychainPlatform.Other));
    }
}
