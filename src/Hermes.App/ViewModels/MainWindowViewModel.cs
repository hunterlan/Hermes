using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.App.ViewModels.Sidebar;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, ITabHost
{
    private readonly TabServices _services;
    private bool _syncingEnvironment;

    /// <summary>The last History draft opened and its content then; with tabs off an untouched one is replaced without asking.</summary>
    private (RequestTabViewModel Tab, string OpenedContent)? _historyDraft;

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
            foreach (var tab in Tabs)
                tab.OnSettingsChanged();
            Collections.OnShowTabsChanged();
            History.OnShowTabsChanged();
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
            yield return new PaletteItem(request.Name, $"{path} · {request.Url}", request.Method, null, () => OpenRequestCommand.Execute(request));

        yield return new PaletteItem("New request", null, null, "Ctrl T", () => NewRequestCommand.Execute(null));
        yield return new PaletteItem("Import…", "Postman collection or environment, cURL", null, "Ctrl O", () => _ = OpenImport());
        yield return new PaletteItem("Open environments matrix", null, null, null, () => OpenEnvironmentMatrixCommand.Execute(null));
        foreach (var option in EnvironmentOptions)
            yield return new PaletteItem($"Use environment: {option.Name}", null, null, null, () => SelectedEnvironment = option);
        yield return new PaletteItem("Toggle sidebar", null, null, "Ctrl \\", () => IsSidebarVisible = !IsSidebarVisible);
        yield return new PaletteItem("Show collections", null, null, null, () => ShowPane(SidebarPane.Collections));
        yield return new PaletteItem("Show environments", null, null, null, () => ShowPane(SidebarPane.Environments));
        yield return new PaletteItem("Show history", null, null, null, () => ShowPane(SidebarPane.History));
        yield return new PaletteItem("Toggle light / dark theme", null, null, null, () => _services.Settings.ToggleTheme());
        yield return new PaletteItem("Manage cookies", null, null, null, () => _ = OpenCookies());
        yield return new PaletteItem("Settings", null, null, "Ctrl ,", () => _ = OpenSettings());
        yield return new PaletteItem("Open manual", null, null, "F1", () => OpenManualCommand.Execute(null));
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

    /// <summary>Rows each quick-look section shows before pointing to the matrix.</summary>
    public const int QuickLookLimit = 5;

    public QuickLookSection QuickLookEnvironment { get; private set; } = new([], 0);
    public QuickLookSection QuickLookGlobals { get; private set; } = new([], 0);
    public string QuickLookEnvironmentTitle => $"{Workspace.ActiveEnvironment?.Name} · {QuickLookEnvironment.Total} {(QuickLookEnvironment.Total == 1 ? "variable" : "variables")}";
    public string QuickLookGlobalsTitle => $"Globals · {QuickLookGlobals.Total}";
    public string EnvironmentButtonText => Workspace.ActiveEnvironment?.Name ?? "None";
    public bool HasActiveEnvironment => Workspace.ActiveEnvironment is not null;
    public bool ActiveEnvironmentConfirms => Workspace.ActiveEnvironment?.ConfirmBeforeSend == true;
    public EnvironmentColor ActiveEnvironmentColor => Workspace.ActiveEnvironment?.Color ?? EnvironmentColor.None;

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
            var active = Workspace.ActiveEnvironment;
            EnvironmentOptions.Add(new EnvironmentOption("No environment", null, active is null));
            foreach (var env in Workspace.Environments)
                EnvironmentOptions.Add(new EnvironmentOption(env.Name, env, env == active));
            SelectedEnvironment = EnvironmentOptions.FirstOrDefault(o => o.IsActive) ?? EnvironmentOptions[0];
        }
        finally
        {
            _syncingEnvironment = false;
        }

        static List<Variable> Visible(EnvironmentModel? env) => env?.Variables.Where(v => v.Enabled && v.Key.Length > 0).ToList() ?? [];

        static QuickLookSection Section(List<Variable> variables, Func<Variable, bool> isOverridden) => new(
            variables.Take(QuickLookLimit).Select(v => new QuickLookVariable(v.Key, Mask(v), isOverridden(v))).ToList(),
            variables.Count);

        static string Mask(Variable v) =>
            v.Type == VariableType.Secret && v.EffectiveValue.Length > 0 ? VariableScope.MaskedValue : v.EffectiveValue;

        var environmentVariables = Visible(Workspace.ActiveEnvironment);
        var environmentKeys = environmentVariables.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        QuickLookEnvironment = Section(environmentVariables, _ => false);
        QuickLookGlobals = Section(Visible(Workspace.Globals), v => environmentKeys.Contains(v.Key));
        OnPropertyChanged(nameof(QuickLookEnvironment));
        OnPropertyChanged(nameof(QuickLookGlobals));
        OnPropertyChanged(nameof(QuickLookEnvironmentTitle));
        OnPropertyChanged(nameof(QuickLookGlobalsTitle));
        OnPropertyChanged(nameof(EnvironmentButtonText));
        OnPropertyChanged(nameof(HasActiveEnvironment));
        OnPropertyChanged(nameof(ActiveEnvironmentConfirms));
        OnPropertyChanged(nameof(ActiveEnvironmentColor));
    }

    [RelayCommand]
    private async Task EditActiveEnvironment()
    {
        if (Workspace.ActiveEnvironment is { } env)
            await OpenEnvironmentAsync(env);
    }

    [RelayCommand]
    private Task EditGlobals() => OpenEnvironmentAsync(Workspace.Globals);

    // ---- ITabHost ----

    public Task OpenCollectionAsync(CollectionModel collection) =>
        OpenOrFocusAsync(collection.Id, () => new CollectionTabViewModel(_services, collection));

    public Task OpenFolderAsync(FolderModel folder) =>
        OpenOrFocusAsync(folder.Id, () => new FolderTabViewModel(_services, folder));

    [RelayCommand]
    public Task OpenEnvironmentMatrixAsync() =>
        OpenOrFocusAsync(EnvironmentMatrixTabViewModel.MatrixId, () => new EnvironmentMatrixTabViewModel(_services));

    /// <summary>Opens the manual, at <paramref name="fileName"/> when given.</summary>
    [RelayCommand]
    public async Task OpenManualAsync(string? fileName = null)
    {
        if (await OpenOrFocusAsync(HelpTabViewModel.HelpId, () => new HelpTabViewModel(_services))
            && fileName is not null && ActiveTab is HelpTabViewModel help)
            help.Navigate(fileName);
    }

    public Task OpenEnvironmentAsync(EnvironmentModel environment) =>
        OpenOrFocusAsync(environment.Id, () => new EnvironmentTabViewModel(_services, environment));

    public async Task OpenHistoryDraftAsync(HttpRequestModel model)
    {
        RequestTabViewModel? draft = null;
        if (await OpenNewTabAsync(() => draft = DraftTab(model)) && draft is not null)
            _historyDraft = (draft, ContentOf(draft));
    }

    /// <summary>A tab for an unsaved request, compared against a blank one so it shows as modified.</summary>
    private RequestTabViewModel DraftTab(HttpRequestModel model) =>
        new(_services, model, ModelCloner.Serialize(HttpRequestModel.CreateNew()));

    private static string ContentOf(RequestTabViewModel tab) => ModelCloner.Serialize(tab.BuildModel());

    /// <summary>
    /// A History draft the user hasn't edited shows as modified but holds nothing to lose (History still has it),
    /// so single-tab mode replaces it without asking.
    /// </summary>
    private bool IsUntouchedHistoryDraft(TabViewModelBase tab) =>
        _historyDraft is { } history && history.Tab == tab && ContentOf(history.Tab) == history.OpenedContent;

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
    public Task NewRequest() => OpenNewTabAsync(() => new RequestTabViewModel(_services, HttpRequestModel.CreateNew()));

    /// <summary>
    /// Opens a tab next to the active one. With tabs off it replaces the active tab instead, pinned or not, once
    /// the user has saved or discarded its changes. False when the user cancelled or the save didn't happen.
    /// </summary>
    /// <param name="create">Called only once the tab will open, so nothing is built for a cancelled open.</param>
    private async Task<bool> OpenNewTabAsync(Func<TabViewModelBase> create)
    {
        if (ShowTabs || ActiveTab is not { } current)
        {
            InsertTab(create());
            return true;
        }
        if (!IsUntouchedHistoryDraft(current) && !await ConfirmDiscardAsync(current))
            return false;
        var tab = create();
        var index = Tabs.IndexOf(current);
        if (index < 0)
        {
            // The tab went away while the prompt was up (e.g. its entity was deleted).
            InsertTab(tab);
            return true;
        }
        Tabs[index] = tab;
        ActiveTab = tab;
        return true;
    }

    private void InsertTab(TabViewModelBase tab)
    {
        var index = ActiveTab is null ? Tabs.Count : Tabs.IndexOf(ActiveTab) + 1;
        Tabs.Insert(index, tab);
        ActiveTab = tab;
    }

    /// <summary>Focuses an open tab for the entity, or opens a new one. False when the user cancelled.</summary>
    private async Task<bool> OpenOrFocusAsync(Guid entityId, Func<TabViewModelBase> create)
    {
        if (Tabs.FirstOrDefault(t => t.EntityId == entityId) is { } open)
        {
            ActiveTab = open;
            return true;
        }
        return await OpenNewTabAsync(create);
    }

    [RelayCommand]
    public async Task OpenRequestAsync(HttpRequestModel model)
    {
        var opened = await OpenOrFocusAsync(model.Id, () => new RequestTabViewModel(_services, ModelCloner.Clone(model)));
        if (opened && ActiveTab is not null)
            ActiveTab.IsPreview = false;
    }

    public async Task PreviewRequestAsync(HttpRequestModel model)
    {
        if (Tabs.FirstOrDefault(t => t.EntityId == model.Id) is { } open)
        {
            ActiveTab = open;
            return;
        }
        RequestTabViewModel CreatePreview() => new(_services, ModelCloner.Clone(model)) { IsPreview = true };
        if (Tabs.FirstOrDefault(t => t is { IsPreview: true, IsDirty: false }) is { } previous)
        {
            var tab = CreatePreview();
            Tabs[Tabs.IndexOf(previous)] = tab;
            ActiveTab = tab;
        }
        else
        {
            await OpenNewTabAsync(CreatePreview);
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

    /// <summary>With tabs turned off there's only ever one tab, so the strip is hidden.</summary>
    public bool IsTabStripVisible => ShowTabs;

    /// <summary>Pinned tabs are only kept apart while tabs are on; in single-tab mode they're like any other tab.</summary>
    private bool IsKeptByPin(TabViewModelBase tab) => ShowTabs && tab.IsPinned;

    /// <summary>Tabs single-tab mode doesn't keep: every tab but the active one, pinned or not.</summary>
    private IEnumerable<TabViewModelBase> TabsNotKeptInSingleTabMode => Tabs.Where(t => t != ActiveTab);

    /// <summary>
    /// Run before Tabs is turned off: once the user confirms, closes every tab but the active one, asking to save
    /// each with unsaved changes. False when the user cancelled; tabs closed by then stay closed.
    /// </summary>
    public async Task<bool> CloseTabsForTurningTabsOffAsync()
    {
        var closing = TabsNotKeptInSingleTabMode.ToList();
        if (closing.Count == 0)
            return true;
        var confirmed = await Dialogs.ConfirmAsync("Turn off tabs?",
            $"{closing.Count} {(closing.Count == 1 ? "tab" : "tabs")} will be closed: every tab except the current one, " +
            "including pinned tabs. You'll be asked to save any with unsaved changes.",
            "Close tabs");
        if (!confirmed)
            return false;
        foreach (var tab in closing)
        {
            if (!await TryCloseTabAsync(tab))
                return false;
        }
        return true;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModelBase.Title))
            UpdateTitlePrefixes();
    }

    private void OnTabsChanged()
    {
        OnPropertyChanged(nameof(HasTabs));
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
        if (tab is not null)
            await TryCloseTabAsync(tab);
    }

    /// <summary>Closes the tab after asking about unsaved changes. False when the user cancelled.</summary>
    private async Task<bool> TryCloseTabAsync(TabViewModelBase tab)
    {
        if (!await ConfirmDiscardAsync(tab))
            return false;
        RemoveTab(tab);
        return true;
    }

    /// <summary>Asks to save a dirty tab. False when the user cancelled or the save didn't happen.</summary>
    private async Task<bool> ConfirmDiscardAsync(TabViewModelBase tab)
    {
        if (!tab.IsDirty)
            return true;
        return await Dialogs.AskSaveChangesAsync(tab.Title) switch
        {
            SaveChangesResult.Cancel => false,
            SaveChangesResult.Save => await tab.SaveAsync(),
            _ => true,
        };
    }

    /// <summary>Tabs whose edits quitting would lose: dirty, and the session can't keep a draft of them.</summary>
    private IEnumerable<TabViewModelBase> TabsLosingEditsOnQuit => Tabs.Where(t => t.IsDirty && !t.KeepsDraftInSession);

    public bool HasUnsavedEdits => TabsLosingEditsOnQuit.Any();

    /// <summary>Asks to save each tab that would lose edits on quitting. False when the user cancelled.</summary>
    public async Task<bool> ConfirmQuitAsync()
    {
        foreach (var tab in TabsLosingEditsOnQuit.ToList())
        {
            ActiveTab = tab;
            if (!await ConfirmDiscardAsync(tab))
                return false;
        }
        return true;
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
        foreach (var other in Tabs.Where(t => t != tab && !IsKeptByPin(t)).ToList())
            if (!await TryCloseTabAsync(other))
                return;
    }

    [RelayCommand]
    private async Task CloseAllTabs()
    {
        foreach (var tab in Tabs.Where(t => !IsKeptByPin(t)).ToList())
            if (!await TryCloseTabAsync(tab))
                return;
    }

    [RelayCommand]
    private async Task DuplicateTab(TabViewModelBase tab)
    {
        if (tab is not RequestTabViewModel request)
            return;
        var copy = request.BuildModel();
        copy.Id = Guid.NewGuid();
        copy.Name += " Copy";
        await OpenNewTabAsync(() => DraftTab(copy));
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

    /// <summary>Sends the active request, or cancels it while it's in flight.</summary>
    [RelayCommand]
    private void SendActive() => (ActiveTab as RequestTabViewModel)?.SendOrCancel();

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
        {
            // With tabs off each draft replaces the last; cancelling a save prompt stops opening the rest.
            if (!await OpenNewTabAsync(() => DraftTab(request)))
                break;
        }

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
        var vm = new SettingsDialogViewModel(_services.Settings, Dialogs, _services.Http, Workspace.DataDirectory, CloseTabsForTurningTabsOffAsync);
        await Dialogs.ShowDialogAsync<bool>(new Views.Dialogs.SettingsDialog { DataContext = vm });
        if (vm.ManualRequested)
            await OpenManualAsync();
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

        // ActiveTabIndex counts the saved tabs, some of which may not come back (their entity was deleted).
        var restored = new List<TabViewModelBase>();
        TabViewModelBase? active = null;
        foreach (var (saved, index) in session.Tabs.Select((t, i) => (t, i)))
        {
            var tab = RestoreTab(saved);
            if (tab is not null)
            {
                tab.IsPinned = saved.IsPinned;
                restored.Add(tab);
                if (index <= session.ActiveTabIndex || active is null)
                    active = tab;
            }
        }

        // With tabs turned off only the active tab and tabs with unsaved changes come back.
        foreach (var tab in restored.Where(t => ShowTabs || t == active || t.IsDirty))
            Tabs.Add(tab);

        if (active is null)
            InsertTab(new RequestTabViewModel(_services, HttpRequestModel.CreateNew()));
        else
            ActiveTab = active;
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
            case SessionTabKind.Help:
                return new HelpTabViewModel(_services);
            default:
                return null;
        }
    }

    /// <summary>The snapshot a clean tab would have for <paramref name="model"/> (normalised through the view model).</summary>
    private string SnapshotOf(HttpRequestModel model) =>
        ModelCloner.Serialize(new RequestTabViewModel(_services, ModelCloner.Clone(model)).BuildModel());
}
