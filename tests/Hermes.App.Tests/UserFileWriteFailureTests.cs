using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.App.ViewModels.Sidebar;
using Hermes.App.Views.Dialogs;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

/// <summary>Saving a response and exporting to a file the user picked: a location that can't be written is reported, not thrown.</summary>
public class UserFileWriteFailureTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly Window _window = new() { Width = 800, Height = 600 };
    private readonly DialogService _dialogs;

    public UserFileWriteFailureTests()
    {
        _window.Show();
        _dialogs = new DialogService { Owner = _window };
    }

    private string UnwritableFile
    {
        get
        {
            var locked = _temp.Lock("exports");
            Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
            return Path.Combine(_temp.Root, "exports", "out.json");
        }
    }

    private StorageErrorDialog? Dialog => _window.OwnedWindows.OfType<StorageErrorDialog>().SingleOrDefault();

    private IEnumerable<string?> DialogTexts => Dialog!.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text);

    private IEnumerable<object?> DialogButtons => Dialog!.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).Select(b => b.Content);

    private async Task<bool> RunWithDialogClosedAsync(Task<bool> write)
    {
        await UiTestHelpers.WaitUntil(() => Dialog is not null);
        Dialog!.Close();
        await UiTestHelpers.WaitUntil(() => write.IsCompleted);
        return await write;
    }

    [AvaloniaFact]
    public async Task A_response_that_cannot_be_written_is_reported_with_the_file_named()
    {
        var response = new ResponseViewModel(_dialogs, _temp.Settings);
        response.SetResponse(new ResponseModel { StatusCode = 200, Body = "hello"u8.ToArray(), ContentType = "text/plain" });
        var path = UnwritableFile;

        var write = response.WriteToFileAsync(path);
        await UiTestHelpers.WaitUntil(() => Dialog is not null);

        Assert.Contains("File \"out.json\" couldn't be saved: Hermes isn't allowed to write to it.", DialogTexts);
        Assert.Contains("Reveal folder", DialogButtons);
        Assert.False(await RunWithDialogClosedAsync(write));
        Assert.False(File.Exists(path));
    }

    [AvaloniaFact]
    public async Task A_collection_export_that_cannot_be_written_is_reported()
    {
        var collection = _temp.Workspace.CreateCollection("Pets");
        var pane = new CollectionsPaneViewModel(_temp.Workspace, _dialogs, new NullTabHost());
        var path = UnwritableFile;

        var write = pane.WriteExportAsync(collection, path);
        await UiTestHelpers.WaitUntil(() => Dialog is not null);

        Assert.Equal("Couldn't export the collection", Dialog!.Title);
        Assert.Contains("File \"out.json\" couldn't be saved: Hermes isn't allowed to write to it.", DialogTexts);
        Assert.False(await RunWithDialogClosedAsync(write));
    }

    [AvaloniaFact]
    public async Task An_environment_export_that_cannot_be_written_is_reported()
    {
        var environment = _temp.Workspace.CreateEnvironment("Staging");
        var pane = new EnvironmentsPaneViewModel(_temp.Workspace, _dialogs, new NullTabHost());
        var path = UnwritableFile;

        var write = pane.WriteExportAsync(environment, isGlobals: false, path);
        await UiTestHelpers.WaitUntil(() => Dialog is not null);

        Assert.Equal("Couldn't export the environment", Dialog!.Title);
        Assert.False(await RunWithDialogClosedAsync(write));
    }

    [AvaloniaFact]
    public async Task A_writable_location_is_written_without_a_dialog()
    {
        var collection = _temp.Workspace.CreateCollection("Pets");
        var pane = new CollectionsPaneViewModel(_temp.Workspace, _dialogs, new NullTabHost());
        var path = Path.Combine(_temp.Root, "pets.postman_collection.json");

        var written = await pane.WriteExportAsync(collection, path);

        Assert.True(written);
        Assert.Contains("Pets", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Null(Dialog);
    }

    public void Dispose()
    {
        _window.Close();
        _temp.Dispose();
    }
}
