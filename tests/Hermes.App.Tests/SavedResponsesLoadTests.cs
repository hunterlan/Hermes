using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Serialization;

namespace Hermes.App.Tests;

/// <summary>
/// A request tab reads its saved responses off the UI thread once it is shown, not while it is built: building a tab
/// for its snapshot or restoring one behind another touches no files.
/// </summary>
public class SavedResponsesLoadTests
{
    private static async Task<HttpRequestModel> SavedRequestWithResponsesAsync(TempWorkspace temp, params int[] statusCodes)
    {
        var collection = await temp.Workspace.CreateCollectionAsync("Pets");
        var request = new HttpRequestModel { Name = "List", Url = "https://example.com/pets" };
        await temp.Workspace.AddNodeAsync(collection, null, request);
        foreach (var statusCode in statusCodes)
            await temp.Workspace.SaveResponseAsync(request.Id, Saved(statusCode));
        return request;
    }

    private static SavedResponse Saved(int statusCode) => new() { Response = new ResponseModel { StatusCode = statusCode } };

    // An unreadable file is reported on every read of the folder, which makes the reads countable.
    private static void AddUnreadableResponse(TempWorkspace temp, Guid requestId) =>
        File.WriteAllText(Path.Combine(temp.Repository.ResponsesFolder(requestId), "0.json"), "{ not json");

    [AvaloniaFact]
    public async Task A_tab_shows_the_latest_saved_response_and_counts_the_earlier_ones_once_loaded()
    {
        using var temp = new TempWorkspace();
        var request = await SavedRequestWithResponsesAsync(temp, 200, 201, 202);
        using var tab = new RequestTabViewModel(RequestTabTestServices.Create(temp), ModelCloner.Clone(request));

        Assert.False(tab.Response.Saved.HasItems);
        Assert.True(tab.Response.IsEmpty);

        await tab.LoadSavedResponsesAsync();

        Assert.Equal("Saved · 2 earlier responses", tab.Response.Saved.Label);
        Assert.Equal(202, tab.Response.Saved.Items[0].Saved.Response.StatusCode);
        Assert.True(tab.Response.HasResponse);
        Assert.Contains("202", tab.Response.StatusCodeText);
        Assert.Same(tab.LoadSavedResponsesAsync(), tab.LoadSavedResponsesAsync());
    }

    [AvaloniaFact]
    public async Task Showing_a_tab_starts_the_read_through_the_load_command()
    {
        using var temp = new TempWorkspace();
        var request = await SavedRequestWithResponsesAsync(temp, 200, 201);
        using var tab = new RequestTabViewModel(RequestTabTestServices.Create(temp), ModelCloner.Clone(request));
        Assert.False(tab.Response.Saved.HasItems);

        tab.IsActive = true;
        await tab.LoadSavedResponsesCommand.ExecutionTask!;

        Assert.Equal("Saved · 1 earlier response", tab.Response.Saved.Label);
        Assert.Contains("201", tab.Response.StatusCodeText);
    }

    [AvaloniaFact]
    public async Task A_restored_tab_reads_its_saved_responses_only_once_it_is_shown()
    {
        using var temp = new TempWorkspace();
        var request = await SavedRequestWithResponsesAsync(temp, 200, 201, 202);
        AddUnreadableResponse(temp, request.Id);
        var collection = temp.Workspace.Collections.Single();
        var draft = ModelCloner.Clone(request);
        draft.Url = "https://example.com/pets?draft=1";
        // The request tab has a draft, so restoring it also builds a throwaway tab for the saved request's snapshot.
        await temp.Workspace.SaveSessionAsync(new SessionState
        {
            Tabs =
            [
                new SessionTab { Kind = SessionTabKind.Request, EntityId = request.Id, RequestDraft = draft },
                new SessionTab { Kind = SessionTabKind.Collection, EntityId = collection.Id },
            ],
            ActiveTabIndex = 1,
        });

        using var vm = new MainWindowViewModel(RequestTabTestServices.Create(temp));
        var requestTab = vm.Tabs.OfType<RequestTabViewModel>().Single();
        await temp.Workspace.FlushWritesAsync();

        Assert.True(requestTab.IsDirty);
        Assert.False(requestTab.Response.Saved.HasItems);
        Assert.Empty(temp.Problems);

        vm.ActiveTab = requestTab;
        await requestTab.LoadSavedResponsesAsync();

        Assert.Equal("Saved · 2 earlier responses", requestTab.Response.Saved.Label);
        Assert.True(requestTab.Response.HasResponse);
        Assert.StartsWith("A saved response", Assert.Single(temp.Problems).Summary);
    }

    [AvaloniaFact]
    public async Task A_tab_closed_before_its_saved_responses_are_read_ignores_them()
    {
        var secrets = new GatedSecretStore();
        using var temp = new TempWorkspace(secrets);
        var request = await SavedRequestWithResponsesAsync(temp, 200);
        var tab = new RequestTabViewModel(RequestTabTestServices.Create(temp), ModelCloner.Clone(request));
        var holdQueue = await HoldTheQueueAsync(temp, secrets);

        var loading = tab.LoadSavedResponsesAsync();
        tab.Dispose();
        secrets.Gate.Set();
        await holdQueue;
        await loading;

        Assert.False(tab.Response.Saved.HasItems);
        Assert.False(tab.Response.HasResponse);
    }

    [AvaloniaFact]
    public async Task A_response_saved_while_the_read_waits_stays_in_front_of_the_list()
    {
        var secrets = new GatedSecretStore();
        using var temp = new TempWorkspace(secrets);
        var request = await SavedRequestWithResponsesAsync(temp, 200, 201);
        using var tab = new RequestTabViewModel(RequestTabTestServices.Create(temp), ModelCloner.Clone(request));
        var holdQueue = await HoldTheQueueAsync(temp, secrets);

        var loading = tab.LoadSavedResponsesAsync();
        var sentMeanwhile = Saved(299);
        tab.Response.Saved.AddSaved(sentMeanwhile);
        secrets.Gate.Set();
        await holdQueue;
        await loading;

        Assert.Same(sentMeanwhile, tab.Response.Saved.Items[0].Saved);
        Assert.Equal([299, 201, 200], tab.Response.Saved.Items.Select(item => item.Saved.Response.StatusCode));
    }

    /// <summary>Queues a write that waits at the secret store's gate, so what is queued after it waits too.</summary>
    private static async Task<Task<bool>> HoldTheQueueAsync(TempWorkspace temp, GatedSecretStore secrets)
    {
        var environment = await temp.Workspace.CreateEnvironmentAsync("Held");
        environment.Variables.Add(new Variable("token", "secret") { Type = VariableType.Secret });
        secrets.Gate.Reset();
        var saving = temp.Workspace.SaveEnvironmentAsync(environment);
        Assert.True(await secrets.WriteStarted.WaitAsync(TimeSpan.FromSeconds(10)));
        return saving;
    }
}
