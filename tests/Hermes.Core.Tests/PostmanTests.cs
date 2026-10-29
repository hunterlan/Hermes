using System.Text.Json.Nodes;
using Hermes.Core.Import;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;

namespace Hermes.Core.Tests;

public class PostmanTests
{
    private static string Sample => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "sample.postman_collection.json"));

    [Fact]
    public void Imports_structure_auth_variables_and_bodies()
    {
        var c = PostmanCollectionImporter.Import(Sample);

        Assert.Equal("Sample API", c.Name);
        Assert.Equal(AuthType.Bearer, c.Auth.Type);
        Assert.Equal("{{token}}", c.Auth.Bearer.Token);
        Assert.Equal(2, c.Variables.Count);
        Assert.NotNull(c.EventsJson);

        var users = Assert.IsType<FolderModel>(c.Items[0]);
        Assert.Equal(AuthType.Basic, users.Auth.Type);

        var get = Assert.IsType<HttpRequestModel>(users.Items[0]);
        Assert.Equal("{{baseUrl}}/users/:id?verbose=true", get.Url);
        Assert.Equal(2, get.QueryParams.Count);
        Assert.False(get.QueryParams[1].Enabled);
        Assert.Equal("42", get.PathVariables.Single().Value);
        Assert.Equal(AuthType.Inherit, get.Auth.Type);
        Assert.False(get.Headers[1].Enabled);

        var create = Assert.IsType<HttpRequestModel>(users.Items[1]);
        Assert.Equal(AuthType.None, create.Auth.Type);
        Assert.Equal(BodyMode.Raw, create.Body.Mode);
        Assert.Equal(RawLanguage.Json, create.Body.RawLanguage);

        var upload = Assert.IsType<HttpRequestModel>(c.Items[1]);
        Assert.Equal(KeyValueKind.File, upload.Body.FormData[0].Kind);
        Assert.Equal("/tmp/a.png", upload.Body.FormData[0].Value);

        var graphql = Assert.IsType<HttpRequestModel>(c.Items[2]);
        Assert.Equal(BodyMode.None, graphql.Body.Mode);
        Assert.NotNull(graphql.Body.UnsupportedJson);
    }

    [Fact]
    public void Export_then_import_round_trips()
    {
        var original = PostmanCollectionImporter.Import(Sample);
        var json = PostmanCollectionExporter.Export(original);
        var again = PostmanCollectionImporter.Import(json);

        Assert.Equal(PostmanCollectionExporter.SchemaUrl, JsonNode.Parse(json)!["info"]!["schema"]!.GetValue<string>());
        Assert.Equal(original.Name, again.Name);
        Assert.Equal(original.PostmanId, again.PostmanId);
        Assert.Equal(original.Auth.Bearer.Token, again.Auth.Bearer.Token);
        Assert.Equal(original.Variables.Select(v => (v.Key, v.Value)), again.Variables.Select(v => (v.Key, v.Value)));
        Assert.Equal(original.EventsJson, again.EventsJson);

        var a = (HttpRequestModel)((FolderModel)original.Items[0]).Items[0];
        var b = (HttpRequestModel)((FolderModel)again.Items[0]).Items[0];
        Assert.Equal(a.Url, b.Url);
        Assert.Equal(a.QueryParams.Select(q => (q.Key, q.Value, q.Enabled)), b.QueryParams.Select(q => (q.Key, q.Value, q.Enabled)));
        Assert.Equal(a.PathVariables.Select(q => (q.Key, q.Value)), b.PathVariables.Select(q => (q.Key, q.Value)));
        Assert.Equal(a.Headers.Select(q => (q.Key, q.Value, q.Enabled)), b.Headers.Select(q => (q.Key, q.Value, q.Enabled)));

        var upload = (HttpRequestModel)again.Items[1];
        Assert.Equal(KeyValueKind.File, upload.Body.FormData[0].Kind);

        // Unsupported body survives.
        Assert.Contains("graphql", ((HttpRequestModel)again.Items[2]).Body.UnsupportedJson);
    }

    [Fact]
    public void OAuth2_auth_round_trips()
    {
        var auth = new AuthConfig
        {
            Type = AuthType.OAuth2,
            OAuth2 = { GrantType = OAuth2GrantType.AuthorizationCode, UsePkce = true, ClientId = "id", AuthUrl = "https://a", AccessTokenUrl = "https://t", Scope = "s" },
        };
        var back = PostmanCollectionImporter.ReadAuth(PostmanCollectionExporter.WriteAuth(auth))!;
        Assert.Equal(AuthType.OAuth2, back.Type);
        Assert.True(back.OAuth2.UsePkce);
        Assert.Equal("id", back.OAuth2.ClientId);
        Assert.Equal("https://t", back.OAuth2.AccessTokenUrl);
    }

    [Fact]
    public void Environment_round_trips_initial_values_only()
    {
        var env = new EnvironmentModel
        {
            Name = "Dev",
            Variables = [new Variable("a", "1") { CurrentValue = "local" }, new Variable("s", "x") { Type = VariableType.Secret, Enabled = false }],
        };
        var json = PostmanEnvironmentConverter.Export(env);
        Assert.DoesNotContain("local", json);

        var back = PostmanEnvironmentConverter.Import(json);
        Assert.Equal("Dev", back.Name);
        Assert.Equal("1", back.Variables[0].Value);
        Assert.Null(back.Variables[0].CurrentValue);
        Assert.Equal(VariableType.Secret, back.Variables[1].Type);
        Assert.False(back.Variables[1].Enabled);
    }

    [Fact]
    public void Environment_colour_is_neither_exported_nor_imported()
    {
        var json = PostmanEnvironmentConverter.Export(new EnvironmentModel { Name = "Prod", Color = EnvironmentColor.Red });
        Assert.DoesNotContain("color", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Red", json);

        var withStrayColor = JsonNode.Parse(json)!.AsObject();
        withStrayColor["color"] = "Red";
        var back = PostmanEnvironmentConverter.Import(withStrayColor);
        Assert.Equal(EnvironmentColor.None, back.Color);
    }

    [Fact]
    public void ImportService_detects_formats()
    {
        var result = new ImportResult();
        ImportService.ImportText(Sample, result);
        ImportService.ImportText(PostmanEnvironmentConverter.Export(new EnvironmentModel { Name = "E" }), result);
        ImportService.ImportText("curl https://x.test", result);
        ImportService.ImportText("{\"foo\": 1}", result);

        Assert.Single(result.Collections);
        Assert.Single(result.Environments);
        Assert.Single(result.Requests);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Loosely_typed_flags_are_read_not_rejected()
    {
        const string json = """
            {
              "info": { "name": "Loose", "schema": 2.1 },
              "variable": [ { "key": "off", "value": "1", "disabled": "true" }, { "key": "on", "value": "2", "enabled": 1 } ],
              "item": [ {
                "name": "R",
                "protocolProfileBehavior": { "strictSSL": "false", "followRedirects": 0 },
                "request": { "method": "GET", "url": "https://x", "header": [ { "key": "H", "value": "v", "disabled": "false" } ] }
              } ]
            }
            """;

        var collection = PostmanCollectionImporter.Import(json);

        var request = Assert.IsType<HttpRequestModel>(Assert.Single(collection.Items));
        Assert.False(request.Settings.VerifySsl);
        Assert.False(request.Settings.FollowRedirects);
        Assert.True(Assert.Single(request.Headers).Enabled);
        Assert.False(collection.Variables.Single(v => v.Key == "off").Enabled);
        Assert.True(collection.Variables.Single(v => v.Key == "on").Enabled);
    }

    [Fact]
    public void OAuth2_access_token_round_trips()
    {
        var auth = new AuthConfig { Type = AuthType.OAuth2, OAuth2 = { GrantType = OAuth2GrantType.ClientCredentials, AccessToken = "tok" } };

        var back = PostmanCollectionImporter.ReadAuth(PostmanCollectionExporter.WriteAuth(auth))!;

        Assert.Equal("tok", back.OAuth2.AccessToken);
        Assert.Equal(OAuth2GrantType.ClientCredentials, back.OAuth2.GrantType);
    }

    [Fact]
    public void Importer_failures_become_import_errors()
    {
        var result = new ImportResult();

        // Repeated names make System.Text.Json throw ArgumentException when the object is read.
        ImportService.ImportText("""{ "info": { "name": "a", "name": "b" }, "item": [] }""", result, "dup.json");

        Assert.Empty(result.Collections);
        Assert.StartsWith("dup.json:", Assert.Single(result.Errors));
    }
}
