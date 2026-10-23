using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

public class TabTests
{
    private static WorkspaceService Workspace => App.Services.GetRequiredService<WorkspaceService>();

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
    public void Request_settings_keep_an_override_that_matches_the_app_setting()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, _) = CreateRequests("Settings");
        var appVerifies = App.Services.GetRequiredService<SettingsService>().Current.VerifySsl;
        first.Settings.VerifySsl = appVerifies;

        vm.OpenRequest(first);
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
    public void Cancel_in_an_unsaved_changes_prompt_stops_close_all()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Cancel");
        foreach (var request in new[] { first, second })
        {
            vm.OpenRequest(request);
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
    public void Single_click_replaces_the_preview_tab_until_it_is_edited()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Preview");
        var before = vm.Tabs.Count;

        vm.PreviewRequest(first);
        var preview = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.True(preview.IsPreview);

        vm.PreviewRequest(second);
        Assert.Equal(before + 1, vm.Tabs.Count);
        Assert.DoesNotContain(preview, vm.Tabs);
        var edited = Assert.IsType<RequestTabViewModel>(vm.ActiveTab);
        Assert.Equal(second.Id, edited.RequestId);

        edited.Url = "https://example.com/changed";
        Assert.False(edited.IsPreview);

        vm.PreviewRequest(first);
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
    public void Same_named_tabs_show_their_collection()
    {
        var (window, vm) = ShowEmptyWindow();
        var shared = "Same " + Guid.NewGuid();
        var (a, firstA, _) = CreateRequests("Alpha", shared);
        var (b, firstB, _) = CreateRequests("Beta", shared);

        vm.OpenRequest(firstA);
        var tabA = vm.ActiveTab!;
        Assert.Null(tabA.TitlePrefix);

        vm.OpenRequest(firstB);
        Assert.Equal(a.Name, tabA.TitlePrefix);
        Assert.Equal(b.Name, vm.ActiveTab!.TitlePrefix);

        vm.RemoveTab(vm.ActiveTab);
        Assert.Null(tabA.TitlePrefix);
        window.Close();
    }

    [AvaloniaFact]
    public void Pinned_tabs_survive_close_all_and_move_to_the_start()
    {
        var (window, vm) = ShowEmptyWindow();
        var (_, first, second) = CreateRequests("Pin");

        vm.OpenRequest(second);
        vm.OpenRequest(first);
        var pinned = vm.ActiveTab!;
        vm.TogglePinCommand.Execute(pinned);
        Assert.True(pinned.IsPinned);
        Assert.Same(pinned, vm.Tabs[0]);

        vm.CloseAllTabsCommand.Execute(null);
        UiTestHelpers.Pump();
        Assert.Same(pinned, Assert.Single(vm.Tabs));
        window.Close();
    }
}
