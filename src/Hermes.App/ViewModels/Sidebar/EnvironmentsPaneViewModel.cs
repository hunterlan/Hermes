using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels.Sidebar;

public sealed partial class EnvironmentItemViewModel(EnvironmentModel model, bool isGlobals, bool isActive) : ObservableObject
{
    public EnvironmentModel Model { get; } = model;
    public string Name => Model.Name;
    public bool IsGlobals { get; } = isGlobals;
    public bool IsActive { get; } = isActive;
    public bool IsRegular => !IsGlobals;
    public bool ConfirmBeforeSend => Model.ConfirmBeforeSend;

    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial bool IsRenaming { get; set; }
}

public sealed partial class EnvironmentsPaneViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private readonly DialogService _dialogs;
    private readonly ITabHost _host;

    public EnvironmentsPaneViewModel(WorkspaceService workspace, DialogService dialogs, ITabHost host)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _host = host;
        workspace.EnvironmentsChanged += Rebuild;
        workspace.VariablesChanged += Rebuild;
        Rebuild();
    }

    public ObservableCollection<EnvironmentItemViewModel> Items { get; } = [];

    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial EnvironmentItemViewModel? SelectedItem { get; set; }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void Rebuild()
    {
        var selected = SelectedItem?.Model.Id;
        Items.Clear();
        Items.Add(new EnvironmentItemViewModel(_workspace.Globals, isGlobals: true, isActive: false));
        foreach (var env in _workspace.Environments)
            Items.Add(new EnvironmentItemViewModel(env, isGlobals: false, isActive: env == _workspace.ActiveEnvironment));
        ApplyFilter();
        SelectedItem = Items.FirstOrDefault(i => i.Model.Id == selected);
    }

    private void ApplyFilter()
    {
        foreach (var item in Items)
            item.IsVisible = Filter.Length == 0 || item.IsGlobals || item.Name.Contains(Filter.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    [RelayCommand]
    public void Open(EnvironmentItemViewModel? item)
    {
        if (item is not null)
            _host.OpenEnvironment(item.Model);
    }

    [RelayCommand]
    private void OpenMatrix() => _host.OpenEnvironmentMatrix();

    [RelayCommand]
    private void NewEnvironment()
    {
        var env = _workspace.CreateEnvironment("New Environment");
        if (Items.FirstOrDefault(i => i.Model == env) is { } item)
        {
            SelectedItem = item;
            item.IsRenaming = true;
        }
    }

    [RelayCommand]
    private void Rename(EnvironmentItemViewModel? item)
    {
        if (item is { IsGlobals: false })
            item.IsRenaming = true;
    }

    public void CommitRename(EnvironmentItemViewModel item, string? name)
    {
        item.IsRenaming = false;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name == item.Model.Name)
            return;
        item.Model.Name = name;
        _workspace.SaveEnvironment(item.Model);
    }

    [RelayCommand]
    private void Duplicate(EnvironmentItemViewModel? item)
    {
        if (item is null)
            return;
        var copy = ModelCloner.Clone(item.Model);
        copy.Id = Guid.NewGuid();
        copy.Name = item.Model.Name + " Copy";
        _workspace.AddEnvironment(copy);
    }

    [RelayCommand]
    private async Task Delete(EnvironmentItemViewModel? item)
    {
        if (item is not { IsGlobals: false })
            return;
        if (!await _dialogs.ConfirmAsync($"Delete \"{item.Name}\"?", "This environment and its variables will be permanently deleted."))
            return;
        _workspace.DeleteEnvironment(item.Model);
        _host.CloseTabsFor([item.Model.Id]);
    }

    [RelayCommand]
    private void SetActive(EnvironmentItemViewModel? item)
    {
        if (item is { IsGlobals: false })
            _workspace.ActiveEnvironment = item.IsActive ? null : item.Model;
    }

    [RelayCommand]
    private async Task Export(EnvironmentItemViewModel? item)
    {
        if (item is null)
            return;
        var suffix = item.IsGlobals ? "postman_globals" : "postman_environment";
        if (await _dialogs.SaveFileAsync("Export environment", $"{item.Name}.{suffix}.json", ".json") is { } path)
            await File.WriteAllTextAsync(path, PostmanEnvironmentConverter.Export(item.Model, item.IsGlobals));
    }
}
