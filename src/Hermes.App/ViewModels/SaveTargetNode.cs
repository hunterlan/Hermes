using System.Collections.ObjectModel;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

public sealed class SaveTargetNode
{
    public required string Name { get; init; }
    public required CollectionModel Collection { get; init; }
    public FolderModel? Folder { get; init; }
    public ObservableCollection<SaveTargetNode> Children { get; } = [];
    public bool IsFolder => Folder is not null;
    public string Path { get; init; } = "";
}
