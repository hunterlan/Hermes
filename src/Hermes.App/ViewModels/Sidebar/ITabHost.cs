using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

/// <summary>What sidebar panes need from the main window: opening and closing tabs.</summary>
public interface ITabHost
{
    void OpenRequest(HttpRequestModel model);
    void OpenCollection(CollectionModel collection);
    void OpenFolder(FolderModel folder);
    void OpenEnvironment(EnvironmentModel environment);

    /// <summary>Opens an unsaved copy of a request (from history).</summary>
    void OpenDraft(HttpRequestModel model);

    /// <summary>Closes tabs bound to the given entities without prompting (they were deleted).</summary>
    void CloseTabsFor(IEnumerable<Guid> entityIds);

    /// <summary>A request was renamed from the sidebar; open tabs follow.</summary>
    void OnRequestRenamed(Guid requestId, string name);
}
