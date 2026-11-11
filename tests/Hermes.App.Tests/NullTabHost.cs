using Hermes.App.ViewModels.Sidebar;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>A tab host that opens nothing, for testing a sidebar pane on its own.</summary>
internal sealed class NullTabHost : ITabHost
{
    public bool ShowTabs => true;
    public List<Guid> ClosedEntities { get; } = [];
    public List<RenamedRequest> RenamedRequests { get; } = [];

    public Task OpenRequestAsync(HttpRequestModel model) => Task.CompletedTask;
    public Task PreviewRequestAsync(HttpRequestModel model) => Task.CompletedTask;
    public Task OpenCollectionAsync(CollectionModel collection) => Task.CompletedTask;
    public Task OpenFolderAsync(FolderModel folder) => Task.CompletedTask;
    public Task OpenEnvironmentAsync(EnvironmentModel environment) => Task.CompletedTask;
    public Task OpenEnvironmentMatrixAsync() => Task.CompletedTask;
    public Task OpenHistoryDraftAsync(HttpRequestModel model) => Task.CompletedTask;
    public void CloseTabsFor(IEnumerable<Guid> entityIds) => ClosedEntities.AddRange(entityIds);
    public void OnRequestRenamed(Guid requestId, string name) => RenamedRequests.Add(new RenamedRequest(requestId, name));
}
