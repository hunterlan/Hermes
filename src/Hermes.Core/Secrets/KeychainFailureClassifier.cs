using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Core.Secrets;

/// <summary>
/// Works out why a keychain call failed. The keychain library (Microsoft.Identity.Client.Extensions.Msal) throws one
/// public type, <c>MsalCachePersistenceException</c>, with no public error code, so this reads what it does carry: the
/// type of the exception chain (a missing libsecret is a <see cref="DllNotFoundException"/>, also when the library
/// wraps it), the macOS status number that its internal <c>InteropException</c> holds or its message prints as
/// "Error code: N", and for macOS the fixed texts it uses for the same failures. A library update can change the texts,
/// so anything not matched is <see cref="KeychainFailureKind.Unrecognised"/> and the raw message goes to Details.
/// </summary>
public static partial class KeychainFailureClassifier
{
    // OSStatus values from Apple's Security framework (SecBase.h).
    private const int ErrSecUserCanceled = -128;
    private const int ErrSecNoSuchKeychain = -25294;
    private const int ErrSecAuthFailed = -25293;
    private const int ErrSecInteractionNotAllowed = -25308;

    /// <summary>The platform Hermes is running on.</summary>
    public static KeychainPlatform CurrentPlatform =>
        OperatingSystem.IsMacOS() ? KeychainPlatform.MacOS
        : OperatingSystem.IsLinux() ? KeychainPlatform.Linux
        : OperatingSystem.IsWindows() ? KeychainPlatform.Windows
        : KeychainPlatform.Other;

    public static KeychainFailureKind Classify(Exception exception, KeychainPlatform platform)
    {
        var chain = ChainOf(exception).ToList();
        if (chain.Any(link => link is JsonException))
            return KeychainFailureKind.UnreadableData;

        switch (platform)
        {
            case KeychainPlatform.Linux:
                return chain.Any(link => link is DllNotFoundException)
                    ? KeychainFailureKind.LibsecretMissing
                    : KeychainFailureKind.NoKeyringService;
            case KeychainPlatform.MacOS:
                return StatusOf(chain) switch
                {
                    ErrSecAuthFailed => KeychainFailureKind.AccessDenied,
                    ErrSecInteractionNotAllowed => KeychainFailureKind.InteractionNotAllowed,
                    ErrSecUserCanceled => KeychainFailureKind.UserCanceled,
                    ErrSecNoSuchKeychain => KeychainFailureKind.NoSuchKeychain,
                    _ => KeychainFailureKind.Unrecognised,
                };
            case KeychainPlatform.Windows:
                // The library keeps the encrypted secrets in a file in the config folder, so file errors are possible too.
                if (chain.Any(link => link is CryptographicException))
                    return KeychainFailureKind.EncryptionUnavailable;
                return chain.Any(link => link is IOException or UnauthorizedAccessException)
                    ? KeychainFailureKind.SecretsFileUnavailable
                    : KeychainFailureKind.Unrecognised;
            default:
                return KeychainFailureKind.Unrecognised;
        }
    }

    private static IEnumerable<Exception> ChainOf(Exception exception)
    {
        for (var link = exception; link is not null; link = link.InnerException)
        {
            yield return link;
            if (link is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.SelectMany(ChainOf))
                    yield return inner;
        }
    }

    private static int? StatusOf(IReadOnlyList<Exception> chain)
    {
        foreach (var link in chain)
            if (ErrorCodeProperty(link) is { } code)
                return code;
        foreach (var link in chain)
            if (ErrorCodeInMessage().Match(link.Message) is { Success: true } match
                && int.TryParse(match.Groups[1].Value, out var code))
                return code;
        foreach (var link in chain)
            if (StatusFromText(link.Message) is { } code)
                return code;
        return null;
    }

    // InteropException is internal to the library, so its ErrorCode is read by name and any surprise means "no code".
    private static int? ErrorCodeProperty(Exception exception)
    {
        try
        {
            return exception.GetType().Name == "InteropException"
                && exception.GetType().GetProperty("ErrorCode", BindingFlags.Public | BindingFlags.Instance)?.GetValue(exception) is int code
                    ? code
                    : null;
        }
        catch (Exception ex) when (ex is TargetInvocationException or MethodAccessException or InvalidCastException)
        {
            return null;
        }
    }

    // The texts the library uses for these statuses, for when the number isn't in the message.
    private static int? StatusFromText(string message) => message switch
    {
        _ when message.Contains("authorization/authentication failed", StringComparison.OrdinalIgnoreCase) => ErrSecAuthFailed,
        _ when message.Contains("interaction with the Security Server is not allowed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("user interaction is required", StringComparison.OrdinalIgnoreCase) => ErrSecInteractionNotAllowed,
        _ when message.Contains("user cancelled the operation", StringComparison.OrdinalIgnoreCase) => ErrSecUserCanceled,
        _ when message.Contains("The keychain does not exist", StringComparison.OrdinalIgnoreCase) => ErrSecNoSuchKeychain,
        _ => null,
    };

    [GeneratedRegex(@"Error code:\s*(-?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorCodeInMessage();
}
