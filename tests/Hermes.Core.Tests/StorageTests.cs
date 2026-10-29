using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Storage;

namespace Hermes.Core.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid());

    [Fact]
    public void Only_one_instance_holds_a_data_folder()
    {
        using (var first = DataFolderLock.TryAcquire(_dir))
        {
            Assert.NotNull(first);
            Assert.Null(DataFolderLock.TryAcquire(_dir));
        }

        using var afterRelease = DataFolderLock.TryAcquire(_dir);
        Assert.NotNull(afterRelease);
    }

    [Fact]
    public async Task A_save_waits_for_a_reader_that_briefly_holds_the_file()
    {
        var path = Path.Combine(_dir, "held.json");
        JsonFileStore.Write(path, new AppSettings { ShowTabs = false }, HermesJsonContext.Default.AppSettings);
        // Opened the way other programs usually read: the file can't be replaced until this lets go (on Windows).
        var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await holder.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        JsonFileStore.Write(path, new AppSettings { ShowTabs = true }, HermesJsonContext.Default.AppSettings);
        await release;

        Assert.True(JsonFileStore.Read(path, HermesJsonContext.Default.AppSettings)!.ShowTabs);
    }

    [Fact]
    public void Saved_responses_keep_the_newest_and_drop_large_bodies()
    {
        var repo = new WorkspaceRepository(_dir);
        var id = Guid.NewGuid();
        for (var i = 0; i < WorkspaceRepository.MaxSavedResponses + 2; i++)
            repo.SaveResponse(id, new SavedResponse { Response = new ResponseModel { StatusCode = 200 + i, Body = [1, 2, 3] }, SentRequest = $"GET /{i}" });
        repo.SaveResponse(id, new SavedResponse { Response = new ResponseModel { StatusCode = 500, Body = new byte[WorkspaceRepository.MaxSavedBodyBytes + 1] } });

        var loaded = new WorkspaceRepository(_dir).LoadResponses(id).Items;

        Assert.Equal(WorkspaceRepository.MaxSavedResponses, loaded.Count);
        Assert.Equal(500, loaded[0].Response.StatusCode);
        Assert.True(loaded[0].BodyOmitted);
        Assert.Empty(loaded[0].Response.Body);
        Assert.Equal(200 + WorkspaceRepository.MaxSavedResponses + 1, loaded[1].Response.StatusCode);
        Assert.Equal([1, 2, 3], loaded[1].Response.Body);

        repo.DeleteResponses(id);
        Assert.Empty(repo.LoadResponses(id).Items);
    }

    [Fact]
    public void Concurrent_writes_to_one_file_neither_fail_nor_leave_temp_files()
    {
        var path = Path.Combine(_dir, "session.json");

        Parallel.For(0, 200, i => JsonFileStore.Write(path, new SessionState { ActiveTabIndex = i }, HermesJsonContext.Default.SessionState));

        var stored = JsonFileStore.Read(path, HermesJsonContext.Default.SessionState);
        Assert.NotNull(stored);
        Assert.InRange(stored.ActiveTabIndex, 0, 199);
        Assert.Equal(["session.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void An_unreadable_saved_response_is_reported_and_kept()
    {
        var repo = new WorkspaceRepository(_dir);
        var id = Guid.NewGuid();
        repo.SaveResponse(id, new SavedResponse { Timestamp = DateTimeOffset.Now.AddMinutes(-1), Response = new ResponseModel { StatusCode = 200 } });
        var corrupt = Directory.GetFiles(Path.Combine(_dir, "responses", id.ToString())).Single();
        File.WriteAllText(corrupt, "{ cut short");

        repo.SaveResponse(id, new SavedResponse { Response = new ResponseModel { StatusCode = 201 } });
        var loaded = repo.LoadResponses(id);

        Assert.Equal(201, Assert.Single(loaded.Items).Response.StatusCode);
        Assert.Contains(corrupt, Assert.Single(loaded.Errors));
        Assert.Equal("{ cut short", File.ReadAllText(corrupt));
    }

    [Fact]
    public void History_lines_cut_short_are_skipped_and_later_entries_still_load()
    {
        var repo = new WorkspaceRepository(_dir);
        repo.AppendHistory(new HistoryEntry { StatusCode = 200 });
        var file = Directory.GetFiles(Path.Combine(_dir, "history")).Single();
        File.AppendAllText(file, "\n{\"statusCode\": 2");
        repo.AppendHistory(new HistoryEntry { StatusCode = 404 });

        var fresh = new WorkspaceRepository(_dir);
        Assert.Equal([404, 200], fresh.LoadHistory().Select(h => h.StatusCode));
        Assert.Single(fresh.LoadErrors);

        // Reported once: the damaged line is gone from the file after that.
        var again = new WorkspaceRepository(_dir);
        Assert.Equal([404, 200], again.LoadHistory().Select(h => h.StatusCode));
        Assert.Empty(again.LoadErrors);
    }

    [Fact]
    public async Task A_clear_queued_after_an_append_wins()
    {
        var repo = new WorkspaceRepository(_dir);
        var writer = new BackgroundWriter();
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        writer.Enqueue(() => { started.Set(); release.Wait(); });
        writer.Enqueue(() => repo.AppendHistory(new HistoryEntry { StatusCode = 200 }));
        writer.Enqueue(repo.ClearHistory);

        started.Wait(TestContext.Current.CancellationToken);
        release.Set();
        await writer.FlushAsync();

        Assert.Empty(new WorkspaceRepository(_dir).LoadHistory());
    }

    [Fact]
    public async Task Background_write_failures_are_reported()
    {
        var writer = new BackgroundWriter();
        Exception? failure = null;
        writer.Failed += e => failure = e;

        writer.Enqueue(() => throw new IOException("disk full"));
        writer.Enqueue(() => { });
        await writer.FlushAsync();

        Assert.Equal("disk full", failure?.Message);
    }

    [Fact]
    public void Collections_environments_history_and_session_persist()
    {
        var repo = new WorkspaceRepository(_dir);
        var collection = new CollectionModel { Name = "C" };
        var folder = new FolderModel { Name = "F" };
        folder.Items.Add(new HttpRequestModel { Name = "R", Url = "http://x" });
        collection.Items.Add(folder);
        repo.SaveCollection(collection);
        repo.SaveEnvironment(new EnvironmentModel { Name = "E", Variables = [new Variable("a", "1")] });
        repo.AppendHistory(new HistoryEntry { Request = new HttpRequestModel { Url = "http://h" }, StatusCode = 200 });
        repo.SaveSession(new SessionState { ActiveTabIndex = 2 });

        var fresh = new WorkspaceRepository(_dir);
        var loaded = Assert.Single(fresh.LoadCollections());
        var loadedRequest = Assert.IsType<HttpRequestModel>(Assert.IsType<FolderModel>(loaded.Items[0]).Items[0]);
        Assert.Equal("http://x", loadedRequest.Url);
        Assert.Equal("1", Assert.Single(fresh.LoadEnvironments()).Variables[0].Value);
        Assert.Equal(200, Assert.Single(fresh.LoadHistory()).StatusCode);
        Assert.Equal(2, fresh.LoadSession().ActiveTabIndex);

        fresh.DeleteCollection(collection.Id);
        Assert.Empty(fresh.LoadCollections());
    }

    [Fact]
    public void Corrupt_files_are_reported_not_thrown()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "collections"));
        File.WriteAllText(Path.Combine(_dir, "collections", "bad.json"), "{ nope");
        var repo = new WorkspaceRepository(_dir);
        Assert.Empty(repo.LoadCollections());
        Assert.Single(repo.LoadErrors);
    }

    [Fact]
    public void Duplicate_gives_new_ids()
    {
        var folder = new FolderModel { Items = [new HttpRequestModel()] };
        var copy = (FolderModel)ModelCloner.Duplicate(folder);
        Assert.NotEqual(folder.Id, copy.Id);
        Assert.NotEqual(folder.Items[0].Id, copy.Items[0].Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }
}
