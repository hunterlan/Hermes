using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Tests;

public class RequestPreparerTests
{
    private static RequestContext Context(CollectionModel? collection = null, IReadOnlyList<FolderModel>? folders = null, EnvironmentModel? env = null) =>
        new() { Collection = collection, Folders = folders ?? [], Environment = env };

    [Fact]
    public void Resolves_url_path_variables_and_headers()
    {
        var env = new EnvironmentModel { Variables = [new Variable("base", "https://api.test"), new Variable("v", "9")] };
        var request = new HttpRequestModel
        {
            Url = "{{base}}/users/:id?x={{v}}",
            PathVariables = [new KeyValueItem("id", "{{v}}")],
            Headers = [new KeyValueItem("X-A", "{{v}}"), new KeyValueItem("X-Off", "1", enabled: false)],
        };

        var prepared = RequestPreparer.Prepare(request, Context(env: env));

        Assert.Equal("https://api.test/users/9?x=9", prepared.Url);
        Assert.Equal("9", prepared.GetHeader("X-A"));
        Assert.Null(prepared.GetHeader("X-Off"));
        Assert.Equal(RequestPreparer.UserAgent, prepared.GetHeader("User-Agent"));
    }

    [Fact]
    public void Inherits_auth_from_nearest_folder()
    {
        var collection = new CollectionModel { Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "coll" } } };
        var outer = new FolderModel { Name = "outer", Auth = new AuthConfig { Type = AuthType.Basic, Basic = { Username = "u", Password = "p" } } };
        var inner = new FolderModel { Name = "inner" };
        var request = new HttpRequestModel { Url = "http://x" };

        var prepared = RequestPreparer.Prepare(request, Context(collection, [outer, inner]));

        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("u:p")), prepared.GetHeader("Authorization"));
        Assert.Equal("outer", Context(collection, [outer, inner]).ResolveAuth(request.Auth).InheritedFrom);
    }

    [Fact]
    public void Collection_auth_applies_when_nothing_overrides()
    {
        var collection = new CollectionModel
        {
            Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "{{token}}" } },
            Variables = [new Variable("token", "t0k")],
        };
        var prepared = RequestPreparer.Prepare(new HttpRequestModel { Url = "http://x" }, Context(collection));
        Assert.Equal("Bearer t0k", prepared.GetHeader("Authorization"));
    }

    [Fact]
    public void Explicit_authorization_header_wins_over_auth()
    {
        var request = new HttpRequestModel
        {
            Url = "http://x",
            Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "auth" } },
            Headers = [new KeyValueItem("authorization", "Custom 1")],
        };
        var prepared = RequestPreparer.Prepare(request, RequestContext.Empty);
        Assert.Single(prepared.Headers, h => h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Custom 1", prepared.GetHeader("Authorization"));
    }

    [Fact]
    public void ApiKey_in_query()
    {
        var request = new HttpRequestModel
        {
            Url = "http://x/a?b=1",
            Auth = new AuthConfig { Type = AuthType.ApiKey, ApiKey = { Key = "api key", Value = "v&1", AddTo = ApiKeyLocation.Query } },
        };
        Assert.Equal("http://x/a?b=1&api%20key=v%261", RequestPreparer.Prepare(request, RequestContext.Empty).Url);
    }

    [Fact]
    public void Raw_body_sets_content_type_unless_user_did()
    {
        var request = new HttpRequestModel { Url = "http://x", Body = { Mode = BodyMode.Raw, Raw = "<a/>", RawLanguage = RawLanguage.Xml } };
        Assert.Equal("application/xml", RequestPreparer.Prepare(request, RequestContext.Empty).GetHeader("Content-Type"));

        request.Headers.Add(new KeyValueItem("Content-Type", "text/xml"));
        Assert.Equal("text/xml", RequestPreparer.Prepare(request, RequestContext.Empty).GetHeader("Content-Type"));
    }

    [Fact]
    public void Request_settings_override_app_settings()
    {
        var context = new RequestContext { Settings = new AppSettings { FollowRedirects = true, VerifySsl = true, RequestTimeoutMs = 100 } };
        var request = new HttpRequestModel { Url = "http://x", Settings = { FollowRedirects = false, TimeoutMs = 5 } };
        var prepared = RequestPreparer.Prepare(request, context);
        Assert.False(prepared.FollowRedirects);
        Assert.True(prepared.VerifySsl);
        Assert.Equal(5, prepared.TimeoutMs);
    }
}
