using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <param name="IsNewCollection">The collection was created in the dialog and isn't in the workspace yet.</param>
public sealed record SaveRequestTarget(string Name, CollectionModel Collection, FolderModel? Folder, bool IsNewCollection);
