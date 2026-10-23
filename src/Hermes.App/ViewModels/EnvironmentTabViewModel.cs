using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels;

public sealed partial class EnvironmentTabViewModel : TabViewModelBase
{
    private readonly EnvironmentModel _environment;
    private string _snapshot;
    private bool _loading;

    public EnvironmentTabViewModel(TabServices services, EnvironmentModel environment) : base(services)
    {
        _environment = environment;
        Variables.Changed += UpdateDirty;
        Load();
        _snapshot = Snapshot();
    }

    public override SessionTabKind Kind => SessionTabKind.Environment;
    public override Guid EntityId => _environment.Id;
    public override string KindLabel => IsGlobals ? "GLB" : "ENV";

    public bool IsGlobals => _environment == Services.Workspace.Globals;
    public bool CanRename => !IsGlobals;

    public VariableListViewModel Variables { get; } = new();

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial bool ConfirmBeforeSend { get; set; }

    public bool IsActiveEnvironment => Services.Workspace.ActiveEnvironment == _environment;

    private void Load()
    {
        _loading = true;
        Name = _environment.Name;
        Title = _environment.Name;
        ConfirmBeforeSend = _environment.ConfirmBeforeSend;
        Variables.Load(_environment.Variables);
        _loading = false;
    }

    private string Snapshot() => ModelCloner.Serialize(new EnvironmentModel
    {
        Id = _environment.Id,
        Name = Name,
        Variables = Variables.ToModels(),
        ConfirmBeforeSend = ConfirmBeforeSend,
    });

    partial void OnConfirmBeforeSendChanged(bool value) => UpdateDirty();

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

    public override Task<bool> SaveAsync()
    {
        if (!IsGlobals)
            _environment.Name = string.IsNullOrWhiteSpace(Name) ? "New Environment" : Name.Trim();
        _environment.Variables = Variables.ToModels();
        _environment.ConfirmBeforeSend = ConfirmBeforeSend && !IsGlobals;
        Services.Workspace.SaveEnvironment(_environment);
        Name = _environment.Name;
        _snapshot = Snapshot();
        UpdateDirty();
        return Task.FromResult(true);
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    [RelayCommand]
    private void SetActive()
    {
        if (!IsGlobals)
            Services.Workspace.ActiveEnvironment = _environment;
    }

    public override void OnVariablesChanged()
    {
        OnPropertyChanged(nameof(IsActiveEnvironment));
        if (!IsDirty && Name != _environment.Name)
        {
            Load();
            _snapshot = Snapshot();
        }
    }
}
