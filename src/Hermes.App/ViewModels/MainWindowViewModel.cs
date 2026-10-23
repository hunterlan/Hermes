using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.App.ViewModels.Sidebar;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.ViewModels;

public enum SidebarPane
{
    Collections,
    Environments,
    History,
}

/// <summary>Entry in the environment selector; <see cref="Model"/> is null for "No Environment".</summary>
public sealed record EnvironmentOption(string Name, EnvironmentModel? Model)
{
    public override string ToString() => Name;
}

/// <summary>Row in the environment quick-look popup.</summary>
public sealed record QuickLookVariable(string Key, string InitialValue, string CurrentValue);

public sealed record QuickLookSection(IReadOnlyList<QuickLookVariable> Rows)
{
    public bool IsEmpty => Rows.Count == 0;
}

public sealed partial class MainWindowViewModel : ObservableObject, ITabHost
{
    private readonly TabServices _services;
    private bool _syncingEnvironment;

    public MainWindowViewModel(TabServices services)
    {
        _services = services;
        Collections = new CollectionsPaneViewModel(services.Workspace, services.Dialogs, this);
        Environments = new EnvironmentsPaneViewModel(services.Workspace, services.Dialogs, this);
        History = new HistoryPaneViewModel(services.Workspace, services.Dialogs, this);

        Workspace.VariablesChanged += () =>
        {
            foreach (var tab in Tabs)
                tab.OnVariablesChanged();
            RefreshEnvironmentOptions();
        };
        Workspace.EnvironmentsChanged += () =>
        {
            RefreshEnvironmentOptions();
            foreach (var matrix in Tabs.OfType<EnvironmentMatrixTabViewModel>())
                matrix.OnVariablesChanged();
        };
        Tabs.CollectionChanged += (_, e) =>
        {
            foreach (var tab in e.NewItems?.OfType<TabViewModelBase>() ?? [])
                tab.PropertyChanged += OnTabPropertyChanged;
            foreach (var tab in e.OldItems?.OfType<TabViewModelBase>() ?? [])
                tab.PropertyChanged -= OnTabPropertyChanged;
            OnTabsChanged();
        };
        services.Settings.Changed += () =>
        {
            OnPropertyChanged(nameof(ShowTabs));
            OnPropertyChanged(nameof(IsTabStripVisible));
        };
        Workspace.CollectionChanged += _ => NotifyWorkspaceChanged();
        Workspace.Secrets.ProtectionLost += () => Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AreSecretsUnprotected));
            OnPropertyChanged(nameof(SecretsWarning));
        });
        Workspace.CollectionsListChanged += NotifyWorkspaceChanged;
        Workspace.StorageProblem += message => Dispatcher.UIThread.Post(() => _ = Dialogs.ShowMessageAsync("Storage problem", message));
        RefreshEnvironmentOptions();
        CommandPalette = new CommandPaletteViewModel(PaletteItems);

        RestoreSession();
    }

    public CommandPaletteViewModel CommandPalette { get; }

    [RelayCommand]
    private void OpenCommandPalette() => CommandPalette.Open();

    private IEnumerable<PaletteItem> PaletteItems()
    {
        foreach (var (request, path) in CommandPaletteViewModel.Requests(Workspace.Collections))
            yield return new PaletteItem(request.Name, $"{path} · {request.Url}", request.Method, null, () => OpenRequest(request));

        yield return new PaletteItem("New request", null, null, "Ctrl T", NewRequest);
        yield return new PaletteItem("Import…", "Postman collection or environment, cURL", null, "Ctrl O", () => _ = OpenImport());
        yield return new PaletteItem("Open environments matrix", null, null, null, OpenEnvironmentMatrix);
        foreach (var option in EnvironmentOptions)
            yield return new PaletteItem($"Use environment: {option.Name}", null, null, null, () => SelectedEnvironment = option);
        yield return new PaletteItem("Toggle sidebar", null, null, "Ctrl \\", () => IsSidebarVisible = !IsSidebarVisible);
        yield return new PaletteItem("Show collections", null, null, null, () => ShowPane(SidebarPane.Collections));
        yield return new PaletteItem("Show environments", null, null, null, () => ShowPane(SidebarPane.Environments));
        yield return new PaletteItem("Show history", null, null, null, () => ShowPane(SidebarPane.History));
        yield return new PaletteItem("Toggle light / dark theme", null, null, null, () => _services.Settings.ToggleTheme());
        yield return new PaletteItem("Manage cookies", null, null, null, () => _ = OpenCookies());
        yield return new PaletteItem("Settings", null, null, "Ctrl ,", () => _ = OpenSettings());
    }

    private WorkspaceService Workspace => _services.Workspace;
    private DialogService Dialogs => _services.Dialogs;

    public CollectionsPaneViewModel Collections { get; }
    public EnvironmentsPaneViewModel Environments { get; }
    public HistoryPaneViewModel History { get; }

    public ObservableCollection<TabViewModelBase> Tabs { get; } = [];

    [ObservableProperty] public partial TabViewModelBase? ActiveTab { get; set; }
    [ObservableProperty] public partial SidebarPane SelectedPane { get; set; }
    [ObservableProperty] public partial bool IsSidebarVisible { get; set; } = true;

    public bool HasTabs => Tabs.Count > 0;
    public bool IsCollectionsPane => SelectedPane == SidebarPane.Collections;
    public bool IsEnvironmentsPane => SelectedPane == SidebarPane.Environments;
    public bool IsHistoryPane => SelectedPane == SidebarPane.History;

    partial void OnSelectedPaneChanged(SidebarPane value)
    {
        OnPropertyChanged(nameof(IsCollectionsPane));
        OnPropertyChanged(nameof(IsEnvironmentsPane));
        OnPropertyChanged(nameof(IsHistoryPane));
    }

    [RelayCommand]
    private void ShowPane(SidebarPane pane)
    {
        SelectedPane = pane;
        IsSidebarVisible = true;
    }

    /// <summary>Folder holding the workspace's plain JSON files (shown in the status bar).</summary>
    public string DataDirectory => Workspace.DataDirectory;

    /// <summary>True when there's no usable keychain, so secrets are written to the data files as plain text.</summary>
    public bool AreSecretsUnprotected => !Workspace.Secrets.IsProtected;

    public string SecretsWarning =>
        $"Secret values are saved as plain text in the data folder. {Workspace.Secrets.UnprotectedReason}";

    public string AppVersion { get; } = "Hermes " + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(2) ?? "");

    private void NotifyWorkspaceChanged()
    {
        foreach (var tab in Tabs)
            tab.OnWorkspaceChanged();
        UpdateTitlePrefixes();
    }

    // ---- environment selector ----

    public ObservableCollection<EnvironmentOption> EnvironmentOptions { get; } = [];

    [ObservableProperty] public partial EnvironmentOption? SelectedEnvironment { get; set; }

    public QuickLookSection QuickLookEnvironment { get; private set; } = new([]);
    public QuickLookSection QuickLookGlobals { get; private set; } = new([]);
    public string QuickLookEnvironmentTitle => Workspace.ActiveEnvironment?.Name ?? "No environment selected";
    public bool HasActiveEnvironment => Workspace.ActiveEnvironment is not null;
    public bool ActiveEnvironmentConfirms => Workspace.ActiveEnvironment?.ConfirmBeforeSend == true;

    partial void OnSelectedEnvironmentChanged(EnvironmentOption? value)
    {
        if (!_syncingEnvironment)
            Workspace.ActiveEnvironment = value?.Model;
    }

    private void RefreshEnvironmentOptions()
    {
        _syncingEnvironment = true;
        try
        {
            EnvironmentOptions.Clear();
            EnvironmentOptions.Add(new EnvironmentOption("No Environment", null));
            foreach (var env in Workspace.Environments)
                EnvironmentOptions.Add(new EnvironmentOption(env.Name, env));
            SelectedEnvironment = EnvironmentOptions.FirstOrDefault(o => o.Model == Workspace.ActiveEnvironment) ?? EnvironmentOptions[0];
        }
        finally
        {
            _syncingEnvironment = false;
        }

        static QuickLookSection Rows(EnvironmentModel? env) => new(
            env?.Variables.Where(v => v.Enabled && v.Key.Length > 0)
                .Select(v => new QuickLookVariable(v.Key, Mask(v, v.Value), Mask(v, v.EffectiveValue)))
                .ToList() ?? []);

        static string Mask(Variable v, string value) => v.Type == VariableType.Secret && value.Length > 0 ? "••••••" : value;

        QuickLookEnvironment = Rows(Workspace.ActiveEnvironment);
        QuickLookGlobals = Rows(Workspace.Globals);
        OnPropertyChanged(nameof(QuickLookEnvironment));
        OnPropertyChanged(nameof(QuickLookGlobals));
        OnPropertyChanged(nameof(QuickLookEnvironmentTitle));
        OnPropertyChanged(nameof(HasActiveEnvironment));
        OnPropertyChanged(nameof(ActiveEnvironmentConfirms));
    }

    [RelayCommand]
    private void EditActiveEnvironment()
    {
        if (Workspace.ActiveEnvironment is { } env)
            OpenEnvironment(env);
    }

    [RelayCommand]
    private void EditGlobals() => OpenEnvironment(Workspace.Globals);

    // ---- ITabHost ----

    void ITabHost.OpenRequest(HttpRequestModel model) => OpenRequest(model);

    public void OpenCollection(CollectionModel collection) =>
        OpenOrFocus(collection.Id, () => new CollectionTabViewModel(_services, collection));

    public void OpenFolder(FolderModel folder) =>
        OpenOrFocus(folder.Id, () => new FolderTabViewModel(_services, folder));

    [RelayCommand]
    public void OpenEnvironmentMatrix() =>
        OpenOrFocus(EnvironmentMatrixTabViewModel.MatrixId, () => new EnvironmentMatrixTabViewModel(_services));

    public void OpenEnvironment(EnvironmentModel environment) =>
        OpenOrFocus(environment.Id, () => new EnvironmentTabViewModel(_services, environment));

    public void OpenDraft(HttpRequestModel model) =>
        AddTab(new RequestTabViewModel(_services, model, ModelCloner.Serialize(HttpRequestModel.CreateNew())));

    public void CloseTabsFor(IEnumerable<Guid> entityIds)
    {
        var ids = entityIds.ToHashSet();
        foreach (var tab in Tabs.Where(t => ids.Contains(t.EntityId)).ToList())
            RemoveTab(tab);
    }

    public void OnRequestRenamed(Guid requestId, string name)
    {
        if (Tabs.OfType<RequestTabViewModel>().FirstOrDefault(t => t.RequestId == requestId) is { } tab)
            tab.ApplyExternalRename(name);
    }

    partial void OnActiveTabChanged(TabViewModelBase? oldValue, TabViewModelBase? newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        if (newValue is not null)
        {
            newValue.IsActive = true;
            if (newValue is not EnvironmentTabViewModel)
                Collections.Reveal(newValue.EntityId);
        }
    }

    // ---- tabs ----

    [RelayCommand]
    public void NewRequest() => AddTab(new RequestTabViewModel(_services, HttpRequestModel.CreateNew()));

    public void AddTab(TabViewModelBase tab)
    {
        // Single-tab mode: the new tab takes the place of a clean current one.
        if (!ShowTabs && ActiveTab is { IsDirty: false, IsPinned: false } replaced)
        {
            Tabs[Tabs.IndexOf(replaced)] = tab;
            ActiveTab = tab;
            return;
        }
        var index = ActiveTab is null ? Tabs.Count : Tabs.IndexOf(ActiveTab) + 1;
        Tabs.Insert(index, tab);
        ActiveTab = tab;
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

    public void OpenRequest(HttpRequestModel model)
    {
        OpenOrFocus(model.Id, () => new RequestTabViewModel(_services, ModelCloner.Clone(model)));
        if (ActiveTab is not null)
            ActiveTab.IsPreview = false;
    }

    public void PreviewRequest(HttpRequestModel model)
    {
        if (Tabs.FirstOrDefault(t => t.EntityId == model.Id) is { } open)
        {
            ActiveTab = open;
            return;
        }
        var tab = new RequestTabViewModel(_services, ModelCloner.Clone(model)) { IsPreview = true };
        if (Tabs.FirstOrDefault(t => t is { IsPreview: true, IsDirty: false }) is { } previous)
        {
            Tabs[Tabs.IndexOf(previous)] = tab;
            ActiveTab = tab;
        }
        else
        {
            AddTab(tab);
        }
    }

    [RelayCommand]
    private void KeepTab(TabViewModelBase tab) => tab.IsPreview = false;

    [RelayCommand]
    private void TogglePin(TabViewModelBase tab)
    {
        tab.IsPinned = !tab.IsPinned;
        // Pinned tabs sit together at the start of the strip.
        var target = tab.IsPinned ? Tabs.Count(t => t.IsPinned && t != tab) : Tabs.Count(t => t.IsPinned);
        Tabs.Move(Tabs.IndexOf(tab), target);
    }

    // ---- tab strip ----

    public bool ShowTabs => _services.Settings.Current.ShowTabs;

    /// <summary>With tabs turned off the strip still appears while more than one tab (unsaved work) is open.</summary>
    public bool IsTabStripVisible => ShowTabs || Tabs.Count > 1;

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModelBase.Title))
            UpdateTitlePrefixes();
    }

    private void OnTabsChanged()
    {
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(IsTabStripVisible));
        UpdateTitlePrefixes();
    }

    /// <summary>Same-named tabs show their collection so they can be told apart.</summary>
    private void UpdateTitlePrefixes()
    {
        var duplicates = Tabs.GroupBy(t => t.Title).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
        foreach (var tab in Tabs)
            tab.TitlePrefix = duplicates.Contains(tab) ? tab.CollectionName : null;
    }

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
        foreach (var other in Tabs.Where(t => t != tab && !t.IsPinned).ToList())
            await CloseTab(other);
    }

    [RelayCommand]
    private async Task CloseAllTabs()
    {
        foreach (var tab in Tabs.Where(t => !t.IsPinned).ToList())
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

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    // ---- dialogs ----

    [RelayCommand]
    public async Task OpenImport(IReadOnlyList<string>? files = null)
    {
        var vm = new ImportDialogViewModel(Dialogs);
        if (files is not null)
            vm.AddFiles(files);
        var result = await Dialogs.ShowDialogAsync<Core.Import.ImportResult>(new Views.Dialogs.ImportDialog { DataContext = vm });
        if (result is not null)
            await ApplyImportAsync(result);
    }

    /// <summary>Adds imported collections/environments to the workspace and opens imported cURL requests.</summary>
    public async Task ApplyImportAsync(Core.Import.ImportResult result)
    {
        foreach (var collection in result.Collections)
            Workspace.AddCollection(collection);
        foreach (var environment in result.Environments)
            Workspace.AddEnvironment(environment);
        foreach (var request in result.Requests)
            OpenDraft(request);

        if (result.Collections.Count > 0)
        {
            SelectedPane = SidebarPane.Collections;
            IsSidebarVisible = true;
            if (Collections.Find(result.Collections[^1].Id) is { } node)
            {
                node.IsExpanded = true;
                Collections.SelectedNode = node;
            }
        }
        else if (result.Environments.Count > 0)
        {
            SelectedPane = SidebarPane.Environments;
            IsSidebarVisible = true;
        }

        if (result.Errors.Count > 0)
            await Dialogs.ShowMessageAsync("Some items couldn't be imported", string.Join("\n", result.Errors));
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        var vm = new SettingsDialogViewModel(_services.Settings, Dialogs, _services.Http, Workspace.DataDirectory);
        await Dialogs.ShowDialogAsync<bool>(new Views.Dialogs.SettingsDialog { DataContext = vm });
        foreach (var tab in Tabs)
            tab.OnVariablesChanged();
    }

    [RelayCommand]
    private async Task RevealDataDirectory() => await Dialogs.RevealFolderAsync(DataDirectory);

    [RelayCommand]
    private async Task OpenCookies() =>
        await Dialogs.ShowDialogAsync<object>(new Views.Dialogs.CookieManagerDialog { DataContext = new CookieManagerViewModel(_services.Http) });

    // ---- session ----

    [ObservableProperty] public partial double SidebarWidth { get; set; } = 300;

    /// <summary>Persists open tabs (including unsaved drafts), the active environment and layout.</summary>
    public void SaveSession()
    {
        try
        {
            Workspace.SaveSession(new SessionState
            {
                Tabs = Tabs.Select(t => t.ToSession()).ToList(),
                ActiveTabIndex = ActiveTab is null ? 0 : Tabs.IndexOf(ActiveTab),
                ActiveEnvironmentId = Workspace.ActiveEnvironment?.Id,
                SidebarWidth = SidebarWidth,
            });
        }
        catch (IOException)
        {
            // Losing the session is not worth crashing over.
        }
    }

    private void RestoreSession()
    {
        var session = Workspace.LoadSession();
        if (session.ActiveEnvironmentId is { } envId && Workspace.FindEnvironment(envId) is { } env && env != Workspace.Globals)
            Workspace.ActiveEnvironment = env;
        SidebarWidth = Math.Clamp(session.SidebarWidth, 200, 600);

        foreach (var saved in session.Tabs)
        {
            var tab = RestoreTab(saved);
            if (tab is not null)
            {
                tab.IsPinned = saved.IsPinned;
                Tabs.Add(tab);
            }
        }

        if (Tabs.Count == 0)
            NewRequest();
        else
            ActiveTab = Tabs[Math.Clamp(session.ActiveTabIndex, 0, Tabs.Count - 1)];
        OnPropertyChanged(nameof(HasTabs));
    }

    private TabViewModelBase? RestoreTab(SessionTab saved)
    {
        var id = saved.EntityId ?? Guid.Empty;
        switch (saved.Kind)
        {
            case SessionTabKind.Request:
                if (Workspace.Find(id)?.Node is HttpRequestModel persisted)
                {
                    // Load the draft (if any) but compare against what's saved, so the tab shows as modified.
                    return saved.RequestDraft is { } draft
                        ? new RequestTabViewModel(_services, draft, SnapshotOf(persisted))
                        : new RequestTabViewModel(_services, ModelCloner.Clone(persisted));
                }
                return saved.RequestDraft is { } orphan
                    ? new RequestTabViewModel(_services, orphan, ModelCloner.Serialize(HttpRequestModel.CreateNew()))
                    : null;

            case SessionTabKind.Collection:
                return Workspace.FindCollection(id) is { } collection ? new CollectionTabViewModel(_services, collection) : null;
            case SessionTabKind.Folder:
                return Workspace.Find(id)?.Node is FolderModel folder ? new FolderTabViewModel(_services, folder) : null;
            case SessionTabKind.Environment:
                return Workspace.FindEnvironment(id) is { } environment ? new EnvironmentTabViewModel(_services, environment) : null;
            case SessionTabKind.EnvironmentMatrix:
                return new EnvironmentMatrixTabViewModel(_services);
            default:
                return null;
        }
    }

    /// <summary>The snapshot a clean tab would have for <paramref name="model"/> (normalised through the view model).</summary>
    private string SnapshotOf(HttpRequestModel model) =>
        ModelCloner.Serialize(new RequestTabViewModel(_services, ModelCloner.Clone(model)).BuildModel());
}
