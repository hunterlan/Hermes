using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels;

/// <summary>Collection settings: overview, authorization and variables.</summary>
public sealed partial class CollectionTabViewModel : TabViewModelBase
{
    private readonly CollectionModel _collection;
    private string _snapshot;
    private bool _loading;

    public CollectionTabViewModel(TabServices services, CollectionModel collection) : base(services)
    {
        _collection = collection;
        Auth = services.CreateAuthEditor(allowInherit: false);
        Auth.ScopeProvider = () => services.Workspace.CreateContextForContainer(_collection, null).BuildScope();
        Auth.Changed += UpdateDirty;
        Variables.Changed += UpdateDirty;
        Load();
        _snapshot = Snapshot();
    }

    public override SessionTabKind Kind => SessionTabKind.Collection;
    public override Guid EntityId => _collection.Id;
    public override string KindLabel => "COL";

    public AuthEditorViewModel Auth { get; }
    public VariableListViewModel Variables { get; } = new();

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial int SelectedSection { get; set; }

    public int RequestCount => Count(_collection.Items, f => f is HttpRequestModel);
    public int FolderCount => Count(_collection.Items, f => f is FolderModel);

    private static int Count(IEnumerable<CollectionNode> items, Func<CollectionNode, bool> predicate) =>
        items.Sum(i => (predicate(i) ? 1 : 0) + (i is FolderModel f ? Count(f.Items, predicate) : 0));

    private void Load()
    {
        _loading = true;
        Name = _collection.Name;
        Title = _collection.Name;
        Description = _collection.Description ?? "";
        Auth.Load(_collection.Auth);
        Variables.Load(_collection.Variables);
        _loading = false;
    }

    private string Snapshot() => JsonSerializer.Serialize(new CollectionModel
    {
        Id = _collection.Id,
        Name = Name,
        Description = Description,
        Auth = Auth.ToModel(),
        Variables = Variables.ToModels(),
    }, HermesJsonContext.Default.CollectionModel);

    private void UpdateDirty()
    {
        if (!_loading)
            IsDirty = Snapshot() != _snapshot;
    }

    partial void OnNameChanged(string value)
    {
        Title = value;
        UpdateDirty();
    }

    partial void OnDescriptionChanged(string value) => UpdateDirty();

    public override Task<bool> SaveAsync()
    {
        _collection.Name = string.IsNullOrWhiteSpace(Name) ? "New Collection" : Name.Trim();
        _collection.Description = string.IsNullOrWhiteSpace(Description) ? null : Description;
        _collection.Auth = Auth.ToModel();
        _collection.Variables = Variables.ToModels();
        Services.Workspace.SaveCollection(_collection);
        Name = _collection.Name;
        _snapshot = Snapshot();
        UpdateDirty();
        return Task.FromResult(true);
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    public override void OnWorkspaceChanged()
    {
        OnPropertyChanged(nameof(RequestCount));
        OnPropertyChanged(nameof(FolderCount));
        // Renamed from the sidebar while the tab was clean: follow it.
        if (!IsDirty && Name != _collection.Name)
        {
            Load();
            _snapshot = Snapshot();
        }
    }
}
