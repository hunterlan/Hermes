using Hermes.App.Services;
using Hermes.Core.Storage;
using Hermes.TestSupport;

namespace Hermes.App.Tests;

/// <summary>
/// A workspace in a temp folder of its own, so a test can make parts of it read-only without touching the workspace
/// the other tests share. Dispose gives the permissions back and deletes the folder.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public const string NotEnforced = "The operating system doesn't enforce read-only folders here (Windows, or running as root).";

    private readonly List<ReadOnlyFolder> _locked = [];

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "hermes-write-tests-" + Guid.NewGuid());
    public WorkspaceRepository Repository { get; }
    public SettingsService Settings { get; }
    public WorkspaceService Workspace { get; }

    /// <summary>What <see cref="WorkspaceService.StorageProblem"/> reported, in order.</summary>
    public List<StorageError> Problems { get; } = [];

    public TempWorkspace()
    {
        Directory.CreateDirectory(Root);
        Repository = new WorkspaceRepository(Root);
        Settings = new SettingsService();
        Workspace = new WorkspaceService(Repository, Settings, TimeProvider.System);
        Workspace.StorageProblem += Problems.Add;
    }

    /// <summary>Makes a folder under the data folder read-only (the data folder itself for an empty name).</summary>
    public ReadOnlyFolder Lock(string folderName = "")
    {
        var folder = ReadOnlyFolder.Create(Path.Combine(Root, folderName));
        _locked.Add(folder);
        return folder;
    }

    public void Dispose()
    {
        foreach (var folder in _locked)
            folder.Dispose();
        Workspace.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }
}
