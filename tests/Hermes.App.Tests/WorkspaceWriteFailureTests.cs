using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels.Sidebar;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

/// <summary>
/// Writes that fail because the data folder is read-only: nothing escapes, the problem is reported, and what the
/// workspace holds in memory is still what is on disk.
/// </summary>
public class WorkspaceWriteFailureTests
{
    [AvaloniaFact]
    public async Task Saving_a_collection_into_a_read_only_folder_reports_it_and_leaves_the_file_alone()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var file = temp.Repository.CollectionFile(collection.Id);
        var before = File.ReadAllText(file);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var changes = 0;
        temp.Workspace.CollectionChanged += _ => changes++;

        collection.Description = "edited";
        var saved = await temp.Workspace.SaveCollectionAsync(collection);

        Assert.False(saved);
        var problem = Assert.Single(temp.Problems);
        Assert.Equal(StorageErrorKind.PermissionDenied, problem.Kind);
        Assert.StartsWith("Collection \"Pets\" couldn't be saved: Hermes isn't allowed to write to it.", problem.Summary);
        Assert.Equal(file, problem.Location);
        Assert.Equal(0, changes);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [AvaloniaFact]
    public async Task The_try_variant_returns_the_error_without_reporting_it()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var error = await temp.Workspace.TrySaveCollectionAsync(collection);

        Assert.NotNull(error);
        Assert.Equal(StorageErrorKind.PermissionDenied, error.Kind);
        Assert.Empty(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_collection_that_cannot_be_written_is_not_added()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var listChanges = 0;
        temp.Workspace.CollectionsListChanged += () => listChanges++;
        var collection = new CollectionModel { Name = "Pets" };

        var added = await temp.Workspace.AddCollectionAsync(collection);

        Assert.False(added);
        Assert.DoesNotContain(collection, temp.Workspace.Collections);
        Assert.Equal(0, listChanges);
        Assert.False(File.Exists(temp.Repository.CollectionFile(collection.Id)));
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_collection_cannot_be_created_when_the_data_folder_itself_is_read_only()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var added = await temp.Workspace.AddCollectionAsync(new CollectionModel { Name = "Pets" });

        Assert.False(added);
        Assert.Empty(temp.Workspace.Collections);
        var problem = Assert.Single(temp.Problems);
        Assert.Equal(StorageErrorKind.PermissionDenied, problem.Kind);
    }

    [AvaloniaFact]
    public async Task Saving_and_adding_environments_into_a_read_only_folder_report_and_change_nothing()
    {
        using var temp = new TempWorkspace();
        var existing = await temp.Workspace.CreateEnvironmentAsync("Staging");
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var changes = 0;
        temp.Workspace.EnvironmentsChanged += () => changes++;

        existing.Variables.Add(new Variable("host", "example.com"));
        var saved = await temp.Workspace.SaveEnvironmentAsync(existing);
        var added = await temp.Workspace.AddEnvironmentAsync(new EnvironmentModel { Name = "Production" });

        Assert.False(saved);
        Assert.False(added);
        Assert.Equal([existing], temp.Workspace.Environments);
        Assert.Equal(0, changes);
        Assert.Equal(2, temp.Problems.Count);
        Assert.StartsWith("Environment \"Staging\" couldn't be saved", temp.Problems[0].Summary);
        Assert.StartsWith("Environment \"Production\" couldn't be saved", temp.Problems[1].Summary);
        Assert.DoesNotContain("example.com", File.ReadAllText(temp.Repository.EnvironmentFile(existing.Id)));
    }

    [AvaloniaFact]
    public async Task Global_variables_that_cannot_be_written_are_reported_by_name()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var saved = await temp.Workspace.SaveEnvironmentAsync(temp.Workspace.Globals);

