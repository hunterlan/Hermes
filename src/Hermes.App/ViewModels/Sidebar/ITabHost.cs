using Hermes.Core.Models;

namespace Hermes.App.ViewModels.Sidebar;

/// <summary>
/// What sidebar panes need from the main window: opening and closing tabs. With tabs off, opening an item may first
/// ask to save the current tab; the returned task finishes once that's answered.
/// </summary>
public interface ITabHost
{
    /// <summary>False in single-tab mode (Settings → Tabs off), where opening an item replaces the current tab.</summary>
    bool ShowTabs { get; }

    Task OpenRequestAsync(HttpRequestModel model);

    /// <summary>Opens a request in the preview tab (single click), replacing the previous preview.</summary>
    Task PreviewRequestAsync(HttpRequestModel model);
    Task OpenCollectionAsync(CollectionModel collection);
    Task OpenFolderAsync(FolderModel folder);
    Task OpenEnvironmentAsync(EnvironmentModel environment);

    /// <summary>Opens the side-by-side view of every variable in every environment.</summary>
    Task OpenEnvironmentMatrixAsync();

    /// <summary>Opens an unsaved copy of a History entry. With tabs off, it's replaced without asking until edited.</summary>
    Task OpenHistoryDraftAsync(HttpRequestModel model);

    /// <summary>Closes tabs bound to the given entities without prompting (they were deleted).</summary>
    void CloseTabsFor(IEnumerable<Guid> entityIds);

    /// <summary>A request was renamed from the sidebar; open tabs follow.</summary>
    void OnRequestRenamed(Guid requestId, string name);
}
