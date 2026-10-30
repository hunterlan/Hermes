using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.ViewModels.Sidebar;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Http;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class TabTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();
    private static SettingsService Settings => App.Services.GetRequiredService<SettingsService>();

    /// <summary>The session is shared across tests, so start from an empty strip.</summary>
    private static (Views.MainWindow, MainWindowViewModel) ShowEmptyWindow()
    {
        var (window, vm) = UiTestHelpers.ShowMainWindow();
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        return (window, vm);
    }

    private static (CollectionModel Collection, HttpRequestModel First, HttpRequestModel Second) CreateRequests(string name, string requestName = "")
    {
        var collection = Workspace.CreateCollection(name + " " + Guid.NewGuid());
        var first = new HttpRequestModel { Name = requestName.Length > 0 ? requestName : "First " + Guid.NewGuid() };
        var second = new HttpRequestModel { Name = "Second " + Guid.NewGuid() };
        Workspace.AddNode(collection, null, first);
        Workspace.AddNode(collection, null, second);
        return (collection, first, second);
    }

    [AvaloniaFact]
    public async Task Request_settings_keep_an_override_that_matches_the_app_setting()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, _) = CreateRequests("Settings");
        var appVerifies = App.Services.GetRequiredService<SettingsService>().Current.VerifySsl;
        first.Settings.VerifySsl = appVerifies;

        await vm.OpenRequestAsync(first);
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Name += " edited";

        Assert.Equal(appVerifies, tab.BuildModel().Settings.VerifySsl);
        Assert.Null(tab.BuildModel().Settings.FollowRedirects);
        Assert.True(tab.HasSettingsOverrides);

        tab.UseDefaultSettingsCommand.Execute(null);
        Assert.Null(tab.BuildModel().Settings.VerifySsl);
        Assert.False(tab.HasSettingsOverrides);

        // Without an override the toggle follows the app setting as it changes.
        var settings = App.Services.GetRequiredService<SettingsService>();
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        settings.Update(settings.Current);
        Assert.Contains(nameof(RequestTabViewModel.VerifySsl), changed);
        window.Close();
    }

    /// <summary>Answers the dialog the window is showing, as if one of its buttons was clicked.</summary>
    private static void Answer(Avalonia.Controls.Window window, string result)
    {
        UiTestHelpers.Pump();
        Assert.Single(window.OwnedWindows).Close(result);
        UiTestHelpers.Pump();
    }

    [AvaloniaFact]
    public async Task Cancel_in_an_unsaved_changes_prompt_stops_close_all()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Cancel");
        foreach (var request in new[] { first, second })
        {
            await vm.OpenRequestAsync(request);
            ((RequestTabViewModel)vm.ActiveTab!).Name += " edited";
        }

        var closing = vm.CloseAllTabsCommand.ExecuteAsync(null);
        Answer(window, "cancel");

        Assert.True(closing.IsCompleted);
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(2, vm.Tabs.Count);
        foreach (var tab in vm.Tabs.ToList())
            vm.RemoveTab(tab);
        window.Close();
    }

    [AvaloniaFact]
    public void Quitting_asks_about_edits_the_session_cannot_keep()
    {
        var (window, vm) = ShowEmptyWindow();
        vm.OpenEnvironmentMatrixCommand.Execute(null);
        var matrix = Assert.IsType<EnvironmentMatrixTabViewModel>(vm.ActiveTab);
        matrix.AddVariableCommand.Execute(null);
        matrix.Rows[^1].Name = "quitVar" + Guid.NewGuid().ToString("N")[..8];
        Assert.True(vm.HasUnsavedEdits);

        window.Close();
        window.Close(); // again while the prompt is up: still one prompt
        Answer(window, "cancel");
        Assert.True(window.IsVisible);

        window.Close();
        Answer(window, "discard");
        Assert.False(window.IsVisible);
        vm.RemoveTab(matrix);
    }

    [AvaloniaFact]
    public void The_active_tab_is_restored_even_when_an_earlier_tab_is_gone()
    {
        var (window, _) = ShowEmptyWindow();
        window.Close();
        var environments = Enumerable.Range(0, 3).Select(i => Workspace.CreateEnvironment($"Restore {i} {Guid.NewGuid()}")).ToList();
        SessionTab EnvTab(EnvironmentModel env) => new() { Kind = SessionTabKind.Environment, EntityId = env.Id };
        Workspace.SaveSession(new SessionState
        {
            // The deleted request comes back as nothing, so the saved index 2 must still mean environments[1].
            Tabs = [EnvTab(environments[0]), new SessionTab { Kind = SessionTabKind.Request, EntityId = Guid.NewGuid() },
                EnvTab(environments[1]), EnvTab(environments[2])],
            ActiveTabIndex = 2,
        });

        var restored = new MainWindowViewModel(App.Services.GetRequiredService<TabServices>());

        Assert.Equal(environments[1].Id, restored.ActiveTab?.EntityId);
        foreach (var env in environments)
            Workspace.DeleteEnvironment(env);
    }

    [AvaloniaFact]
    public void A_collection_made_in_the_save_dialog_exists_only_once_saved_to()
    {
        var name = "Dialog " + Guid.NewGuid();
        var dialog = new SaveRequestDialogViewModel(Workspace, "Request");
        SaveRequestTarget? result = null;
        dialog.CloseRequested += target => result = target;

        dialog.StartNewCollectionCommand.Execute(null);
        dialog.NewCollectionName = name;
        dialog.CreateCollectionCommand.Execute(null);
        Assert.Equal(name, dialog.SelectedTarget?.Name);
        Assert.DoesNotContain(Workspace.Collections, c => c.Name == name);

        dialog.SaveCommand.Execute(null);
        Assert.True(result?.IsNewCollection);
        Assert.DoesNotContain(Workspace.Collections, c => c.Name == name);
    }

    [AvaloniaFact]
    public async Task Single_click_replaces_the_preview_tab_until_it_is_edited()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Preview");
        var before = vm.Tabs.Count;

        await vm.PreviewRequestAsync(first);
        var preview = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.True(preview.IsPreview);

        await vm.PreviewRequestAsync(second);
        Assert.Equal(before + 1, vm.Tabs.Count);
        Assert.DoesNotContain(preview, vm.Tabs);
        var edited = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.Equal(second.Id, edited.RequestId);

        edited.Url = "https://example.com/changed";
        Assert.False(edited.IsPreview);

        await vm.PreviewRequestAsync(first);
        Assert.Equal(before + 2, vm.Tabs.Count);
        Assert.Contains(edited, vm.Tabs);
        window.Close();
    }

    [AvaloniaFact]
    public void Command_palette_finds_requests_by_method_and_url()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, _) = CreateRequests("Palette");
        first.Method = "DELETE";
        first.Url = "https://api.test/palette-" + first.Id;

        Assert.False(vm.HasTabs);
        UiTestHelpers.Screenshot(window, "16-welcome");

        vm.OpenCommandPaletteCommand.Execute(null);
        Assert.True(vm.CommandPalette.IsOpen);
        vm.CommandPalette.Query = "delete palette-" + first.Id;
        var hit = Assert.Single(vm.CommandPalette.Results);
        UiTestHelpers.Screenshot(window, "15-command-palette");

        vm.CommandPalette.Run(hit);
        Assert.False(vm.CommandPalette.IsOpen);
        Assert.Equal(first.Id, Assert.IsType<RequestTabViewModel>(vm.ActiveTab).RequestId);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Same_named_tabs_show_their_collection()
    {
        var (window, vm) = ShowEmptyWindow();
        var shared = "Same " + Guid.NewGuid();
        var (a, firstA, _) = CreateRequests("Alpha", shared);
        var (b, firstB, _) = CreateRequests("Beta", shared);

        await vm.OpenRequestAsync(firstA);
        var tabA = vm.ActiveTab!;
        Assert.Null(tabA.TitlePrefix);

        await vm.OpenRequestAsync(firstB);
        Assert.Equal(a.Name, tabA.TitlePrefix);
        Assert.Equal(b.Name, vm.ActiveTab!.TitlePrefix);

        vm.RemoveTab(vm.ActiveTab);
        Assert.Null(tabA.TitlePrefix);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Pinned_tabs_survive_close_all_and_move_to_the_start()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Pin");

        await vm.OpenRequestAsync(second);
        await vm.OpenRequestAsync(first);
        var pinned = vm.ActiveTab!;
        vm.TogglePinCommand.Execute(pinned);
        Assert.True(pinned.IsPinned);
        Assert.Same(pinned, vm.Tabs[0]);

        vm.CloseAllTabsCommand.Execute(null);
        UiTestHelpers.Pump();
        Assert.Same(pinned, Assert.Single(vm.Tabs));
        window.Close();
    }

    // ---- single-tab mode (Settings → Tabs off) ----

    private static AppSettings CopyWithShowTabs(AppSettings settings, bool showTabs)
    {
        var json = JsonSerializer.Serialize(settings, HermesJsonContext.Default.AppSettings);
        var copy = JsonSerializer.Deserialize(json, HermesJsonContext.Default.AppSettings)!;
        copy.ShowTabs = showTabs;
        return copy;
    }

    /// <summary>Runs <paramref name="test"/> on an empty window with Tabs set to <paramref name="showTabs"/>; settings are shared, so they're restored after.</summary>
    private static void WithShowTabs(bool showTabs, Action<Views.MainWindow, MainWindowViewModel> test)
    {
        var original = Settings.Current;
        var (window, vm) = ShowEmptyWindow();
        try
        {
            Settings.Update(CopyWithShowTabs(original, showTabs));
            test(window, vm);
        }
        finally
        {
            // Leave no drafts in the shared session for other tests.
            foreach (var tab in vm.Tabs.ToList())
                vm.RemoveTab(tab);
            Settings.Update(original);
            window.Close();
        }
    }

    private static void WithTabsOff(Action<Views.MainWindow, MainWindowViewModel> test) => WithShowTabs(false, test);

    /// <summary>
    /// Single-tab tests stay synchronous (settings are shared while they run), so they check that an open which
    /// needs no prompt finished at once instead of awaiting it.
    /// </summary>
    private static void FinishesAtOnce(Task opening) => Assert.True(opening.IsCompletedSuccessfully);

    private static RequestTabViewModel EditActiveRequest(MainWindowViewModel vm)
    {
        var tab = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        tab.Url = "https://example.com/edited-" + Guid.NewGuid();
        Assert.True(tab.IsDirty);
        return tab;
    }

    /// <summary>A settings dialog with Tabs switched off, about to be saved.</summary>
    private static SettingsDialogViewModel SettingsTurningTabsOff(MainWindowViewModel vm) =>
        new(Settings, App.Services.GetRequiredService<DialogService>(), App.Services.GetRequiredService<HttpClientProvider>(),
            Workspace.DataDirectory, vm.CloseTabsForTurningTabsOffAsync)
        { ShowTabs = false };

    private static string MessageOf(Avalonia.Controls.Window window)
    {
        UiTestHelpers.Pump();
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(window.OwnedWindows));
        return string.Join("\n", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    }

    [AvaloniaFact]
    public void Without_tabs_a_session_restores_only_the_active_tab() => WithTabsOff((_, _) =>
    {
        var (_, first, second) = CreateRequests("Restore");
        var environment = Workspace.CreateEnvironment("Restore " + Guid.NewGuid());
        var draft = ModelCloner.Clone(first);
        draft.Url = "https://example.com/draft";
        Workspace.SaveSession(new SessionState
        {
            // The deleted request comes back as nothing, so the saved index 2 must still mean the draft.
            Tabs =
            [
                new SessionTab { Kind = SessionTabKind.Environment, EntityId = environment.Id, IsPinned = true },
                new SessionTab { Kind = SessionTabKind.Request, EntityId = Guid.NewGuid() },
                new SessionTab { Kind = SessionTabKind.Request, EntityId = first.Id, RequestDraft = draft },
                new SessionTab { Kind = SessionTabKind.Request, EntityId = second.Id },
            ],
            ActiveTabIndex = 2,
        });

        var restored = new MainWindowViewModel(App.Services.GetRequiredService<TabServices>());

        var only = Assert.IsType<RequestTabViewModel>(Assert.Single(restored.Tabs));
        Assert.Same(only, restored.ActiveTab);
        Assert.Equal(first.Id, only.RequestId);
        Assert.Equal(draft.Url, only.Url);
        Assert.True(only.IsDirty);
        Assert.False(restored.IsTabStripVisible);
        Workspace.DeleteEnvironment(environment);
    });

    private static MenuItem MenuItemNamed(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => (string?)item.Header == header);

    [AvaloniaFact]
    public void Open_in_tab_menu_items_are_hidden_while_tabs_are_off() => WithShowTabs(true, (_, vm) =>
    {
        var (_, first, _) = CreateRequests("Menu");
        var requestNode = vm.Collections.Find(first.Id)!;
        var historyItem = new HistoryItemViewModel(new HistoryEntry { Request = first });
        // Menus are built when opened; the ones kept here also show that items follow the setting while open.
        var requestMenu = Views.Sidebar.CollectionsPaneView.CreateMenu(vm.Collections, requestNode);
        var historyMenu = Views.Sidebar.HistoryPaneView.CreateMenu(vm.History, historyItem);
        bool[] Visibility() =>
        [
            MenuItemNamed(requestMenu, "Open in tab").IsVisible,
            MenuItemNamed(historyMenu, "Open in new tab").IsVisible,
        ];

        Assert.Equal([true, true], Visibility());

        Settings.Update(CopyWithShowTabs(Settings.Current, false));
        Assert.Equal([false, false], Visibility());
        Assert.False(MenuItemNamed(Views.Sidebar.CollectionsPaneView.CreateMenu(vm.Collections, requestNode), "Open in tab").IsVisible);
        Assert.False(MenuItemNamed(Views.Sidebar.HistoryPaneView.CreateMenu(vm.History, historyItem), "Open in new tab").IsVisible);

        Settings.Update(CopyWithShowTabs(Settings.Current, true));
        Assert.Equal([true, true], Visibility());
    });

    [AvaloniaFact]
    public void Turning_tabs_off_closes_clean_tabs_only_once_confirmed() => WithShowTabs(true, (window, vm) =>
    {
        var (collection, first, second) = CreateRequests("Turn off");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        FinishesAtOnce(vm.OpenCollectionAsync(collection));
        FinishesAtOnce(vm.OpenRequestAsync(second));
        var active = vm.ActiveTab!;
        var dialog = SettingsTurningTabsOff(vm);
        var dialogClosed = false;
        dialog.CloseRequested += _ => dialogClosed = true;

        var saving = dialog.SaveCommand.ExecuteAsync(null);
        Assert.Contains("2 tabs will be closed", MessageOf(window));
        Answer(window, "cancel");

        Assert.True(saving.IsCompleted);
        Assert.True(Settings.Current.ShowTabs);
        Assert.True(dialog.ShowTabs);
        Assert.False(dialogClosed);
        Assert.Equal(3, vm.Tabs.Count);

        dialog.ShowTabs = false;
        saving = dialog.SaveCommand.ExecuteAsync(null);
        Answer(window, "confirm");

        Assert.True(saving.IsCompleted);
        Assert.False(Settings.Current.ShowTabs);
        Assert.True(dialogClosed);
        Assert.Same(active, Assert.Single(vm.Tabs));
        Assert.False(vm.IsTabStripVisible);
    });

    [AvaloniaFact]
    public void Turning_tabs_off_closes_pinned_tabs_too() => WithShowTabs(true, (window, vm) =>
    {
        var (_, first, second) = CreateRequests("Turn off pinned");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        vm.TogglePinCommand.Execute(vm.ActiveTab);
        FinishesAtOnce(vm.OpenRequestAsync(second));
        var active = vm.ActiveTab!;

        var saving = SettingsTurningTabsOff(vm).SaveCommand.ExecuteAsync(null);
        Assert.Contains("1 tab will be closed", MessageOf(window));
        Answer(window, "confirm");

        Assert.True(saving.IsCompleted);
        Assert.Same(active, Assert.Single(vm.Tabs));
    });

    [AvaloniaFact]
    public void Turning_tabs_off_asks_to_save_each_unsaved_tab_and_stops_on_cancel() => WithShowTabs(true, (window, vm) =>
    {
        var (collection, first, second) = CreateRequests("Turn off dirty");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        var firstTab = EditActiveRequest(vm);
        FinishesAtOnce(vm.OpenRequestAsync(second));
        var secondTab = EditActiveRequest(vm);
        FinishesAtOnce(vm.OpenCollectionAsync(collection));
        var active = vm.ActiveTab!;
        var dialog = SettingsTurningTabsOff(vm);
        var dialogClosed = false;
        dialog.CloseRequested += _ => dialogClosed = true;

        // Confirm, don't save the first, then cancel at the second: Tabs stays on and the first stays closed.
        var saving = dialog.SaveCommand.ExecuteAsync(null);
        Assert.Contains("2 tabs will be closed", MessageOf(window));
        Answer(window, "confirm");
        Assert.Contains($"\"{firstTab.Title}\" has unsaved changes", MessageOf(window));
        Answer(window, "discard");
        Assert.Contains($"\"{secondTab.Title}\" has unsaved changes", MessageOf(window));
        Answer(window, "cancel");

        Assert.True(saving.IsCompleted);
        Assert.True(Settings.Current.ShowTabs);
        Assert.True(dialog.ShowTabs);
        Assert.False(dialogClosed);
        Assert.Equal([secondTab, active], vm.Tabs);

        // Again, saving the second: the setting is saved and only the active tab is left.
        dialog.ShowTabs = false;
        saving = dialog.SaveCommand.ExecuteAsync(null);
        Assert.Contains("1 tab will be closed", MessageOf(window));
        Answer(window, "confirm");
        var savedUrl = secondTab.Url;
        Answer(window, "save");

        Assert.True(saving.IsCompleted);
        Assert.False(Settings.Current.ShowTabs);
        Assert.True(dialogClosed);
        Assert.Same(active, Assert.Single(vm.Tabs));
        Assert.Equal(savedUrl, StoredUrl(second.Id));
        Assert.Equal(first.Url, StoredUrl(first.Id));
    });

    [AvaloniaFact]
    public void Turning_tabs_off_keeps_the_active_tab_even_when_unsaved() => WithShowTabs(true, (window, vm) =>
    {
        var (_, first, second) = CreateRequests("Turn off active dirty");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        FinishesAtOnce(vm.OpenRequestAsync(second));
        var active = EditActiveRequest(vm);

        var saving = SettingsTurningTabsOff(vm).SaveCommand.ExecuteAsync(null);
        Answer(window, "confirm");

        Assert.Empty(window.OwnedWindows);
        Assert.True(saving.IsCompleted);
        Assert.False(Settings.Current.ShowTabs);
        Assert.Same(active, Assert.Single(vm.Tabs));
        Assert.True(active.IsDirty);
    });

    [AvaloniaFact]
    public void Turning_tabs_off_with_one_tab_does_not_ask() => WithShowTabs(true, (window, vm) =>
    {
        var (_, first, _) = CreateRequests("Turn off single");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        var only = EditActiveRequest(vm);

        FinishesAtOnce(SettingsTurningTabsOff(vm).SaveCommand.ExecuteAsync(null));
        UiTestHelpers.Pump();

        Assert.Empty(window.OwnedWindows);
        Assert.False(Settings.Current.ShowTabs);
        Assert.Same(only, Assert.Single(vm.Tabs));
    });

    [AvaloniaFact]
    public void Without_tabs_opening_an_item_replaces_a_clean_tab_and_the_strip_stays_hidden() => WithTabsOff((_, vm) =>
    {
        var (collection, first, _) = CreateRequests("Single");
        var folder = new FolderModel { Name = "Folder " + Guid.NewGuid() };
        Workspace.AddNode(collection, null, folder);
        var environment = Workspace.CreateEnvironment("Single " + Guid.NewGuid());

        Func<Task>[] opens =
        [
            () => vm.OpenRequestAsync(first), () => vm.OpenFolderAsync(folder), () => vm.OpenCollectionAsync(collection),
            () => vm.OpenEnvironmentAsync(environment), () => vm.NewRequestCommand.ExecuteAsync(null),
        ];
        foreach (var open in opens)
        {
            FinishesAtOnce(open());
            Assert.Single(vm.Tabs);
            Assert.False(vm.IsTabStripVisible);
        }
        Workspace.DeleteEnvironment(environment);
    });

    [AvaloniaFact]
    public void Without_tabs_a_pinned_tab_is_replaced_like_any_other() => WithTabsOff((_, vm) =>
    {
        var (_, first, second) = CreateRequests("Pinned");

        FinishesAtOnce(vm.OpenRequestAsync(first));
        vm.TogglePinCommand.Execute(vm.ActiveTab);
        FinishesAtOnce(vm.OpenRequestAsync(second));

        Assert.Equal(second.Id, Assert.Single(vm.Tabs).EntityId);
    });

    private static string StoredUrl(Guid requestId) => ((HttpRequestModel)Workspace.Find(requestId)!.Node).Url;

    [AvaloniaFact]
    public void Without_tabs_opening_while_the_tab_is_unsaved_asks_to_save_it_first() => WithTabsOff((window, vm) =>
    {
        var (_, first, second) = CreateRequests("Prompt");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        var firstTab = EditActiveRequest(vm);

        // Cancel: nothing changes and the new request isn't opened.
        var opening = vm.NewRequestCommand.ExecuteAsync(null);
        Assert.Contains($"\"{firstTab.Title}\" has unsaved changes", MessageOf(window));
        Answer(window, "cancel");
        Assert.True(opening.IsCompleted);
        Assert.Same(firstTab, Assert.Single(vm.Tabs));
        Assert.True(firstTab.IsDirty);

        // Don't save: the edits are dropped and the item replaces the tab.
        opening = vm.OpenRequestAsync(second);
        Answer(window, "discard");
        Assert.True(opening.IsCompleted);
        Assert.Equal(second.Id, Assert.Single(vm.Tabs).EntityId);
        Assert.Equal(first.Url, StoredUrl(first.Id));

        // Save: the edits are saved, then the item (here from History) replaces the tab.
        var editedUrl = EditActiveRequest(vm).Url;
        opening = vm.History.OpenCommand.ExecuteAsync(new HistoryItemViewModel(new HistoryEntry { Request = first }));
        Answer(window, "save");
        Assert.True(opening.IsCompleted);
        Assert.Equal(editedUrl, StoredUrl(second.Id));
        Assert.False(Assert.IsType<RequestTabViewModel>(Assert.Single(vm.Tabs)).IsPersisted);
        Assert.False(vm.IsTabStripVisible);
    });

    [AvaloniaFact]
    public void Without_tabs_a_save_that_does_not_happen_keeps_the_tab() => WithTabsOff((window, vm) =>
    {
        var (_, first, _) = CreateRequests("Failed save");
        FinishesAtOnce(vm.NewRequestCommand.ExecuteAsync(null));
        var neverSaved = EditActiveRequest(vm);

        // Saving a new request asks where to put it; closing that dialog means nothing was saved.
        var opening = vm.OpenRequestAsync(first);
        Answer(window, "save");
        Assert.IsType<SaveRequestDialog>(Assert.Single(window.OwnedWindows)).Close();
        UiTestHelpers.Pump();

        Assert.True(opening.IsCompleted);
        Assert.Same(neverSaved, Assert.Single(vm.Tabs));
        Assert.True(neverSaved.IsDirty);
    });

    private static HistoryItemViewModel HistoryItem(string url) =>
        new(new HistoryEntry { Request = new HttpRequestModel { Url = url } });

    [AvaloniaFact]
    public void Without_tabs_browsing_history_asks_only_once_a_draft_is_edited() => WithTabsOff((window, vm) =>
    {
        var historyA = HistoryItem("https://example.com/history-a");
        var historyB = HistoryItem("https://example.com/history-b");

        // A History draft shows as modified from the start, yet the next entry replaces it without asking.
        FinishesAtOnce(vm.History.OpenCommand.ExecuteAsync(historyA));
        Assert.True(vm.ActiveTab!.IsDirty);
        FinishesAtOnce(vm.History.OpenCommand.ExecuteAsync(historyB));
        UiTestHelpers.Pump();
        Assert.Empty(window.OwnedWindows);
        Assert.Equal(historyB.Entry.Request.Url, Assert.IsType<RequestTabViewModel>(Assert.Single(vm.Tabs)).Url);

        var edited = EditActiveRequest(vm);
        var opening = vm.History.OpenCommand.ExecuteAsync(historyA);
        Answer(window, "cancel");
        Assert.True(opening.IsCompleted);
        Assert.Same(edited, Assert.Single(vm.Tabs));
    });

    [AvaloniaFact]
    public void Without_tabs_an_imported_curl_draft_still_asks() => WithTabsOff((window, vm) =>
    {
        var import = new ImportResult();
        ImportService.ImportText("curl https://example.com/imported", import);
        FinishesAtOnce(vm.ApplyImportAsync(import));
        var draft = Assert.Single(vm.Tabs);

        var opening = vm.NewRequestCommand.ExecuteAsync(null);
        Answer(window, "cancel");

        Assert.True(opening.IsCompleted);
        Assert.Same(draft, Assert.Single(vm.Tabs));
    });

    [AvaloniaFact]
    public void Without_tabs_opening_the_current_item_does_not_ask() => WithTabsOff((window, vm) =>
    {
        var (_, first, _) = CreateRequests("Current");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        var tab = EditActiveRequest(vm);

        FinishesAtOnce(vm.OpenRequestAsync(first));
        UiTestHelpers.Pump();

        Assert.Empty(window.OwnedWindows);
        Assert.Same(tab, Assert.Single(vm.Tabs));
    });

    [AvaloniaFact]
    public void With_tabs_an_item_opens_beside_an_unsaved_tab_without_asking() => WithShowTabs(true, (window, vm) =>
    {
        var (_, first, second) = CreateRequests("Tabs on");
        FinishesAtOnce(vm.OpenRequestAsync(first));
        var dirty = EditActiveRequest(vm);

        FinishesAtOnce(vm.OpenRequestAsync(second));
        UiTestHelpers.Pump();

        Assert.Empty(window.OwnedWindows);
        Assert.Equal([dirty.RequestId, second.Id], vm.Tabs.Select(t => t.EntityId));
        Assert.True(vm.IsTabStripVisible);
    });
}
