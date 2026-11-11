using System.Text.Json;

namespace Hermes.Core.Storage;

/// <summary>
/// Turns the exception of a failed file operation into a <see cref="StorageError"/>: a plain sentence, a next step
/// and the raw text kept for Details. Every storage message in the app goes through here so the wording stays the same.
/// </summary>
public static class StorageErrorDescriber
{
    private const string NotJson = "it isn't valid JSON";
    private const string WrongShape = "it is valid JSON but not in the form Hermes expects";

    // Windows error codes, as the low 16 bits of an 0x8007xxxx HResult.
    private const int WindowsFacilityMask = unchecked((int)0xFFFF0000);
    private const int WindowsFacility = unchecked((int)0x80070000);

    // The same leniency as Hermes's own reader (HermesJsonContext), so a hand-edited file with comments or trailing
    // commas isn't called broken because of them.
    private static readonly JsonDocumentOptions LenientJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Describes why <paramref name="exception"/> happened to the file or folder at <paramref name="path"/>.
    /// </summary>
    /// <param name="subject">What the path holds, as the start of a sentence: <c>Collection "Pets"</c>, <c>History</c>.</param>
    /// <param name="operation">What Hermes was doing; picks the wording ("couldn't be loaded", "couldn't be saved").</param>
    public static StorageError Describe(Exception exception, string path, string subject,
        StorageOperation operation = StorageOperation.Read)
    {
        var kind = Classify(exception);
        var details = DetailsOf(exception, path);
        var lead = $"{subject} {Verb(operation)}";

        if (kind == StorageErrorKind.InvalidJson)
        {
            var position = exception is JsonException json ? PositionOf(json) : null;
            var canMoveAside = File.Exists(path);
            var problem = canMoveAside && FileParsesAsJson(path) ? WrongShape : NotJson;
            return new StorageError(kind, $"{lead}: {problem}{position}.",
                canMoveAside
                    ? "Reveal the file to repair it, or move it aside (it is renamed to .corrupt) so Hermes starts without it."
                    : "Reveal the file to repair it.",
                details, path, canMoveAside);
        }

        var (reason, nextStep) = kind switch
        {
            StorageErrorKind.PermissionDenied => (PermissionReason(operation),
                $"Check the permissions of \"{FolderOf(path)}\", then try again."),
            StorageErrorKind.DiskFull => ("the disk is full.", "Free up disk space, then try again."),
            StorageErrorKind.FileInUse => ("another program is using it.",
                "Close the program that has it open (a sync or backup tool, or another Hermes window), then try again."),
            StorageErrorKind.PathNotFound => ("it or its folder wasn't found.",
                "Check that the folder still exists and that its drive is connected, then try again."),
            _ => (OtherReason(operation), "Try again. If it keeps happening, copy the details when you report it."),
        };
        return new StorageError(kind, $"{lead}: {reason}", nextStep, details, path);
    }

    /// <summary>
    /// Describes one line of a JSON Lines file that can't be read. The line is skipped, so there is no move-aside.
    /// </summary>
    public static StorageError DescribeLine(JsonException exception, string path, string subject, int lineNumber,
        string? lineText = null)
    {
        var column = exception.BytePositionInLine is { } position ? $", column {position + 1}" : "";
        var problem = lineText is not null && ParsesAsJson(System.Text.Encoding.UTF8.GetBytes(lineText))
            ? "isn't in the form Hermes expects"
            : "isn't valid JSON";
        return new StorageError(StorageErrorKind.InvalidJson,
            $"{subject} couldn't be fully loaded: line {lineNumber}{column} {problem}, so that entry was skipped.",
            null, DetailsOf(exception, path, $"Line: {lineNumber}"), path);
    }

    /// <summary>The kind of failure <paramref name="exception"/> stands for, from its type and HResult.</summary>
    public static StorageErrorKind Classify(Exception exception)
    {
        if (exception is JsonException)
            return StorageErrorKind.InvalidJson;
        if (exception is UnauthorizedAccessException)
            return StorageErrorKind.PermissionDenied;

        var fromCode = exception is IOException ? ClassifyHResult(exception.HResult) : null;
        if (fromCode is { } known)
            return known;
        return exception is FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException
            ? StorageErrorKind.PathNotFound
            : StorageErrorKind.Other;
    }

