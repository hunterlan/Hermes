using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <param name="IsNewCollection">The collection was created in the dialog and isn't in the workspace yet.</param>
public sealed record SaveRequestTarget(string Name, CollectionModel Collection, FolderModel? Folder, bool IsNewCollection);

public sealed class SaveTargetNode
{
    public required string Name { get; init; }
    public required CollectionModel Collection { get; init; }
    public FolderModel? Folder { get; init; }
    public ObservableCollection<SaveTargetNode> Children { get; } = [];
    public bool IsFolder => Folder is not null;
    public string Path { get; init; } = "";
}

public sealed partial class SaveRequestDialogViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;

    /// <summary>Created with "New collection"; added to the workspace by the caller only if the request is saved there.</summary>
    private CollectionModel? _newCollection;

    public SaveRequestDialogViewModel(WorkspaceService workspace, string name)
    {
        _workspace = workspace;
        Name = name;
        Rebuild();
        SelectedTarget = Targets.FirstOrDefault();
    }

    public ObservableCollection<SaveTargetNode> Targets { get; } = [];

    [ObservableProperty] public partial string Name { get; set; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))] public partial SaveTargetNode? SelectedTarget { get; set; }
    [ObservableProperty] public partial string NewCollectionName { get; set; } = "";
    [ObservableProperty] public partial bool IsCreatingCollection { get; set; }

    public bool HasNoCollections => Targets.Count == 0;
    public string SaveButtonText => SelectedTarget is null ? "Save" : $"Save to {SelectedTarget.Name}";

    public event Action<SaveRequestTarget?>? CloseRequested;

    partial void OnSelectedTargetChanged(SaveTargetNode? value) => OnPropertyChanged(nameof(SaveButtonText));

    private void Rebuild()
    {
        Targets.Clear();
        var collections = _newCollection is null ? _workspace.Collections : [.. _workspace.Collections, _newCollection];
        foreach (var collection in collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var node = new SaveTargetNode { Name = collection.Name, Collection = collection, Path = collection.Name };
            AddFolders(node, collection, collection.Items, collection.Name);
            Targets.Add(node);
        }
        OnPropertyChanged(nameof(HasNoCollections));
    }

    private static void AddFolders(SaveTargetNode parent, CollectionModel collection, IEnumerable<CollectionNode> items, string path)
    {
        foreach (var folder in items.OfType<FolderModel>())
        {
            var p = $"{path} / {folder.Name}";
            var node = new SaveTargetNode { Name = folder.Name, Collection = collection, Folder = folder, Path = p };
            AddFolders(node, collection, folder.Items, p);
            parent.Children.Add(node);
        }
    }

    [RelayCommand]
    private void StartNewCollection()
    {
        IsCreatingCollection = true;
        NewCollectionName = "New Collection";
    }

    [RelayCommand]
    private void CreateCollection()
    {
        var name = NewCollectionName.Trim();
        if (name.Length == 0)
            return;
        _newCollection = new CollectionModel { Name = name };
        IsCreatingCollection = false;
        Rebuild();
        SelectedTarget = Targets.FirstOrDefault(t => t.Collection == _newCollection);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (SelectedTarget is null)
            return;
        var name = string.IsNullOrWhiteSpace(Name) ? "Untitled Request" : Name.Trim();
        CloseRequested?.Invoke(new SaveRequestTarget(name, SelectedTarget.Collection, SelectedTarget.Folder,
            IsNewCollection: SelectedTarget.Collection == _newCollection));
    }

    private bool CanSave() => SelectedTarget is not null;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
