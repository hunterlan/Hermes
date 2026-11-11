using Hermes.Core.Storage;

namespace Hermes.Core.Secrets;

/// <summary>
/// Turns a failure of the keychain, or of the data folder's id file that the keychain entry is keyed by, into a
/// <see cref="StorageError"/>: a plain reason and next step, with the raw text kept for Details.
/// </summary>
public static class KeychainErrorDescriber
{
    private const string RestartHermes = "then restart Hermes.";

    /// <summary>Describes why the keychain failed during <paramref name="operation"/>.</summary>
    public static StorageError Describe(Exception exception, KeychainOperation operation, KeychainPlatform? platform = null)
    {
        var current = platform ?? KeychainFailureClassifier.CurrentPlatform;
        var reasonAndStep = ReasonFor(KeychainFailureClassifier.Classify(exception, current));
        var reason = reasonAndStep.Reason;
        var summary = operation switch
        {
            KeychainOperation.Read => $"Reading secrets from the keychain failed: {reason} Hermes now keeps secret values as plain text.",
            KeychainOperation.Write => $"Saving secrets to the keychain failed: {reason} Hermes now keeps secret values as plain text.",
            _ => reason,
        };
        return new StorageError(StorageErrorKind.Other, summary, reasonAndStep.NextStep, DetailsOf(exception, operation, current));
    }

    /// <summary>
    /// Describes why the data folder's id file (<c>workspace.json</c>) couldn't be read or written. Hermes can't find
    /// the folder's keychain entry without it. The file is never offered for moving aside, because a new id would cut
    /// the folder off from its secrets.
    /// </summary>
    public static StorageError DescribeIdFile(Exception exception, string dataDirectory, StorageOperation operation)
    {
        var path = WorkspaceIdentity.FileFor(dataDirectory);
        // The identity reader wraps a JSON error in an IOException; the original says more.
        var cause = exception.InnerException is System.Text.Json.JsonException json ? json : exception;
        var error = StorageErrorDescriber.Describe(cause, path, "The data folder's id file", operation);
        return error.Kind == StorageErrorKind.InvalidJson
            ? error with
            {
                NextStep = "Reveal the file to repair it, then restart Hermes. If you delete it, the folder gets a new id and its old secrets are no longer found.",
                CanMoveAside = false,
            }
            : error;
    }

    private static KeychainReason ReasonFor(KeychainFailureKind kind) => kind switch
    {
        KeychainFailureKind.LibsecretMissing => new("libsecret isn't installed.",
            $"Install libsecret and a keyring service (e.g. GNOME Keyring or KWallet), {RestartHermes}"),
        KeychainFailureKind.NoKeyringService => new("No keyring service seems to be running (e.g. GNOME Keyring or KWallet).",
            $"Install or start one, {RestartHermes}"),
        KeychainFailureKind.AccessDenied => new("Keychain access was denied.",
            $"Allow access when macOS asks, or check Keychain Access, {RestartHermes}"),
        KeychainFailureKind.InteractionNotAllowed => new("macOS can't ask for keychain access right now.",
            $"Unlock the keychain in Keychain Access and start Hermes from your desktop session, {RestartHermes}"),
        KeychainFailureKind.UserCanceled => new("Keychain access was cancelled.",
            $"Allow access when macOS asks, {RestartHermes}"),
        KeychainFailureKind.NoSuchKeychain => new("The keychain wasn't found.",
            $"Open Keychain Access and check that your login keychain exists, {RestartHermes}"),
        KeychainFailureKind.EncryptionUnavailable => new("Windows couldn't encrypt the secrets for your account.",
            $"Check that you're signed in to your own Windows account, {RestartHermes}"),
        KeychainFailureKind.SecretsFileUnavailable => new("Hermes couldn't read or write its encrypted secrets file.",
            $"Check that Hermes can write to {AppPaths.ConfigDirectory}, {RestartHermes}"),
        KeychainFailureKind.UnreadableData => new("The stored secrets aren't in a form Hermes can read.",
            "Copy the details when you report it."),
        _ => new("The system keychain isn't available.",
            "Restart Hermes. If it keeps happening, copy the details when you report it."),
    };

    private static string DetailsOf(Exception exception, KeychainOperation operation, KeychainPlatform platform)
    {
        var lines = new List<string> { $"Operation: {operation}", $"Platform: {platform}" };
        var prefix = "Error";
        for (var link = exception; link is not null; link = link.InnerException)
        {
            lines.Add($"{prefix}: {link.GetType().FullName}: {link.Message}");
            prefix = "Caused by";
        }
        return string.Join('\n', lines);
    }

    private sealed record KeychainReason(string Reason, string NextStep);
}