    private static StorageErrorKind? ClassifyHResult(int hResult)
    {
        // .NET reports a Windows error as 0x8007xxxx; on Unix the HResult of an IOException is the raw errno.
        if ((hResult & WindowsFacilityMask) == WindowsFacility)
        {
            return (hResult & 0xFFFF) switch
            {
                5 or 19 => StorageErrorKind.PermissionDenied, // access denied, write protected
                32 or 33 => StorageErrorKind.FileInUse, // sharing violation, lock violation
                39 or 112 => StorageErrorKind.DiskFull, // handle disk full, disk full
                2 or 3 or 15 or 53 or 67 => StorageErrorKind.PathNotFound, // file, path, drive, network path, share name
                _ => null,
            };
        }
        if (OperatingSystem.IsWindows())
            return null;

        var quota = OperatingSystem.IsMacOS() ? 69 : 122; // EDQUOT
        var wouldBlock = OperatingSystem.IsMacOS() ? 35 : 11; // EWOULDBLOCK
        if (hResult == quota)
            return StorageErrorKind.DiskFull;
        return hResult switch
        {
            1 or 13 or 30 => StorageErrorKind.PermissionDenied, // EPERM, EACCES, EROFS
            28 => StorageErrorKind.DiskFull, // ENOSPC
            16 or 26 => StorageErrorKind.FileInUse, // EBUSY, ETXTBSY
            2 or 20 => StorageErrorKind.PathNotFound, // ENOENT, ENOTDIR
            _ when hResult == wouldBlock => StorageErrorKind.FileInUse,
            _ => null,
        };
    }

    private static string Verb(StorageOperation operation) => operation switch
    {
        StorageOperation.Write => "couldn't be saved",
        StorageOperation.Use => "can't be used",
        StorageOperation.Delete => "couldn't be deleted",
        _ => "couldn't be loaded",
    };

    private static string PermissionReason(StorageOperation operation) => operation switch
    {
        StorageOperation.Write => "Hermes isn't allowed to write to it.",
        StorageOperation.Use => "Hermes isn't allowed to use it.",
        StorageOperation.Delete => "Hermes isn't allowed to delete it.",
        _ => "Hermes isn't allowed to read it.",
    };

    private static string OtherReason(StorageOperation operation) => operation switch
    {
        StorageOperation.Write => "Hermes couldn't write it.",
        StorageOperation.Use => "Hermes couldn't open it.",
        StorageOperation.Delete => "Hermes couldn't delete it.",
        _ => "Hermes couldn't read it.",
    };

    /// <summary>
    /// Where a syntax error is, as <c> (line 3, column 7)</c> with a leading space, or null when the exception has no
    /// position. 1-based, as editors show them. The column counts bytes, which is the same as characters for plain ASCII.
    /// </summary>
    public static string? PositionOf(JsonException exception) =>
        exception.LineNumber is { } line
            ? $" (line {line + 1}, column {(exception.BytePositionInLine ?? 0) + 1})"
            : null;

    // System.Text.Json also throws JsonException for well-formed JSON of the wrong shape (a text where a number is
    // expected, an unknown value), where a syntax error is the wrong thing to look for.
    private static bool ParsesAsJson(ReadOnlyMemory<byte> utf8)
    {
        try
        {
            using var _ = JsonDocument.Parse(utf8, LenientJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Runs inside error handling, so a file that can't be read a second time just counts as "not JSON".
    private static bool FileParsesAsJson(string path)
    {
        try
        {
            return ParsesAsJson(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            return false;
        }
    }

    private static string FolderOf(string path) =>
        Directory.Exists(path) ? path : Path.GetDirectoryName(path) is { Length: > 0 } folder ? folder : path;

    private static string DetailsOf(Exception exception, string path, string? extra = null)
    {
        var lines = new List<string> { $"Location: {path}" };
        if (extra is not null)
            lines.Add(extra);
        lines.Add($"Error: {exception.GetType().FullName}: {exception.Message}");
        if (exception.HResult != 0)
            lines.Add($"HResult: 0x{exception.HResult:X8}");
        return string.Join('\n', lines);
    }
}
