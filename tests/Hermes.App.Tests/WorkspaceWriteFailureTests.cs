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
    public void Saving_a_collection_into_a_read_only_folder_reports_it_and_leaves_the_file_alone()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var file = temp.Repository.CollectionFile(collection.Id);
        var before = File.ReadAllText(file);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var changes = 0;
        temp.Workspace.CollectionChanged += _ => changes++;

        collection.Description = "edited";
        var saved = temp.Workspace.SaveCollection(collection);

        Assert.False(saved);
        var problem = Assert.Single(temp.Problems);
        Assert.Equal(StorageErrorKind.PermissionDenied, problem.Kind);
        Assert.StartsWith("Collection \"Pets\" couldn't be saved: Hermes isn't allowed to write to it.", problem.Summary);
        Assert.Equal(file, problem.Location);
        Assert.Equal(0, changes);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [AvaloniaFact]
    public void The_try_variant_returns_the_error_without_reporting_it()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var error = temp.Workspace.TrySaveCollection(collection);

        Assert.NotNull(error);
        Assert.Equal(StorageErrorKind.PermissionDenied, error.Kind);
        Assert.Empty(temp.Problems);
    }

    [AvaloniaFact]
    public void A_collection_that_cannot_be_written_is_not_added()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var listChanges = 0;
        temp.Workspace.CollectionsListChanged += () => listChanges++;
        var collection = new CollectionModel { Name = "Pets" };

        var added = temp.Workspace.AddCollection(collection);

        Assert.False(added);
        Assert.DoesNotContain(collection, temp.Workspace.Collections);
        Assert.Equal(0, listChanges);
        Assert.False(File.Exists(temp.Repository.CollectionFile(collection.Id)));
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public void A_collection_cannot_be_created_when_the_data_folder_itself_is_read_only()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var added = temp.Workspace.AddCollection(new CollectionModel { Name = "Pets" });

        Assert.False(added);
        Assert.Empty(temp.Workspace.Collections);
        var problem = Assert.Single(temp.Problems);
        Assert.Equal(StorageErrorKind.PermissionDenied, problem.Kind);
    }

    [AvaloniaFact]
    public void Saving_and_adding_environments_into_a_read_only_folder_report_and_change_nothing()
    {
        using var temp = new TempWorkspace();
        var existing = temp.Workspace.CreateEnvironment("Staging");
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);
        var changes = 0;
        temp.Workspace.EnvironmentsChanged += () => changes++;

        existing.Variables.Add(new Variable("host", "example.com"));
        var saved = temp.Workspace.SaveEnvironment(existing);
        var added = temp.Workspace.AddEnvironment(new EnvironmentModel { Name = "Production" });

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
    public void Global_variables_that_cannot_be_written_are_reported_by_name()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var saved = temp.Workspace.SaveEnvironment(temp.Workspace.Globals);

        Assert.False(saved);
        Assert.StartsWith("Global variables couldn't be saved", Assert.Single(temp.Problems).Summary);
    }

    [AvaloniaFact]
    public void A_request_that_cannot_be_saved_is_taken_out_of_the_collection_again()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var first = new HttpRequestModel { Name = "First" };
        temp.Workspace.AddNode(collection, null, first);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var added = temp.Workspace.AddNode(collection, null, new HttpRequestModel { Name = "Second" });

        Assert.False(added);
        Assert.Equal([first], collection.Items);
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public void A_node_that_cannot_be_deleted_stays_in_its_place()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var first = new HttpRequestModel { Name = "First" };
        var second = new HttpRequestModel { Name = "Second" };
        temp.Workspace.AddNode(collection, null, first);
        temp.Workspace.AddNode(collection, null, second);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var deleted = temp.Workspace.DeleteNode(first.Id);

        Assert.False(deleted);
        Assert.Equal([first, second], collection.Items);
        Assert.NotNull(temp.Workspace.Find(first.Id));
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public void A_move_that_cannot_be_saved_puts_the_node_back()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var folder = new FolderModel { Name = "Dogs" };
        var first = new HttpRequestModel { Name = "First" };
        var second = new HttpRequestModel { Name = "Second" };
        temp.Workspace.AddNode(collection, null, folder);
        temp.Workspace.AddNode(collection, null, first);
        temp.Workspace.AddNode(collection, null, second);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var moved = temp.Workspace.MoveNode(first.Id, collection, folder, 0);

        Assert.False(moved);
        Assert.Equal([folder, first, second], collection.Items);
        Assert.Empty(folder.Items);
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public void A_collection_that_cannot_be_deleted_stays_in_the_list()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var file = temp.Repository.CollectionFile(collection.Id);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var deleted = temp.Workspace.DeleteCollection(collection);

        Assert.False(deleted);
        Assert.Contains(collection, temp.Workspace.Collections);
        Assert.True(File.Exists(file));
        var problem = Assert.Single(temp.Problems);
        Assert.StartsWith("Collection \"Pets\" couldn't be deleted: Hermes isn't allowed to delete it.", problem.Summary);
    }

    [AvaloniaFact]
    public void An_environment_that_cannot_be_deleted_stays_in_the_list_and_stays_active()
    {
        using var temp = new TempWorkspace();
        var environment = temp.Workspace.CreateEnvironment("Staging");
        temp.Workspace.ActiveEnvironment = environment;
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        var deleted = temp.Workspace.DeleteEnvironment(environment);

        Assert.False(deleted);
        Assert.Contains(environment, temp.Workspace.Environments);
        Assert.Same(environment, temp.Workspace.ActiveEnvironment);
        Assert.StartsWith("Environment \"Staging\" couldn't be deleted", Assert.Single(temp.Problems).Summary);
    }

    [AvaloniaFact]
    public void A_rename_that_cannot_be_saved_keeps_the_old_name_in_the_sidebar_and_on_disk()
    {
        using var temp = new TempWorkspace();
        var collection = temp.Workspace.CreateCollection("Pets");
        var request = new HttpRequestModel { Name = "List" };
        temp.Workspace.AddNode(collection, null, request);
        var host = new NullTabHost();
        var pane = new CollectionsPaneViewModel(temp.Workspace, new Hermes.App.Services.DialogService(), host);
        using var locked = temp.Lock("collections");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        pane.CommitRename(pane.Find(collection.Id)!, "Animals");
        pane.CommitRename(pane.Find(request.Id)!, "Fetch");

        Assert.Equal("Pets", collection.Name);
        Assert.Equal("List", request.Name);
        Assert.Equal("Pets", pane.Find(collection.Id)!.Name);
        Assert.Equal("List", pane.Find(request.Id)!.Name);
        Assert.Empty(host.RenamedRequests);
        Assert.Equal(2, temp.Problems.Count);
        Assert.Contains("\"name\":\"Pets\"", File.ReadAllText(temp.Repository.CollectionFile(collection.Id)).Replace(" ", ""));
    }

    [AvaloniaFact]
    public void An_environment_rename_that_cannot_be_saved_keeps_the_old_name()
    {
        using var temp = new TempWorkspace();
        var environment = temp.Workspace.CreateEnvironment("Staging");
        var pane = new EnvironmentsPaneViewModel(temp.Workspace, new Hermes.App.Services.DialogService(), new NullTabHost());
        using var locked = temp.Lock("environments");
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        pane.CommitRename(pane.Items.Single(item => item.Model == environment), "Production");

        Assert.Equal("Staging", environment.Name);
        Assert.Single(temp.Problems);
    }

    [AvaloniaFact]
    public void A_session_that_cannot_be_saved_is_reported_once_until_a_save_works_again()
    {
        using var temp = new TempWorkspace();
        using var locked = temp.Lock();
        Assert.SkipUnless(locked.IsEffective, TempWorkspace.NotEnforced);

        temp.Workspace.SaveSession(new SessionState());
        temp.Workspace.SaveSession(new SessionState());
        temp.Workspace.SaveSession(new SessionState());

        var problem = Assert.Single(temp.Problems);
        Assert.StartsWith("Session (open tabs and drafts) couldn't be saved", problem.Summary);

        locked.Allow();
        temp.Workspace.SaveSession(new SessionState());
        Assert.Single(temp.Problems);
        Assert.True(File.Exists(Path.Combine(temp.Root, "session.json")));

        temp.Lock();
        temp.Workspace.SaveSession(new SessionState());
        Assert.Equal(2, temp.Problems.Count);
    }
}