        Assert.False(saved);
        Assert.StartsWith("Global variables couldn't be saved", Assert.Single(temp.Problems).Summary);
    }

    [AvaloniaFact]
    public async Task A_request_that_cannot_be_saved_is_taken_out_of_the_collection_again()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var first = new HttpRequestModel { Name = "First" };
        await temp.Workspace.AddNodeAsync(collection, null, first);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var added = await temp.Workspace.AddNodeAsync(collection, null, new HttpRequestModel { Name = "Second" });

        Assert.False(added);
        Assert.Equal([first], collection.Items);
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_node_that_cannot_be_deleted_stays_in_its_place()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var first = new HttpRequestModel { Name = "First" };
        var second = new HttpRequestModel { Name = "Second" };
        await temp.Workspace.AddNodeAsync(collection, null, first);
        await temp.Workspace.AddNodeAsync(collection, null, second);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var deleted = await temp.Workspace.DeleteNodeAsync(first.Id);

        Assert.False(deleted);
        Assert.Equal([first, second], collection.Items);
        Assert.NotNull(temp.Workspace.Find(first.Id));
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_move_that_cannot_be_saved_puts_the_node_back()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var folder = new FolderModel { Name = "Dogs" };
        var first = new HttpRequestModel { Name = "First" };
        var second = new HttpRequestModel { Name = "Second" };
        await temp.Workspace.AddNodeAsync(collection, null, folder);
        await temp.Workspace.AddNodeAsync(collection, null, first);
        await temp.Workspace.AddNodeAsync(collection, null, second);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var moved = await temp.Workspace.MoveNodeAsync(first.Id, collection, folder, 0);

        Assert.False(moved);
        Assert.Equal([folder, first, second], collection.Items);
        Assert.Empty(folder.Items);
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_collection_that_cannot_be_deleted_stays_in_the_list()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var file = temp.Repository.CollectionFile(collection.Id);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var deleted = await temp.Workspace.DeleteCollectionAsync(collection);

        Assert.False(deleted);
        Assert.Contains(collection, temp.Workspace.Collections);
        Assert.True(File.Exists(file));
        var problem = Assert.Single(temp.Problems);
        Assert.StartsWith("Collection \"Pets\" couldn't be deleted: Hermes isn't allowed to delete it.", problem.Summary);
    }

    [AvaloniaFact]
    public async Task An_environment_that_cannot_be_deleted_stays_in_the_list_and_stays_active()
    {
        using var temp = new TempWorkspace();
        var environment = await temp.Workspace.CreateEnvironmentAsync("Staging");
        temp.Workspace.ActiveEnvironment = environment;
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var deleted = await temp.Workspace.DeleteEnvironmentAsync(environment);

        Assert.False(deleted);
        Assert.Contains(environment, temp.Workspace.Environments);
        Assert.Same(environment, temp.Workspace.ActiveEnvironment);
        Assert.StartsWith("Environment \"Staging\" couldn't be deleted", Assert.Single(temp.Problems).Summary);
    }

    [AvaloniaFact]
    public async Task A_rename_that_cannot_be_saved_keeps_the_old_name_in_the_sidebar_and_on_disk()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var request = new HttpRequestModel { Name = "List" };
        await temp.Workspace.AddNodeAsync(collection, null, request);
        var folder = new FolderModel { Name = "Cats" };
        await temp.Workspace.AddNodeAsync(collection, null, folder);
        var host = new NullTabHost();
        var pane = new CollectionsPaneViewModel(temp.Workspace, new Hermes.App.Services.DialogService(), host);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        await pane.CommitRenameAsync(pane.Find(collection.Id)!, "Animals");
        await pane.CommitRenameAsync(pane.Find(request.Id)!, "Fetch");
        await pane.CommitRenameAsync(pane.Find(folder.Id)!, "Dogs");

        Assert.Equal("Pets", collection.Name);
        Assert.Equal("List", request.Name);
        Assert.Equal("Pets", pane.Find(collection.Id)!.Name);
        Assert.Equal("List", pane.Find(request.Id)!.Name);
        Assert.Empty(host.RenamedRequests);
        Assert.Equal(3, temp.Problems.Count);
        Assert.StartsWith("Collection \"Pets\" couldn't be renamed to \"Animals\": ", temp.Problems[0].Summary);
        Assert.StartsWith("Request \"List\" in collection \"Pets\" couldn't be renamed to \"Fetch\": ", temp.Problems[1].Summary);
        Assert.StartsWith("Folder \"Cats\" in collection \"Pets\" couldn't be renamed to \"Dogs\": ", temp.Problems[2].Summary);
        Assert.Equal("Cats", folder.Name);
        Assert.Contains("\"name\":\"Pets\"", File.ReadAllText(temp.Repository.CollectionFile(collection.Id)).Replace(" ", ""));
    }

    [AvaloniaFact]
    public async Task A_rename_through_the_rename_command_that_cannot_be_saved_keeps_the_old_name()
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var pane = new CollectionsPaneViewModel(temp.Workspace, new Hermes.App.Services.DialogService(), new NullTabHost());
        var node = pane.Find(collection.Id)!;
        node.IsRenaming = true;
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        await node.CommitRenameCommand.ExecuteAsync("Animals");

        Assert.False(node.IsRenaming);
        Assert.Equal("Pets", collection.Name);
        Assert.Equal("Pets", node.Name);
        Assert.StartsWith("Collection \"Pets\" couldn't be renamed to \"Animals\": ", Assert.Single(temp.Problems).Summary);
    }

    [AvaloniaFact]
    public async Task An_environment_rename_through_the_rename_command_is_saved()
    {
        using var temp = new TempWorkspace();
        var environment = await temp.Workspace.CreateEnvironmentAsync("Staging");
        var pane = new EnvironmentsPaneViewModel(temp.Workspace, new Hermes.App.Services.DialogService(), new NullTabHost());
        var item = pane.Items.Single(candidate => candidate.Model == environment);
        item.IsRenaming = true;

        await item.CommitRenameCommand.ExecuteAsync("Production");

        Assert.False(item.IsRenaming);
        Assert.Equal("Production", environment.Name);
        Assert.Empty(temp.Problems);
    }

    [AvaloniaFact]
    public async Task An_environment_rename_that_cannot_be_saved_keeps_the_old_name()
    {
        using var temp = new TempWorkspace();
        var environment = await temp.Workspace.CreateEnvironmentAsync("Staging");
        var pane = new EnvironmentsPaneViewModel(temp.Workspace, new Hermes.App.Services.DialogService(), new NullTabHost());
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        await pane.CommitRenameAsync(pane.Items.Single(item => item.Model == environment), "Production");

        Assert.Equal("Staging", environment.Name);
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_session_that_cannot_be_saved_is_reported_once_until_a_save_works_again()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        await temp.Workspace.SaveSessionAsync(new SessionState());
        await temp.Workspace.SaveSessionAsync(new SessionState());
        await temp.Workspace.SaveSessionAsync(new SessionState());

        var problem = Assert.Single(temp.Problems);
        Assert.StartsWith("Session (open tabs and drafts) couldn't be saved", problem.Summary);

        locked.Allow();
        await temp.Workspace.SaveSessionAsync(new SessionState());
        Assert.Single(temp.Problems);
        Assert.True(File.Exists(Path.Combine(temp.Root, "session.json")));

        temp.Lock();
        await temp.Workspace.SaveSessionAsync(new SessionState());
        Assert.Equal(2, temp.Problems.Count);
    }
}
