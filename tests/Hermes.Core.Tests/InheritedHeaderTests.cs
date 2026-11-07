using Hermes.Core.CodeGen;
using Hermes.Core.Headers;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Requests;

namespace Hermes.Core.Tests;

public class InheritedHeaderTests
{
    private static RequestContext Context(CollectionModel? collection = null, IReadOnlyList<FolderModel>? folders = null, EnvironmentModel? env = null) =>
        new() { Collection = collection, Folders = folders ?? [], Environment = env };

    private static List<string> HeaderLines(PreparedRequest prepared, string name) =>
        prepared.Headers.Where(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).ToList();

    [Fact]
    public void Nearest_level_wins_from_the_request_out_to_the_collection()
    {
        var collection = new CollectionModel { Name = "C", Headers = [new("X-Level", "collection"), new("X-Collection", "c")] };
        var outer = new FolderModel { Name = "outer", Headers = [new("X-Level", "outer"), new("X-Outer", "o")] };
        var inner = new FolderModel { Name = "inner", Headers = [new("X-Level", "inner"), new("X-Inner", "i")] };
        var request = new HttpRequestModel { Url = "http://x" };
        var context = Context(collection, [outer, inner]);

        var prepared = RequestPreparer.Prepare(request, context);

        Assert.Equal(["inner"], HeaderLines(prepared, "X-Level"));
        Assert.Equal("c", prepared.GetHeader("X-Collection"));
        Assert.Equal("o", prepared.GetHeader("X-Outer"));
        Assert.Equal("i", prepared.GetHeader("X-Inner"));

        var resolved = context.ResolveHeaders(request);
        Assert.Equal(["X-Level", "X-Collection", "X-Level", "X-Outer", "X-Level", "X-Inner"], resolved.Inherited.Select(h => h.Key));
        var levels = resolved.Inherited.Where(h => h.Key == "X-Level").ToList();
        Assert.Equal([HeaderState.Overridden, HeaderState.Overridden, HeaderState.Sent], levels.Select(h => h.State));
        Assert.All(levels.Take(2), h => Assert.Equal(inner.Id, h.OverriddenBy?.Id));
        Assert.Equal([false, true, true], levels.Select(h => h.OverridesOuter));
        Assert.Equal(new HeaderSource(HeaderSourceKind.Collection, collection.Id, "C"), levels[0].Source);
        Assert.Equal(new HeaderSource(HeaderSourceKind.Folder, inner.Id, "inner"), levels[2].Source);

        request.Headers.Add(new KeyValueItem("X-Level", "request"));
        Assert.Equal(["request"], HeaderLines(RequestPreparer.Prepare(request, context), "X-Level"));
        resolved = context.ResolveHeaders(request);
        Assert.True(Assert.Single(resolved.Own).OverridesOuter);
        Assert.All(resolved.Inherited.Where(h => h.Key == "X-Level"), h =>
        {
            Assert.Equal(HeaderState.Overridden, h.State);
            Assert.Equal(HeaderSourceKind.Request, h.OverriddenBy?.Kind);
        });
    }

    [Fact]
    public void Names_match_ignoring_case()
    {
        var collection = new CollectionModel { Headers = [new("x-tenant", "collection")] };
        var folder = new FolderModel { Headers = [new(" X-TENANT ", "folder")] };
        var request = new HttpRequestModel { Url = "http://x" };

        Assert.Equal(["folder"], HeaderLines(RequestPreparer.Prepare(request, Context(collection, [folder])), "X-Tenant"));

        request.Headers.Add(new KeyValueItem("X-Tenant", "request"));
        Assert.Equal(["request"], HeaderLines(RequestPreparer.Prepare(request, Context(collection, [folder])), "X-Tenant"));
    }

    [Fact]
    public void Unticked_rows_are_not_sent_and_do_not_hide_outer_headers()
    {
        var collection = new CollectionModel { Headers = [new("X-A", "collection")] };
        var folder = new FolderModel { Headers = [new("X-A", "folder", enabled: false), new("X-B", "folder", enabled: false), new("", "no name")] };
        var request = new HttpRequestModel { Url = "http://x", Headers = [new("X-A", "request", enabled: false)] };
        var context = Context(collection, [folder]);

        var prepared = RequestPreparer.Prepare(request, context);

        Assert.Equal(["collection"], HeaderLines(prepared, "X-A"));
        Assert.Null(prepared.GetHeader("X-B"));
        var resolved = context.ResolveHeaders(request);
        Assert.Empty(resolved.Own);
        var inherited = Assert.Single(resolved.Inherited);
        Assert.Equal(HeaderState.Sent, inherited.State);
        Assert.False(inherited.OverridesOuter);
    }

