using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly TabServices _services;

    public MainWindowViewModel(TabServices services)
    {
        _services = services;
        Workspace.VariablesChanged += () =>
        {
            foreach (var tab in Tabs)
                tab.OnVariablesChanged();
        };
        Workspace.CollectionChanged += _ =>
        {
            foreach (var tab in Tabs)
                tab.OnWorkspaceChanged();
        };

        NewRequest();
    }

    private WorkspaceService Workspace => _services.Workspace;
    private DialogService Dialogs => _services.Dialogs;

    public ObservableCollection<TabViewModelBase> Tabs { get; } = [];

    [ObservableProperty] public partial TabViewModelBase? ActiveTab { get; set; }

    public bool HasTabs => Tabs.Count > 0;

    partial void OnActiveTabChanged(TabViewModelBase? oldValue, TabViewModelBase? newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        if (newValue is not null)
            newValue.IsActive = true;
    }

    // ---- tabs ----

    [RelayCommand]
    public void NewRequest() => AddTab(new RequestTabViewModel(_services, HttpRequestModel.CreateNew()));

    public void AddTab(TabViewModelBase tab)
    {
        var index = ActiveTab is null ? Tabs.Count : Tabs.IndexOf(ActiveTab) + 1;
        Tabs.Insert(index, tab);
        ActiveTab = tab;
        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>Focuses an open tab for the entity, or opens a new one.</summary>
    public void OpenOrFocus(Guid entityId, Func<TabViewModelBase> create)
    {
        ActiveTab = Tabs.FirstOrDefault(t => t.EntityId == entityId) ?? Add(create());

        TabViewModelBase Add(TabViewModelBase tab)
        {
            AddTab(tab);
            return tab;
        }
    }

    public void OpenRequest(HttpRequestModel model) =>
        OpenOrFocus(model.Id, () => new RequestTabViewModel(_services, ModelCloner.Clone(model)));

    [RelayCommand]
    private void SelectTab(TabViewModelBase tab) => ActiveTab = tab;

    [RelayCommand]
    public async Task CloseTab(TabViewModelBase? tab)
    {
        tab ??= ActiveTab;
        if (tab is null)
            return;

        if (tab.IsDirty)
        {
            switch (await Dialogs.AskSaveChangesAsync(tab.Title))
            {
                case SaveChangesResult.Cancel:
                    return;
                case SaveChangesResult.Save when !await tab.SaveAsync():
                    return;
            }
        }
        RemoveTab(tab);
    }

    /// <summary>Removes a tab without prompting (e.g. its entity was deleted).</summary>
    public void RemoveTab(TabViewModelBase tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;
        Tabs.RemoveAt(index);
        if (ActiveTab == tab)
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        OnPropertyChanged(nameof(HasTabs));
    }

    [RelayCommand]
    private async Task CloseOtherTabs(TabViewModelBase tab)
    {
        foreach (var other in Tabs.Where(t => t != tab).ToList())
            await CloseTab(other);
    }

    [RelayCommand]
    private async Task CloseAllTabs()
    {
        foreach (var tab in Tabs.ToList())
            await CloseTab(tab);
    }

    [RelayCommand]
    private void DuplicateTab(TabViewModelBase tab)
    {
        if (tab is not RequestTabViewModel request)
            return;
        var copy = request.BuildModel();
        copy.Id = Guid.NewGuid();
        copy.Name += " Copy";
        AddTab(new RequestTabViewModel(_services, copy, ModelCloner.Serialize(HttpRequestModel.CreateNew())));
    }

    [RelayCommand]
    private void NextTab() => CycleTab(1);

    [RelayCommand]
    private void PreviousTab() => CycleTab(-1);

    private void CycleTab(int delta)
    {
        if (Tabs.Count == 0)
            return;
        var index = ActiveTab is null ? 0 : Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(index + delta + Tabs.Count) % Tabs.Count];
    }

    [RelayCommand]
    private async Task SaveActive()
    {
        if (ActiveTab is not null)
            await ActiveTab.SaveAsync();
    }

    [RelayCommand]
    private async Task SendActive()
    {
        if (ActiveTab is RequestTabViewModel request)
            await request.SendCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ToggleTheme() => _services.Settings.ToggleTheme();
}
