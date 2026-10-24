using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels;

/// <summary>Folder settings: overview and authorization.</summary>
public sealed partial class FolderTabViewModel : TabViewModelBase
{
    private readonly FolderModel _folder;
    private string _snapshot;
    private bool _loading;

    public FolderTabViewModel(TabServices services, FolderModel folder) : base(services)
    {
        _folder = folder;
        Auth = services.CreateAuthEditor(allowInherit: true);
        Auth.Changed += UpdateDirty;
        Auth.ScopeProvider = () => CreateContext()?.BuildScope() ?? Core.Variables.VariableScope.Empty;
        Auth.InheritedAuthProvider = () => CreateContext()?.ResolveAuth(AuthConfig.Inherit()) ?? new Core.Auth.EffectiveAuth(AuthConfig.NoAuth(), null);
        Auth.ChainProvider = () => (AuthInheritance.OuterLevels(CreateContext()), "Folder", Name);
        Auth.ContainedItemsProvider = () => _folder.Items;
        Load();
        _snapshot = Snapshot();
    }

    public override SessionTabKind Kind => SessionTabKind.Folder;
    public override Guid EntityId => _folder.Id;
    public override string KindLabel => "DIR";

    public AuthEditorViewModel Auth { get; }

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial int SelectedSection { get; set; }

    public string Breadcrumb => Services.Workspace.Find(_folder.Id) is { } location
        ? string.Join(" / ", new[] { location.Collection.Name }.Concat(location.Ancestors.Select(a => a.Name)))
        : "";

    private Core.Http.RequestContext? CreateContext() =>
        Services.Workspace.Find(_folder.Id) is { } location
            ? Services.Workspace.CreateContextForContainer(location.Collection, _folder)
            : null;

    private void Load()
    {
        _loading = true;
        Name = _folder.Name;
        Title = _folder.Name;
        Description = _folder.Description ?? "";
        Auth.Load(_folder.Auth);
        _loading = false;
    }

    private string Snapshot() => JsonSerializer.Serialize(new FolderModel
    {
        Id = _folder.Id,
        Name = Name,
        Description = Description,
        Auth = Auth.ToModel(),
    }, HermesJsonContext.Default.FolderModel);

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
        if (Services.Workspace.Find(_folder.Id) is not { } location)
            return Task.FromResult(true);
        _folder.Name = string.IsNullOrWhiteSpace(Name) ? "New Folder" : Name.Trim();
        _folder.Description = string.IsNullOrWhiteSpace(Description) ? null : Description;
        _folder.Auth = Auth.ToModel();
        Services.Workspace.SaveCollection(location.Collection);
        Name = _folder.Name;
        _snapshot = Snapshot();
        UpdateDirty();
        return Task.FromResult(true);
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    public override void OnWorkspaceChanged()
    {
        OnPropertyChanged(nameof(Breadcrumb));
        Auth.RefreshInherited();
        if (!IsDirty && Name != _folder.Name)
        {
            Load();
            _snapshot = Snapshot();
        }
    }

    public override void OnVariablesChanged() => Auth.RefreshInherited();
}