    [Fact]
    public void Repeated_rows_within_one_level_are_all_sent()
    {
        var folder = new FolderModel { Headers = [new("X-A", "1"), new("x-a", "2")] };
        var collection = new CollectionModel { Headers = [new("X-A", "collection")] };

        var prepared = RequestPreparer.Prepare(new HttpRequestModel { Url = "http://x" }, Context(collection, [folder]));

        Assert.Equal(["1", "2"], HeaderLines(prepared, "X-A"));
    }

    [Fact]
    public void Skipped_headers_are_not_sent_from_any_level()
    {
        var collection = new CollectionModel { Headers = [new("X-A", "collection")] };
        var folder = new FolderModel { Headers = [new("X-A", "folder"), new("X-B", "folder")] };
        var request = new HttpRequestModel { Url = "http://x", SkippedInheritedHeaders = [" x-a "] };
        var context = Context(collection, [folder]);

        var prepared = RequestPreparer.Prepare(request, context);

        Assert.Null(prepared.GetHeader("X-A"));
        Assert.Equal("folder", prepared.GetHeader("X-B"));
        var states = context.ResolveHeaders(request).Inherited.Select(h => (h.Key, h.Value, h.State));
        Assert.Equal([("X-A", "collection", HeaderState.Overridden), ("X-A", "folder", HeaderState.Skipped), ("X-B", "folder", HeaderState.Sent)], states);

        // Skipping only applies to inherited headers: the request's own header of that name is still sent.
        request.Headers.Add(new KeyValueItem("X-A", "request"));
        Assert.Equal(["request"], HeaderLines(RequestPreparer.Prepare(request, context), "X-A"));
    }

    [Fact]
    public void Inherited_headers_win_over_automatic_ones()
    {
        var folder = new FolderModel { Headers = [new("Content-Type", "application/vnd.api+json"), new("Accept", "application/json")] };
        var request = new HttpRequestModel { Url = "http://x", Body = { Mode = BodyMode.Raw, Raw = "{}", RawLanguage = RawLanguage.Json } };

        var prepared = RequestPreparer.Prepare(request, Context(folders: [folder]));

        Assert.Equal(["application/vnd.api+json"], HeaderLines(prepared, "Content-Type"));
        Assert.Equal(["application/json"], HeaderLines(prepared, "Accept"));
    }

