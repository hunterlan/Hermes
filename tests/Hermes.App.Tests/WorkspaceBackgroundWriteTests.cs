using Avalonia.Headless.XUnit;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>
/// Workspace writes run off the UI thread, in the order they were asked for, from a copy taken when they were asked
/// for; the caller still waits for the result, and nothing queued is lost when the workspace is disposed. The keychain
/// write is held at a gate, the way a macOS permission prompt holds it.
/// </summary>
public class WorkspaceBackgroundWriteTests
{
    private static readonly TimeSpan WriteStartTimeout = TimeSpan.FromSeconds(10);

    // A secret variable makes each save write the secret store, where the gate is.
    private static Variable Secret(string value) => new("token", value) { Type = VariableType.Secret };

    private static async Task<CollectionModel> CreateCollectionWithSecretAsync(TempWorkspace temp)
    {
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        collection.Variables.Add(Secret("first"));
        return collection;
    }

    [AvaloniaFact]
    public async Task A_save_runs_off_the_calling_thread_while_the_caller_waits_for_its_result()
    {
        var secrets = new GatedSecretStore();
        using var temp = new TempWorkspace(secrets);
        var collection = await CreateCollectionWithSecretAsync(temp);
        secrets.Gate.Reset();
        var uiThread = Environment.CurrentManagedThreadId;

        var saving = temp.Workspace.SaveCollectionAsync(collection);

        // The call came back while the keychain write is still held at the gate.
        Assert.True(await secrets.WriteStarted.WaitAsync(WriteStartTimeout));
        Assert.False(saving.IsCompleted);
        secrets.Gate.Set();
        Assert.True(await saving);
        Assert.NotEqual(uiThread, Assert.Single(secrets.WriteThreads));
        Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
        Assert.Empty(temp.Problems);
    }

    [AvaloniaFact]
    public async Task A_delete_asked_for_after_a_save_runs_after_it()
    {
        var secrets = new GatedSecretStore();
        using var temp = new TempWorkspace(secrets);
        var collection = await CreateCollectionWithSecretAsync(temp);
        secrets.Gate.Reset();

        var saving = temp.Workspace.SaveCollectionAsync(collection);
        var deleting = temp.Workspace.DeleteCollectionAsync(collection);
        secrets.Gate.Set();

        Assert.True(await saving);
        Assert.True(await deleting);
        // Had the delete run first, the save would have written the file back.
        Assert.False(File.Exists(temp.Repository.CollectionFile(collection.Id)));
        Assert.DoesNotContain(collection, temp.Workspace.Collections);
        Assert.Empty(secrets.Get($"collection/{collection.Id}"));
    }

    [AvaloniaFact]
    public async Task An_edit_made_while_a_save_waits_is_not_in_that_save()
    {
        var secrets = new GatedSecretStore();
        using var temp = new TempWorkspace(secrets);
        var collection = await CreateCollectionWithSecretAsync(temp);
        collection.Description = "as saved";
        secrets.Gate.Reset();

        var saving = temp.Workspace.SaveCollectionAsync(collection);
        collection.Description = "edited afterwards";
        collection.Items.Add(new HttpRequestModel { Name = "Added afterwards" });
        secrets.Gate.Set();

        Assert.True(await saving);
        var onDisk = File.ReadAllText(temp.Repository.CollectionFile(collection.Id));
        Assert.Contains("as saved", onDisk);
        Assert.DoesNotContain("edited afterwards", onDisk);
        Assert.DoesNotContain("Added afterwards", onDisk);
    }

    [AvaloniaFact]
    public async Task Disposing_the_workspace_waits_for_the_writes_still_queued()
    {
        var secrets = new GatedSecretStore();
        using var temp = new TempWorkspace(secrets);
        var collection = await CreateCollectionWithSecretAsync(temp);
        collection.Description = "kept on quit";
        secrets.Gate.Reset();
        var saving = temp.Workspace.SaveCollectionAsync(collection);
        Assert.True(await secrets.WriteStarted.WaitAsync(WriteStartTimeout));

        // Opened from elsewhere, as the keychain would let go while the app is exiting.
        var opening = Task.Run(secrets.Gate.Set);
        temp.Workspace.Dispose();

        Assert.Single(secrets.WriteThreads);
        Assert.Contains("kept on quit", File.ReadAllText(temp.Repository.CollectionFile(collection.Id)));
        await opening;
        Assert.True(await saving);
    }
}
