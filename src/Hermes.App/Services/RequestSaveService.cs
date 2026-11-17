using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>
/// The storage rules for saving a request: over the node it already is, or as a new request into a
/// <see cref="SaveRequestTarget"/>. A failed write is offered again with Retry; when it is given up, nothing is left
/// changed in the workspace and the method returns false. UI thread only: the failure dialog is shown from here.
/// </summary>
public sealed class RequestSaveService(WorkspaceService workspace, DialogService dialogs)
{
    /// <summary>Replaces the node at <paramref name="location"/> with <paramref name="model"/> and writes its collection.</summary>
    public async Task<bool> SaveExistingAsync(NodeLocation location, HttpRequestModel model)
    {
        var index = location.Container.IndexOf(location.Node);
        var previous = location.Container[index];
        location.Container[index] = model;
        while (workspace.TrySaveCollection(location.Collection) is { } error)
        {
            if (await dialogs.ShowSaveFailedAsync(error))
                continue;
            // Not on disk, so not in the collection either.
            location.Container[location.Container.IndexOf(model)] = previous;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Adds <paramref name="model"/> to the folder or collection of <paramref name="target"/>. A new collection is
    /// written once, with the request in it, so a failure can't leave an empty collection behind.
    /// </summary>
    public async Task<bool> SaveNewAsync(SaveRequestTarget target, HttpRequestModel model)
    {
        if (!target.IsNewCollection)
        {
            while (workspace.TryAddNode(target.Collection, target.Folder, model) is { } error)
            {
                if (!await dialogs.ShowSaveFailedAsync(error))
                    return false;
            }
            return true;
        }

        target.Collection.Items.Add(model);
        while (workspace.TryAddCollection(target.Collection) is { } collectionError)
        {
            if (await dialogs.ShowSaveFailedAsync(collectionError))
                continue;
            target.Collection.Items.Remove(model);
            return false;
        }
        return true;
    }
}