    [Fact]
    public void Authorization_header_replaces_the_auth_header_and_is_flagged()
    {
        var collection = new CollectionModel { Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "auth" } } };
        var folder = new FolderModel { Name = "F", Headers = [new("authorization", "Custom folder")] };
        var request = new HttpRequestModel { Url = "http://x" };
        var context = Context(collection, [folder]);

        Assert.Equal(["Custom folder"], HeaderLines(RequestPreparer.Prepare(request, context), "Authorization"));
        var flagged = context.ResolveHeaders(request).AuthHeaderOverride;
        Assert.NotNull(flagged);
        Assert.Equal(folder.Id, flagged.Source.Id);

        // The request's own header replaces it instead; the folder's is overridden and no longer flagged.
        request.Headers.Add(new KeyValueItem("Authorization", "Custom request"));
        Assert.Equal(["Custom request"], HeaderLines(RequestPreparer.Prepare(request, context), "Authorization"));
        var resolved = context.ResolveHeaders(request);
        Assert.Equal(HeaderSourceKind.Request, resolved.AuthHeaderOverride?.Source.Kind);
        Assert.False(Assert.Single(resolved.Inherited).ReplacesAuthHeader);
    }

    [Fact]
    public void Authorization_header_is_not_flagged_when_the_auth_adds_none()
    {
        var folder = new FolderModel { Headers = [new("Authorization", "Custom")] };
        HttpRequestModel Request(AuthConfig auth) => new() { Url = "http://x", Auth = auth };

        Assert.Null(Context(folders: [folder]).ResolveHeaders(Request(AuthConfig.Inherit())).AuthHeaderOverride);
        Assert.Null(Context(folders: [folder]).ResolveHeaders(Request(AuthConfig.NoAuth())).AuthHeaderOverride);
        Assert.Null(Context(folders: [folder]).ResolveHeaders(Request(new AuthConfig { Type = AuthType.OAuth2 })).AuthHeaderOverride);
        var apiKeyInQuery = new AuthConfig { Type = AuthType.ApiKey, ApiKey = { Key = "Authorization", Value = "v", AddTo = ApiKeyLocation.Query } };
        Assert.Null(Context(folders: [folder]).ResolveHeaders(Request(apiKeyInQuery)).AuthHeaderOverride);
    }

    [Fact]
    public void A_header_named_like_the_api_key_header_is_flagged()
    {
        var env = new EnvironmentModel { Variables = [new Variable("keyName", "X-Api-Key")] };
        var folder = new FolderModel { Headers = [new("x-api-key", "from folder"), new("Authorization", "not replacing anything")] };
        var request = new HttpRequestModel
        {
            Url = "http://x",
            Auth = new AuthConfig { Type = AuthType.ApiKey, ApiKey = { Key = "{{keyName}}", Value = "secret", AddTo = ApiKeyLocation.Header } },
        };
        var context = Context(folders: [folder], env: env);

        Assert.Equal(["from folder"], HeaderLines(RequestPreparer.Prepare(request, context), "X-Api-Key"));
        Assert.Equal("x-api-key", context.ResolveHeaders(request).AuthHeaderOverride?.Key);
    }

    [Fact]
    public void Folder_header_variables_resolve_from_the_active_environment()
    {
        var collection = new CollectionModel { Headers = [new("X-Region", "{{region}}")], Variables = [new Variable("region", "eu")] };
        var folder = new FolderModel { Headers = [new("X-Tenant", "{{tenantId}}")] };
        var env = new EnvironmentModel { Variables = [new Variable("tenantId", "t-42")] };
        var request = new HttpRequestModel { Url = "http://x" };

        var prepared = RequestPreparer.Prepare(request, Context(collection, [folder], env));

        Assert.Equal("t-42", prepared.GetHeader("X-Tenant"));
        Assert.Equal("eu", prepared.GetHeader("X-Region"));
        // The merge itself keeps the rows as written.
        Assert.Contains(Context(collection, [folder], env).ResolveHeaders(request).Inherited, h => h.Value == "{{tenantId}}");
    }

    [Fact]
    public void Sent_request_preview_and_curl_carry_the_same_inherited_headers()
    {
        var collection = new CollectionModel { Headers = [new("X-Collection", "c")] };
        var folder = new FolderModel { Headers = [new("X-Tenant", "{{tenantId}}"), new("X-Skipped", "s")] };
        var env = new EnvironmentModel { Variables = [new Variable("tenantId", "t-42")] };
        var request = new HttpRequestModel { Url = "http://x", Headers = [new("X-Own", "1")], SkippedInheritedHeaders = ["X-Skipped"] };
        var context = Context(collection, [folder], env);

        var (sent, masked) = RequestPreparer.PrepareWithMasked(request, context);
        var preview = RequestPreparer.Preview(request, context);
        var curl = CurlGenerator.Generate(RequestPreparer.Prepare(request, context, includeDefaultHeaders: false));
        var sentText = RawRequestFormatter.Format(masked);

        // Own headers first, then inherited ones outermost first.
        string[] expected = ["X-Own: 1", "X-Collection: c", "X-Tenant: t-42"];
        Assert.Equal(expected, sent.Headers.Take(3).Select(h => $"{h.Key}: {h.Value}"));
        Assert.Equal(expected, preview.Headers.Take(3).Select(h => $"{h.Key}: {h.Value}"));
        Assert.All(expected, line => Assert.Contains(line, sentText));
        Assert.All(expected, line => Assert.Contains($"--header '{line}'", curl));
        Assert.DoesNotContain("X-Skipped", sentText);
        Assert.DoesNotContain("X-Skipped", curl);
    }
}
