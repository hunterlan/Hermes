using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.App.Tests;

/// <summary>A tab whose save fails stays dirty, says why, and writes after Retry once the problem is gone.</summary>
public class TabSaveFailureTests
{
    private static StorageErrorDialog? SaveDialogOf(Window owner) =>
        owner.OwnedWindows.OfType<StorageErrorDialog>().SingleOrDefault();

    private static async Task<StorageErrorDialog> WaitForSaveDialog(Window owner)
    {
        await UiTestHelpers.WaitUntil(() => SaveDialogOf(owner) is not null);
        return SaveDialogOf(owner)!;
    }

    private static void Click(StorageErrorDialog dialog, string text)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsVisible && Equals(b.Content, text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UiTestHelpers.Pump();
    }

    private sealed record TabServicesFixture(TabServices Services, Window Window);

    private static TabServicesFixture CreateServices(TempWorkspace temp)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        var dialogs = new DialogService { Owner = window };
        var services = new TabServices(temp.Workspace, App.Services.GetRequiredService<RequestExecutor>(),
            App.Services.GetRequiredService<OAuth2Service>(), dialogs, temp.Settings,
            App.Services.GetRequiredService<HttpClientProvider>(), App.Services.GetRequiredService<ManualService>(),
            TimeProvider.System);
        return new TabServicesFixture(services, window);
    }

    [AvaloniaFact]
    public async Task A_collection_tab_stays_dirty_when_the_save_is_given_up()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var (services, window) = CreateServices(temp);
        var tab = new CollectionTabViewModel(services, collection);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        tab.Name = "Animals";
        Assert.True(tab.IsDirty);

        var save = tab.SaveAsync();
        var dialog = await WaitForSaveDialog(window);
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Collection \"Animals\" couldn't be saved: Hermes isn't allowed to write to it.", texts);
        Assert.Contains(dialog.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Retry"));
        Assert.Contains(dialog.GetVisualDescendants().OfType<Button>(), b => b.IsVisible && Equals(b.Content, "Reveal file"));
        dialog.Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.False(await save);
        Assert.True(tab.IsDirty);
        Assert.Equal("Pets", collection.Name);
        Assert.Contains("\"name\":\"Pets\"", File.ReadAllText(temp.Repository.CollectionFile(collection.Id)).Replace(" ", ""));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Retry_saves_once_the_folder_is_writable_again()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var (services, window) = CreateServices(temp);
        var tab = new CollectionTabViewModel(services, collection);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        tab.Name = "Animals";

        var save = tab.SaveAsync();
        var dialog = await WaitForSaveDialog(window);
        Assert.True(tab.IsDirty);
        locked.Allow();
        Click(dialog, "Retry");
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.True(await save);
        Assert.False(tab.IsDirty);
        Assert.Equal("Animals", collection.Name);
        Assert.Contains("\"name\":\"Animals\"", File.ReadAllText(temp.Repository.CollectionFile(collection.Id)).Replace(" ", ""));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Retry_that_fails_again_shows_the_problem_again()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var (services, window) = CreateServices(temp);
        var tab = new CollectionTabViewModel(services, collection);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        tab.Name = "Animals";

        var save = tab.SaveAsync();
        var first = await WaitForSaveDialog(window);
        Click(first, "Retry");
        await UiTestHelpers.WaitUntil(() => SaveDialogOf(window) is { } next && next != first);

        Assert.False(save.IsCompleted);
        Assert.True(tab.IsDirty);
        SaveDialogOf(window)!.Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);
        Assert.False(await save);
        window.Close();
    }

    [AvaloniaFact]
    public async Task An_environment_tab_stays_dirty_and_the_environment_keeps_its_old_name()
    {
        using var temp = new TempWorkspace();
        var environment = temp.Workspace.CreateEnvironment("Staging");
        var (services, window) = CreateServices(temp);
        var tab = new EnvironmentTabViewModel(services, environment);
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        tab.Name = "Production";

        var save = tab.SaveAsync();
        (await WaitForSaveDialog(window)).Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.False(await save);
        Assert.True(tab.IsDirty);
        Assert.Equal("Staging", environment.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_request_tab_stays_dirty_and_the_collection_keeps_the_saved_request()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var request = new HttpRequestModel { Name = "List", Url = "https://old.example.com" };
        temp.Workspace.AddNode(collection, null, request);
        var (services, window) = CreateServices(temp);
        var tab = new RequestTabViewModel(services, request);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        tab.Url = "https://new.example.com";
        Assert.True(tab.IsDirty);

        var save = tab.SaveAsync();
        (await WaitForSaveDialog(window)).Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.False(await save);
        Assert.True(tab.IsDirty);
        Assert.Same(request, Assert.Single(collection.Items));
        Assert.Equal("https://old.example.com", request.Url);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_environment_matrix_restores_the_variables_it_could_not_write()
    {
        using var temp = new TempWorkspace();
        var environment = temp.Workspace.CreateEnvironment("Staging");
        environment.Variables.Add(new Variable("host", "old.example.com"));
        temp.Workspace.SaveEnvironment(environment);
        var (services, window) = CreateServices(temp);
        var tab = new EnvironmentMatrixTabViewModel(services);
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var cell = tab.Rows.Single(row => row.Name == "host").Cells.Single(c => c.Column.Model == environment);
        cell.Value = "new.example.com";
        Assert.True(tab.IsDirty);

        var save = tab.SaveAsync();
        (await WaitForSaveDialog(window)).Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.False(await save);
        Assert.True(tab.IsDirty);
        Assert.Equal("old.example.com", Assert.Single(environment.Variables).Value);
        window.Close();
    }
}
