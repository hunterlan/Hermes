using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Serialization;

namespace Hermes.App.Tests;

/// <summary>
/// Opening a request in a tab and building the model again must give the same request back, otherwise a restored
/// tab compares against its saved snapshot and opens dirty. These pin today's behaviour.
/// </summary>
public class RequestTabRoundTripTests
{
    // Models are written in the form the tab normalizes to: upper-case method, and query params listed (enabled ones
    // match the URL's query string, disabled ones are only in the list).
    private static Dictionary<string, HttpRequestModel> Cases() => new()
    {
        ["plain GET"] = new() { Name = "Plain", Url = "https://example.com/items" },
        ["method, description and events"] = new()
        {
            Name = "Described",
            Method = "PATCH",
            Url = "https://example.com/items/1",
            Description = "Updates **one** item.",
            EventsJson = """[{"listen":"prerequest","script":{"exec":["console.log(1)"]}}]""",
        },
        ["query params with a disabled one"] = new()
        {
            Name = "Query",
            Url = "https://example.com/search?q=cats&page=2",
            QueryParams =
            [
                new("q", "cats"),
                new("debug", "1", enabled: false) { Description = "turn on" },
                new("page", "2") { Description = "which page" },
            ],
        },
        ["path variables"] = new()
        {
            Name = "Path",
            Url = "https://example.com/users/:userId/posts/:postId",
            PathVariables = [new("userId", "42") { Description = "the user" }, new("postId", "7")],
        },
        ["headers and skipped inherited headers"] = new()
        {
            Name = "Headers",
            Url = "https://example.com",
            Headers = [new("X-One", "1"), new("X-Two", "2", enabled: false) { Description = "off for now" }],
            SkippedInheritedHeaders = ["Accept", "X-Team"],
        },
        ["raw JSON body"] = RawBody(RawLanguage.Json, """{"a":1}"""),
        ["raw text body"] = RawBody(RawLanguage.Text, "hello"),
        ["raw XML body"] = RawBody(RawLanguage.Xml, "<a><b/></a>"),
        ["raw HTML body"] = RawBody(RawLanguage.Html, "<p>hi</p>"),
        ["raw JavaScript body"] = RawBody(RawLanguage.JavaScript, "const a = 1;"),
        ["form data with a file row"] = new()
        {
            Name = "Form",
            Method = "POST",
            Url = "https://example.com/upload",
            Body = new RequestBody
            {
                Mode = BodyMode.FormData,
                FormData =
                [
                    new("title", "Cat"),
                    new("photo", "/tmp/cat.png") { Kind = KeyValueKind.File },
                    new("skipped", "x", enabled: false),
                ],
            },
        },
        ["url encoded body"] = new()
        {
            Name = "Encoded",
            Method = "POST",
            Url = "https://example.com/login",
            Body = new RequestBody { Mode = BodyMode.UrlEncoded, UrlEncoded = [new("user", "a b"), new("pw", "&=", enabled: false)] },
        },
        ["binary file body"] = new()
        {
            Name = "Binary",
            Method = "PUT",
            Url = "https://example.com/blob",
            Body = new RequestBody { Mode = BodyMode.Binary, BinaryFilePath = "/tmp/data.bin" },
        },
        ["unsupported body"] = new()
        {
            Name = "GraphQL",
            Method = "POST",
            Url = "https://example.com/graphql",
            Body = new RequestBody { Mode = BodyMode.None, UnsupportedJson = """{"mode":"graphql","graphql":{"query":"{ a }"}}""" },
        },
        ["follow redirects off"] = WithSettings(new RequestSettings { FollowRedirects = false }),
        ["follow redirects on"] = WithSettings(new RequestSettings { FollowRedirects = true }),
        ["verify SSL off"] = WithSettings(new RequestSettings { VerifySsl = false }),
        ["verify SSL on"] = WithSettings(new RequestSettings { VerifySsl = true }),
        ["timeout"] = WithSettings(new RequestSettings { TimeoutMs = 2500 }),
        ["all settings"] = WithSettings(new RequestSettings { FollowRedirects = false, VerifySsl = false, TimeoutMs = 0 }),
        ["inherited auth"] = WithAuth(AuthConfig.Inherit()),
        // Every OAuth 2.0 field set to a non-default value.
        ["oauth2 auth"] = WithAuth(new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2 = new OAuth2Auth
            {
                GrantType = OAuth2GrantType.Password,
                AuthUrl = "https://auth.example.com/authorize",
                AccessTokenUrl = "https://auth.example.com/token",
                ClientId = "client-id",
                ClientSecret = "client-secret",
                Scope = "read write",
                RedirectUri = "http://localhost:9999/cb",
                State = "xyz",
                UsePkce = false,
                Username = "me",
                Password = "pw",
                ClientAuthentication = OAuth2ClientAuthentication.Body,
                HeaderPrefix = "Token",
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero),
            },
        }),
        ["no auth"] = WithAuth(AuthConfig.NoAuth()),
        ["basic auth"] = WithAuth(new AuthConfig { Type = AuthType.Basic, Basic = new BasicAuth { Username = "me", Password = "pw" } }),
        ["bearer auth"] = WithAuth(new AuthConfig { Type = AuthType.Bearer, Bearer = new BearerAuth { Token = "tok" } }),
        ["api key auth"] = WithAuth(new AuthConfig
        {
            Type = AuthType.ApiKey,
            ApiKey = new ApiKeyAuth { Key = "k", Value = "v", AddTo = ApiKeyLocation.Query },
        }),
    };

    public static TheoryData<string> CaseNames => new(Cases().Keys);

    private static HttpRequestModel RawBody(RawLanguage language, string raw) => new()
    {
        Name = "Raw " + language,
        Method = "POST",
        Url = "https://example.com/raw",
        Body = new RequestBody { Mode = BodyMode.Raw, RawLanguage = language, Raw = raw },
    };

    private static HttpRequestModel WithSettings(RequestSettings settings) =>
        new() { Name = "Settings", Url = "https://example.com", Settings = settings };

    private static HttpRequestModel WithAuth(AuthConfig auth) =>
        new() { Name = "Auth", Url = "https://example.com", Auth = auth };

    [AvaloniaTheory]
    [MemberData(nameof(CaseNames))]
    public void A_tab_builds_the_model_it_was_opened_on_and_is_clean(string caseName)
    {
        using var temp = new TempWorkspace();
        var model = Cases()[caseName];

        using var tab = new RequestTabViewModel(RequestTabTestServices.Create(temp), ModelCloner.Clone(model));

        Assert.Equal(ModelCloner.Serialize(model), ModelCloner.Serialize(tab.BuildModel()));
        Assert.False(tab.IsDirty);
    }

    [AvaloniaTheory]
    [MemberData(nameof(CaseNames))]
    public async Task A_saved_request_reopens_with_the_same_model_and_is_clean(string caseName)
    {
        using var temp = new TempWorkspace();
        var collection = await temp.Workspace.CreateCollectionAsync("Saved");
        var model = Cases()[caseName];
        await temp.Workspace.AddNodeAsync(collection, null, ModelCloner.Clone(model));
        var services = RequestTabTestServices.Create(temp);

        using var first = new RequestTabViewModel(services, ModelCloner.Clone(model));
        Assert.True(await first.SaveAsync());
        var saved = Assert.IsType<HttpRequestModel>(temp.Workspace.Find(model.Id)!.Node);
        using var reopened = new RequestTabViewModel(services, ModelCloner.Clone(saved));

        Assert.False(first.IsDirty);
        Assert.False(reopened.IsDirty);
        Assert.Equal(ModelCloner.Serialize(model), ModelCloner.Serialize(reopened.BuildModel()));
    }

    [AvaloniaTheory]
    [MemberData(nameof(CaseNames))]
    public void An_unsaved_draft_restored_from_the_session_has_the_same_model(string caseName)
    {
        using var temp = new TempWorkspace();
        var services = RequestTabTestServices.Create(temp);
        var model = Cases()[caseName];
        using var original = new RequestTabViewModel(services, ModelCloner.Clone(model));

        var draft = original.ToSession().RequestDraft;

        // MainWindowViewModel restores a draft that isn't in a collection against a blank request's snapshot.
        Assert.NotNull(draft);
        using var restored = new RequestTabViewModel(services, ModelCloner.Clone(draft),
            ModelCloner.Serialize(HttpRequestModel.CreateNew()));
        Assert.Equal(ModelCloner.Serialize(model), ModelCloner.Serialize(restored.BuildModel()));
    }

    [AvaloniaFact]
    public void A_collapsed_response_and_its_split_come_back_from_the_session()
    {
        using var temp = new TempWorkspace();
        var services = RequestTabTestServices.Create(temp);
        using var original = new RequestTabViewModel(services, HttpRequestModel.CreateNew());
        original.Response.Pane.ResponseSplitRatio = 0.3;
        original.Response.Pane.SideBySideResponseSplitRatio = 0.65;
        original.Response.Pane.ToggleCollapsedCommand.Execute(null);

        var saved = original.ToSession();
        using var restored = new RequestTabViewModel(services, ModelCloner.Clone(saved.RequestDraft!));
        restored.RestoreSession(saved);

        Assert.True(restored.Response.Pane.IsResponseCollapsed);
        Assert.Equal(0.3, restored.Response.Pane.ResponseSplitRatio);
        Assert.Equal(0.65, restored.Response.Pane.SideBySideResponseSplitRatio);
    }

    [AvaloniaFact]
    public void A_side_by_side_split_that_cannot_be_one_comes_back_as_half()
    {
        using var temp = new TempWorkspace();
        var services = RequestTabTestServices.Create(temp);
        var saved = new SessionTab
        {
            Kind = SessionTabKind.Request,
            RequestDraft = HttpRequestModel.CreateNew(),
            SideBySideResponseSplitRatio = 1.5,
        };

        using var restored = new RequestTabViewModel(services, saved.RequestDraft);
        restored.RestoreSession(saved);

        Assert.Equal(SessionTab.DefaultResponseSplitRatio, restored.Response.Pane.SideBySideResponseSplitRatio);
    }

    [AvaloniaFact]
    public void A_tab_from_a_session_without_the_response_pane_comes_back_expanded_at_half()
    {
        using var temp = new TempWorkspace();
        var services = RequestTabTestServices.Create(temp);
        // What an older session file gives: no response pane values, so their defaults.
        var saved = new SessionTab { Kind = SessionTabKind.Request, RequestDraft = HttpRequestModel.CreateNew() };

        using var restored = new RequestTabViewModel(services, saved.RequestDraft);
        restored.RestoreSession(saved);

        Assert.False(restored.Response.Pane.IsResponseCollapsed);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, restored.Response.Pane.ResponseSplitRatio);
        Assert.Equal(SessionTab.DefaultResponseSplitRatio, restored.Response.Pane.SideBySideResponseSplitRatio);
    }
}
