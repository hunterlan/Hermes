using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>Saving a request replaces its node or adds it where asked, and a write that is given up changes nothing.</summary>
public class RequestSaveServiceTests
{
    private sealed record Fixture(RequestSaveService Service, Window Window);

    private static Fixture Create(TempWorkspace temp)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        return new Fixture(new RequestSaveService(temp.Workspace, new DialogService { Owner = window }), window);
    }

    private static async Task<StorageErrorDialog> WaitForSaveDialog(Window owner)
    {
        await UiTestHelpers.WaitUntil(() => owner.OwnedWindows.OfType<StorageErrorDialog>().Any());
        return owner.OwnedWindows.OfType<StorageErrorDialog>().Single();
    }

    private static void Click(StorageErrorDialog dialog, string text)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsVisible && Equals(b.Content, text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UiTestHelpers.Pump();
    }

    private static string FileOf(TempWorkspace temp, CollectionModel collection) =>
        File.ReadAllText(temp.Repository.CollectionFile(collection.Id)).Replace(" ", "");

    [AvaloniaFact]
    public async Task An_existing_node_is_replaced_and_written()
    {
        using var temp = new TempWorkspace();
        var (service, window) = Create(temp);
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var original = new HttpRequestModel { Name = "List", Url = "https://old.example.com" };
        await temp.Workspace.AddNodeAsync(collection, null, original);
        var edited = new HttpRequestModel { Id = original.Id, Name = "List", Url = "https://new.example.com" };

        Assert.True(await service.SaveExistingAsync(temp.Workspace.Find(original.Id)!, edited));

        Assert.Same(edited, Assert.Single(collection.Items));
        Assert.Contains("https://new.example.com", FileOf(temp, collection));
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_new_request_is_saved_into_a_folder()
    {
        using var temp = new TempWorkspace();
        var (service, window) = Create(temp);
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var folder = new FolderModel { Name = "Cats" };
        await temp.Workspace.AddNodeAsync(collection, null, folder);
        var model = new HttpRequestModel { Name = "List", Url = "https://cats.example.com" };

        Assert.True(await service.SaveNewAsync(new SaveRequestTarget("List", collection, folder, false), model));

        Assert.Same(model, Assert.Single(folder.Items));
        Assert.Contains("https://cats.example.com", FileOf(temp, collection));
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_new_request_is_saved_into_a_new_collection()
    {
        using var temp = new TempWorkspace();
        var (service, window) = Create(temp);
        var collection = new CollectionModel { Name = "Fresh" };
        var model = new HttpRequestModel { Name = "List", Url = "https://fresh.example.com" };

        Assert.True(await service.SaveNewAsync(new SaveRequestTarget("List", collection, null, true), model));

        Assert.Contains(collection, temp.Workspace.Collections);
        Assert.Same(model, Assert.Single(collection.Items));
        Assert.Contains("https://fresh.example.com", FileOf(temp, collection));
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_given_up_write_puts_the_previous_node_back()
    {
        using var temp = new TempWorkspace();
        var (service, window) = Create(temp);
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var original = new HttpRequestModel { Name = "List", Url = "https://old.example.com" };
        await temp.Workspace.AddNodeAsync(collection, null, original);
        var edited = new HttpRequestModel { Id = original.Id, Name = "List", Url = "https://new.example.com" };
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var save = service.SaveExistingAsync(temp.Workspace.Find(original.Id)!, edited);
        (await WaitForSaveDialog(window)).Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.False(await save);
        Assert.Same(original, Assert.Single(collection.Items));
        Assert.Contains("https://old.example.com", FileOf(temp, collection));
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_given_up_write_leaves_no_new_collection_and_no_request()
    {
        using var temp = new TempWorkspace();
        var (service, window) = Create(temp);
        var collection = new CollectionModel { Name = "Fresh" };
        var model = new HttpRequestModel { Name = "List" };
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var save = service.SaveNewAsync(new SaveRequestTarget("List", collection, null, true), model);
        (await WaitForSaveDialog(window)).Close();
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.False(await save);
        Assert.Empty(collection.Items);
        Assert.DoesNotContain(collection, temp.Workspace.Collections);
        Assert.False(File.Exists(temp.Repository.CollectionFile(collection.Id)));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Retry_that_works_writes_once_more_and_returns_true()
    {
        using var temp = new TempWorkspace();
        var (service, window) = Create(temp);
        var collection = new CollectionModel { Name = "Fresh" };
        var model = new HttpRequestModel { Name = "List", Url = "https://fresh.example.com" };
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var save = service.SaveNewAsync(new SaveRequestTarget("List", collection, null, true), model);
        var dialog = await WaitForSaveDialog(window);
        locked.Allow();
        Click(dialog, "Retry");
        await UiTestHelpers.WaitUntil(() => save.IsCompleted);

        Assert.True(await save);
        Assert.Contains(collection, temp.Workspace.Collections);
        Assert.Same(model, Assert.Single(collection.Items));
        Assert.Contains("https://fresh.example.com", FileOf(temp, collection));
        window.Close();
    }
}
