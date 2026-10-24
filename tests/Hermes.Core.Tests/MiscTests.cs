using System.Net;
using Hermes.Core.Auth;
using Hermes.Core.Formatting;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Serialization;
using Hermes.Core.Storage;
using Hermes.TestSupport;

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
        Assert.Equal([404, 200], fresh.LoadHistory(30).Select(h => h.StatusCode));
        Assert.Single(fresh.LoadErrors);

        // Reported once: the damaged line is gone from the file after that.
        var again = new WorkspaceRepository(_dir);
        Assert.Equal([404, 200], again.LoadHistory(30).Select(h => h.StatusCode));
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

        Assert.Empty(new WorkspaceRepository(_dir).LoadHistory(30));
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
        Assert.Equal(200, Assert.Single(fresh.LoadHistory(30)).StatusCode);
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

public class FormatterAndOAuthTests
{
    [Fact]
    public void Formats_valid_json()
    {
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ]\n}", JsonFormatter.Format("{\"a\":[1,2]}").ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Formats_json_with_unquoted_variables()
    {
        var formatted = JsonFormatter.Format("{\"id\":{{id}},\"tags\":[]}");
        Assert.Equal("{\n  \"id\": {{id}},\n  \"tags\": []\n}", formatted);
    }

    [Fact]
    public void Strict_json_formatting_leaves_text_that_is_not_json()
    {
        Assert.True(JsonFormatter.TryFormat("[1]", out var formatted));
        Assert.Equal("[\n  1\n]", formatted.ReplaceLineEndings("\n"));
        Assert.False(JsonFormatter.TryFormat("{\"id\":{{id}}}", out formatted));
        Assert.Equal("{\"id\":{{id}}}", formatted);
        // A response's comments aren't dropped from its pretty view.
        Assert.False(JsonFormatter.TryFormat("[1 /* one */]", out _));
    }

    [Fact]
    public void Formats_xml()
    {
        Assert.True(XmlFormatter.TryFormat("<a><b>1</b></a>", out var xml));
        Assert.Contains("\n  <b>1</b>", xml.ReplaceLineEndings("\n"));
        Assert.False(XmlFormatter.TryFormat("<a>", out _));
    }

    [Fact]
    public void Pkce_challenge_matches_rfc7636_example()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            OAuth2Service.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Parses_token_responses()
    {
        var ok = OAuth2Service.ParseTokenResponse("{\"access_token\":\"t\",\"expires_in\":3600,\"refresh_token\":\"r\"}", HttpStatusCode.OK);
        Assert.Equal("t", ok.AccessToken);
        Assert.Equal("r", ok.RefreshToken);
        Assert.NotNull(ok.ExpiresAt);

        var form = OAuth2Service.ParseTokenResponse("access_token=abc&token_type=bearer", HttpStatusCode.OK);
        Assert.Equal("abc", form.AccessToken);

        var error = OAuth2Service.ParseTokenResponse("{\"error\":\"invalid_client\",\"error_description\":\"bad\"}", HttpStatusCode.BadRequest);
        Assert.Equal("invalid_client: bad", error.Error);
    }

    [Fact]
    public void Odd_token_responses_are_failures_not_exceptions()
    {
        Assert.NotNull(OAuth2Service.ParseTokenResponse("[1, 2]", HttpStatusCode.OK).Error);
        Assert.Equal("second", OAuth2Service.ParseTokenResponse("{\"access_token\":\"first\",\"access_token\":\"second\"}", HttpStatusCode.OK).AccessToken);
        Assert.Equal("b", OAuth2Service.ParseTokenResponse("access_token=a&access_token=b", HttpStatusCode.OK).AccessToken);
    }

    [Fact]
    public async Task Token_requests_neither_send_nor_keep_the_users_cookies()
    {
        string? cookieSent = null;
        using var server = new LocalHttpServer(async (ctx, stop) =>
        {
            cookieSent = ctx.Request.Headers["Cookie"];
            ctx.Response.AppendHeader("Set-Cookie", "idp_session=1; Path=/");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync("{\"access_token\":\"t\"}"u8.ToArray(), stop);
            ctx.Response.Close();
        });
        using var userClients = new HttpClientProvider();
        userClients.Cookies.Add(new Uri(server.Url), new Cookie("user_session", "1"));
        using var oauth = new OAuth2Service();

        var result = await oauth.GetTokenAsync(
            new OAuth2Auth { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = server.Url + "token", ClientId = "c" },
            Variables.VariableScope.Empty, _ => Task.CompletedTask, verifySsl: true, TestContext.Current.CancellationToken);

        Assert.Equal("t", result.AccessToken);
        Assert.Null(cookieSent);
        Assert.DoesNotContain(userClients.GetAllCookies(), c => c.Name == "idp_session");
    }

    [Fact]
    public async Task Token_endpoint_connection_failure_is_reported()
    {
        using var port = new ResettingPort();
        using var oauth = new OAuth2Service();

        var result = await oauth.GetTokenAsync(
            new OAuth2Auth { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = $"http://127.0.0.1:{port.Port}/token", ClientId = "c" },
            Variables.VariableScope.Empty, _ => Task.CompletedTask, verifySsl: true, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Error);
        Assert.Null(result.AccessToken);
    }
}
