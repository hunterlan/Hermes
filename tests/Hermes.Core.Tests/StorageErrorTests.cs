using System.Runtime.Versioning;
using System.Text.Json;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.Core.Tests;

public class StorageErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid());

    private static void AssertPlainText(StorageError error)
    {
        foreach (var text in new[] { error.Summary, error.NextStep ?? "", error.Message })
        {
            Assert.DoesNotContain("BytePositionInLine", text);
            Assert.DoesNotContain("LineNumber", text);
            Assert.DoesNotContain("Exception", text);
            Assert.DoesNotContain("System.", text);
            Assert.DoesNotContain("$.", text);
        }
    }

    [Fact]
    public void A_truncated_collection_is_named_and_located_in_plain_words()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        var file = Path.Combine(_dir, "collections", $"{Guid.NewGuid()}.json");
        File.WriteAllText(file, "{\n  \"id\": \"7f8a3c2e-1b4d-4e5f-9a6b-0c1d2e3f4a5b\",\n  \"name\": \"Pets API\",\n  \"items\": [\n    { \"na");

        var repo = new WorkspaceRepository(_dir);
        Assert.Empty(repo.LoadCollections());

        var error = Assert.Single(repo.LoadErrors);
        Assert.StartsWith("Collection \"Pets API\" couldn't be loaded: it isn't valid JSON (line 5, column", error.Summary);
        Assert.Equal(StorageErrorKind.InvalidJson, error.Kind);
        Assert.Equal(file, error.Location);
        Assert.True(error.CanMoveAside);
        Assert.NotNull(error.NextStep);
        AssertPlainText(error);
        Assert.Contains("BytePositionInLine", error.Details);
    }

    [Fact]
    public void A_file_of_the_wrong_shape_is_called_valid_json_in_the_wrong_form()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        var file = Path.Combine(_dir, "collections", $"{Guid.NewGuid()}.json");
        File.WriteAllText(file, "{\n  \"name\": \"Pets API\",\n  \"id\": \"not-a-guid\"\n}");

        var repo = new WorkspaceRepository(_dir);
        Assert.Empty(repo.LoadCollections());

        var error = Assert.Single(repo.LoadErrors);
        Assert.StartsWith("Collection \"Pets API\" couldn't be loaded: it is valid JSON but not in the form Hermes expects (line ", error.Summary);
        Assert.DoesNotContain("isn't valid JSON", error.Summary);
        Assert.True(error.CanMoveAside);
        Assert.Equal(file, error.Location);
        AssertPlainText(error);
    }

    [Fact]
    public void A_wrong_shape_file_with_comments_and_trailing_commas_is_still_valid_json()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        File.WriteAllText(Path.Combine(_dir, "collections", $"{Guid.NewGuid()}.json"),
            "{\n  // edited by hand\n  \"name\": \"Pets API\",\n  \"id\": \"not-a-guid\",\n}");

        var repo = new WorkspaceRepository(_dir);
        repo.LoadCollections();

        Assert.Contains("valid JSON but not in the form Hermes expects", Assert.Single(repo.LoadErrors).Summary);
    }

    [Fact]
    public void Describing_a_json_error_never_throws_when_the_file_cant_be_read_again()
    {
        Directory.CreateDirectory(_dir);
        var removed = Path.Combine(_dir, "gone.json");
        var exception = new JsonException("raw", null, 0, 3);

        var missing = StorageErrorDescriber.Describe(exception, removed, "Collection file \"gone.json\"");
        var directory = StorageErrorDescriber.Describe(exception, _dir, "Collection file \"x\"");

        Assert.Contains("isn't valid JSON", missing.Summary);
        Assert.False(missing.CanMoveAside);
        Assert.Contains("isn't valid JSON", directory.Summary);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Describing_a_json_error_for_a_file_that_became_unreadable_falls_back_to_the_syntax_wording()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File permissions are set with Unix modes here.");
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "locked.json");
        File.WriteAllText(file, "{ \"name\": 1 }");
        File.SetUnixFileMode(file, UnixFileMode.None);
        try
        {
            Assert.SkipWhen(CanReadAnyway(file), "Running with rights that ignore file permissions (root).");

            var error = StorageErrorDescriber.Describe(new JsonException("raw", null, 0, 3), file, "Collection file \"locked.json\"");

            Assert.Contains("isn't valid JSON", error.Summary);
        }
        finally
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void A_history_line_of_the_wrong_shape_is_not_called_a_syntax_error()
    {
        var repo = new WorkspaceRepository(_dir);
        repo.AppendHistory(new HistoryEntry { StatusCode = 200 });
        File.AppendAllText(Directory.GetFiles(Path.Combine(_dir, "history")).Single(), "\n{\"statusCode\": \"abc\"}");

        var fresh = new WorkspaceRepository(_dir);
        fresh.LoadHistory();

        var error = Assert.Single(fresh.LoadErrors);
        Assert.Contains("isn't in the form Hermes expects", error.Summary);
        AssertPlainText(error);
    }

    [Fact]
    public void A_truncated_environment_is_named()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "environments"));
        File.WriteAllText(Path.Combine(_dir, "environments", $"{Guid.NewGuid()}.json"), "{ \"name\": \"Staging\", \"variables\": [");

        var repo = new WorkspaceRepository(_dir);
        repo.LoadEnvironments();

        Assert.StartsWith("Environment \"Staging\" couldn't be loaded", Assert.Single(repo.LoadErrors).Summary);
    }

    [Fact]
    public void A_file_without_a_recoverable_name_is_named_by_its_file()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        File.WriteAllText(Path.Combine(_dir, "collections", "abc.json"), "{ nope");

        var repo = new WorkspaceRepository(_dir);
        repo.LoadCollections();

        Assert.StartsWith("Collection file \"abc.json\" couldn't be loaded: it isn't valid JSON (line 1, column 3)",
            Assert.Single(repo.LoadErrors).Summary);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void An_unreadable_collection_reports_missing_permission_with_a_next_step()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File permissions are set with Unix modes here.");
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        var file = Path.Combine(_dir, "collections", "locked.json");
        File.WriteAllText(file, "{ \"name\": \"Locked\" }");
        File.SetUnixFileMode(file, UnixFileMode.None);
        try
        {
            Assert.SkipWhen(CanReadAnyway(file), "Running with rights that ignore file permissions (root).");

            var repo = new WorkspaceRepository(_dir);
            Assert.Empty(repo.LoadCollections());

            var error = Assert.Single(repo.LoadErrors);
            Assert.Equal(StorageErrorKind.PermissionDenied, error.Kind);
            Assert.StartsWith("Collection file \"locked.json\" couldn't be loaded: Hermes isn't allowed to read it.", error.Summary);
            Assert.StartsWith("Check the permissions of", error.NextStep);
            Assert.False(error.CanMoveAside);
            AssertPlainText(error);
            Assert.DoesNotContain("denied", error.Message);
            Assert.Contains("UnauthorizedAccessException", error.Details);
        }
        finally
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static bool CanReadAnyway(string file)
    {
        try
        {
            File.ReadAllBytes(file);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void A_damaged_history_line_is_described_with_its_position_and_not_offered_for_moving_aside()
    {
        var repo = new WorkspaceRepository(_dir);
        repo.AppendHistory(new HistoryEntry { StatusCode = 200 });
        File.AppendAllText(Directory.GetFiles(Path.Combine(_dir, "history")).Single(), "\n{\"statusCode\": 2");

        var fresh = new WorkspaceRepository(_dir);
        fresh.LoadHistory();
        var error = Assert.Single(fresh.LoadErrors);

        Assert.Contains("line 3", error.Summary);
        Assert.Contains("skipped", error.Summary);
        Assert.False(error.CanMoveAside);
        AssertPlainText(error);
    }

    [Theory]
    [InlineData(0x80070070, StorageErrorKind.DiskFull)]
    [InlineData(0x80070027, StorageErrorKind.DiskFull)]
    [InlineData(0x80070020, StorageErrorKind.FileInUse)]
    [InlineData(0x80070021, StorageErrorKind.FileInUse)]
    [InlineData(0x80070005, StorageErrorKind.PermissionDenied)]
    [InlineData(0x80070003, StorageErrorKind.PathNotFound)]
    [InlineData(0x80070002, StorageErrorKind.PathNotFound)]
    [InlineData(0x80131620, StorageErrorKind.Other)]
    public void IOExceptions_are_classified_by_their_windows_error_code(uint hResult, StorageErrorKind expected) =>
        Assert.Equal(expected, StorageErrorDescriber.Classify(new IOException("raw text", unchecked((int)hResult))));

    [Fact]
    public void Exception_types_are_classified()
    {
        Assert.Equal(StorageErrorKind.PermissionDenied, StorageErrorDescriber.Classify(new UnauthorizedAccessException()));
        Assert.Equal(StorageErrorKind.PathNotFound, StorageErrorDescriber.Classify(new DirectoryNotFoundException()));
        Assert.Equal(StorageErrorKind.PathNotFound, StorageErrorDescriber.Classify(new FileNotFoundException()));
        Assert.Equal(StorageErrorKind.InvalidJson, StorageErrorDescriber.Classify(new JsonException()));
        Assert.Equal(StorageErrorKind.Other, StorageErrorDescriber.Classify(new IOException("who knows")));
    }

    [Fact]
    public void Unix_errno_values_are_classified_on_unix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix reports the errno as the HResult.");
        Assert.Equal(StorageErrorKind.DiskFull, StorageErrorDescriber.Classify(new IOException("raw", 28)));
        Assert.Equal(StorageErrorKind.PermissionDenied, StorageErrorDescriber.Classify(new IOException("raw", 13)));
        Assert.Equal(StorageErrorKind.PathNotFound, StorageErrorDescriber.Classify(new IOException("raw", 2)));
        Assert.Equal(StorageErrorKind.FileInUse, StorageErrorDescriber.Classify(new IOException("raw", 26)));
    }

    [Theory]
    [InlineData(0x80070070, "the disk is full.", "Free up disk space")]
    [InlineData(0x80070020, "another program is using it.", "Close the program")]
    [InlineData(0x80070005, "Hermes isn't allowed to write to it.", "Check the permissions")]
    [InlineData(0x80070003, "it or its folder wasn't found.", "Check that the folder still exists")]
    public void Each_io_failure_has_a_plain_sentence_and_a_next_step_and_keeps_the_raw_text(
        uint hResult, string reason, string nextStep)
    {
        var path = Path.Combine(_dir, "history", "2026-10-29.jsonl");
        var raw = new IOException("No space left on device : '" + path + "'", unchecked((int)hResult));

        var error = StorageErrorDescriber.Describe(raw, path, "History", StorageOperation.Write);

        Assert.StartsWith($"History couldn't be saved: ", error.Summary);
        Assert.Contains(reason, error.Summary);
        Assert.Contains(nextStep, error.NextStep);
        Assert.DoesNotContain("No space left", error.Message);
        Assert.Contains("No space left on device", error.Details);
        Assert.Equal(path, error.Location);
        AssertPlainText(error);
    }

    [Fact]
    public void The_data_folder_wording_says_it_can_t_be_used()
    {
        var error = StorageErrorDescriber.Describe(new UnauthorizedAccessException("Access to the path '/x' is denied."),
            "/x/data", "Data folder \"/x/data\"", StorageOperation.Use);

        Assert.Equal("Data folder \"/x/data\" can't be used: Hermes isn't allowed to use it.", error.Summary);
        Assert.DoesNotContain("Access to the path", error.Message);
    }

    [Fact]
    public void The_delete_wording_says_it_couldn_t_be_deleted()
    {
        var denied = StorageErrorDescriber.Describe(new UnauthorizedAccessException("Access to the path '/x/a.json' is denied."),
            "/x/a.json", "Collection \"Pets\"", StorageOperation.Delete);
        var other = StorageErrorDescriber.Describe(new IOException("odd"), "/x/a.json", "Collection \"Pets\"", StorageOperation.Delete);

        Assert.Equal("Collection \"Pets\" couldn't be deleted: Hermes isn't allowed to delete it.", denied.Summary);
        Assert.Equal("Collection \"Pets\" couldn't be deleted: Hermes couldn't delete it.", other.Summary);
        AssertPlainText(denied);
    }

    [Fact]
    public void A_corrupt_file_can_be_moved_aside_without_overwriting_an_earlier_one()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "bad.json");
        File.WriteAllText(file, "first");
        Assert.Equal(file + ".corrupt", CorruptFile.MoveAside(file));
        File.WriteAllText(file, "second");

        var second = CorruptFile.MoveAside(file);

        Assert.Equal(file + ".corrupt2", second);
        Assert.False(File.Exists(file));
        Assert.Equal("first", File.ReadAllText(file + ".corrupt"));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void After_moving_a_corrupt_collection_aside_the_next_start_is_clean()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        File.WriteAllText(Path.Combine(_dir, "collections", "bad.json"), "{ \"name\": \"Pets\", ");
        var first = new WorkspaceRepository(_dir);
        first.LoadCollections();
        CorruptFile.MoveAside(Assert.Single(first.LoadErrors).Location!);

        var second = new WorkspaceRepository(_dir);
        Assert.Empty(second.LoadCollections());
        Assert.Empty(second.LoadErrors);
    }

    [Theory]
    [InlineData("collections")]
    [InlineData("environments")]
    [InlineData("history")]
    [UnsupportedOSPlatform("windows")]
    public void An_unlistable_data_subfolder_is_reported_and_loading_continues(string subfolder)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Folder permissions are set with Unix modes here.");
        var folder = Path.Combine(_dir, subfolder);
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.None);
        try
        {
            Assert.SkipWhen(CanListAnyway(folder), "Running with rights that ignore folder permissions (root).");

            var repo = new WorkspaceRepository(_dir);
            Assert.Empty(repo.LoadCollections());
            Assert.Empty(repo.LoadEnvironments());
            Assert.Empty(repo.LoadHistory());

            var error = Assert.Single(repo.LoadErrors);
            Assert.Equal(StorageErrorKind.PermissionDenied, error.Kind);
            Assert.Contains($"folder \"{folder}\" couldn't be loaded", error.Summary);
            Assert.Equal(folder, error.Location);
            AssertPlainText(error);
        }
        finally
        {
            RestoreFolder(folder);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void The_startup_check_names_a_subfolder_that_cant_be_listed_and_says_the_data_stays()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Folder permissions are set with Unix modes here.");
        var folder = Path.Combine(_dir, "collections");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(_dir, "environments"));
        File.SetUnixFileMode(folder, UnixFileMode.None);
        try
        {
            Assert.SkipWhen(CanListAnyway(folder), "Running with rights that ignore folder permissions (root).");

            var error = DataFolderCheck.FindUnusableSubfolder(_dir)!;

            Assert.Equal(StorageErrorKind.PermissionDenied, error.Kind);
            Assert.Equal($"Collections folder \"{folder}\" can't be used: Hermes isn't allowed to use it.", error.Summary);
            Assert.Equal("Fix the folder's permissions and restart Hermes, or choose another data folder (your collections stay in this one).",
                error.NextStep);
            Assert.Equal(folder, error.Location);
            AssertPlainText(error);
        }
        finally
        {
            RestoreFolder(folder);
        }
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void The_startup_check_finds_a_subfolder_that_can_be_listed_but_not_written_to()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Folder permissions are set with Unix modes here.");
        var folder = Path.Combine(_dir, "history");
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.SkipWhen(CanWriteAnyway(folder), "Running with rights that ignore folder permissions (root).");

            var error = DataFolderCheck.FindUnusableSubfolder(_dir)!;

            Assert.StartsWith("History folder", error.Summary);
            Assert.Contains("(your history stays in this one)", error.NextStep);
        }
        finally
        {
            RestoreFolder(folder);
        }
    }

    [Fact]
    public void The_startup_check_accepts_missing_and_usable_subfolders_and_leaves_no_probe_file_behind()
    {
        Assert.Null(DataFolderCheck.FindUnusableSubfolder(_dir));

        var folder = Path.Combine(_dir, "responses");
        Directory.CreateDirectory(folder);
        Assert.Null(DataFolderCheck.FindUnusableSubfolder(_dir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    private static bool CanListAnyway(string folder)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(folder).ToList();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanWriteAnyway(string folder)
    {
        var probe = Path.Combine(folder, "probe");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void RestoreFolder(string folder) =>
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            if (!OperatingSystem.IsWindows())
                foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                    File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(_dir, true);
        }
    }
}
