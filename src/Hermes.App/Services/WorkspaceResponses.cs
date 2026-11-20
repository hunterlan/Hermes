using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>The responses kept with saved requests.</summary>
internal sealed class WorkspaceResponses(WorkspaceRepository repository, WorkspaceStorage storage)
{
    /// <summary>Saved responses for a request, newest first. Files that can't be read are reported and skipped.</summary>
    public IReadOnlyList<SavedResponse> Load(Guid requestId)
    {
        var loaded = repository.LoadResponses(requestId);
        foreach (var error in loaded.Errors)
            storage.Report(error);
        return loaded.Items;
    }

    /// <summary>Saves a response with its request; returns what was stored, or null (after reporting it) if it couldn't be written.</summary>
    public SavedResponse? Save(Guid requestId, SavedResponse saved)
    {
        try
        {
            return repository.SaveResponse(requestId, saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            storage.Report(StorageErrorDescriber.Describe(ex, Path.Combine(repository.Root, "responses", requestId.ToString()),
                "The response", StorageOperation.Write, target: StorageTarget.Folder));
            return null;
        }
    }

    /// <summary>
    /// Deletes the saved responses of the requests in <paramref name="nodes"/> and their folders, on the write queue.
    /// The nodes are already gone from the collection; saved responses that stay behind are only wasted space, so a
    /// failure is just reported.
    /// </summary>
    public async Task DeleteForAsync(IEnumerable<CollectionNode> nodes)
    {
        // The ids are taken now: the nodes belong to the UI thread, and the deletes run after it moved on.
        foreach (var requestId in RequestsIn(nodes).Select(request => request.Id).ToList())
        {
            storage.Report(await storage.AttemptAsync(() => repository.DeleteResponses(requestId),
                repository.ResponsesFolder(requestId), "Saved responses", StorageOperation.Delete));
        }
    }

    private static IEnumerable<HttpRequestModel> RequestsIn(IEnumerable<CollectionNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is HttpRequestModel request)
                yield return request;
            else if (node is FolderModel folder)
                foreach (var inner in RequestsIn(folder.Items))
                    yield return inner;
        }
    }
}
