using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Secrets;
using Hermes.Core.Storage;

namespace Hermes.Core.Tests;

public class SecretTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid());
    private readonly InMemorySecretStore _store = new();

    private string ReadFile(params string[] parts) => File.ReadAllText(Path.Combine([_dir, .. parts]));

    [Fact]
    public void Secret_variables_stay_out_of_the_file_and_come_back_on_load()
    {
        var env = new EnvironmentModel
        {
            Name = "Staging",
            Variables =
            [
                new Variable("baseUrl", "https://api.test"),
                new Variable("apiKey", "initial-secret") { Type = VariableType.Secret, CurrentValue = "current-secret" },
            ],
        };
        new WorkspaceRepository(_dir, _store).SaveEnvironment(env);

        var file = ReadFile("environments", $"{env.Id}.json");
        Assert.Contains("https://api.test", file);
        Assert.DoesNotContain("initial-secret", file);
        Assert.DoesNotContain("current-secret", file);
        Assert.Equal("initial-secret", env.Variables[1].Value);

        var loaded = Assert.Single(new WorkspaceRepository(_dir, _store).LoadEnvironments()).Variables[1];
        Assert.Equal(("initial-secret", "current-secret"), (loaded.Value, loaded.CurrentValue));
    }

    [Fact]
    public void Literal_auth_credentials_move_to_the_store_but_variable_references_stay()
    {
        var request = new HttpRequestModel { Name = "Me", Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "tok-literal-123" } } };
        var collection = new CollectionModel
        {
            Name = "API",
            Auth = new AuthConfig { Type = AuthType.Basic, Basic = { Username = "ada", Password = "{{password}}" } },
            Items = [new FolderModel { Name = "Users", Items = [request] }],
        };
        collection.Auth.OAuth2.ClientSecret = "client-secret-xyz";
        new WorkspaceRepository(_dir, _store).SaveCollection(collection);

        var file = ReadFile("collections", $"{collection.Id}.json");
        Assert.DoesNotContain("tok-literal-123", file);
        Assert.DoesNotContain("client-secret-xyz", file);
        Assert.Contains("{{password}}", file);
        Assert.Contains("ada", file);

        var loaded = Assert.Single(new WorkspaceRepository(_dir, _store).LoadCollections());
        var loadedRequest = (HttpRequestModel)((FolderModel)loaded.Items[0]).Items[0];
        Assert.Equal("tok-literal-123", loadedRequest.Auth.Bearer.Token);
        Assert.Equal("client-secret-xyz", loaded.Auth.OAuth2.ClientSecret);
        Assert.Equal("{{password}}", loaded.Auth.Basic.Password);
    }

    [Fact]
    public void Without_a_keychain_secrets_are_written_as_plain_text()
    {
        var env = new EnvironmentModel { Variables = [new Variable("key", "plain-secret") { Type = VariableType.Secret }] };
        new WorkspaceRepository(_dir, SecretStore.Unprotected("no keyring")).SaveEnvironment(env);

        Assert.Contains("plain-secret", ReadFile("environments", $"{env.Id}.json"));
    }

    [Fact]
    public void A_failing_keychain_falls_back_to_plain_text_instead_of_losing_the_secret()
    {
        var store = new FailingStore();
        var env = new EnvironmentModel { Variables = [new Variable("key", "kept-secret") { Type = VariableType.Secret }] };
        new WorkspaceRepository(_dir, store).SaveEnvironment(env);

        Assert.False(store.IsProtected);
        Assert.Contains("kept-secret", ReadFile("environments", $"{env.Id}.json"));
    }

    [Fact]
    public void An_unreadable_keychain_is_never_overwritten()
    {
        var store = new UnreadableStore();
        var env = new EnvironmentModel { Variables = [new Variable("key", "local-secret") { Type = VariableType.Secret }] };
        new WorkspaceRepository(_dir, store).SaveEnvironment(env);

        Assert.False(store.IsProtected);
        Assert.False(store.Written);
        Assert.Contains("local-secret", ReadFile("environments", $"{env.Id}.json"));
    }

    [Fact]
    public void Making_a_variable_non_secret_or_deleting_its_owner_clears_the_store()
    {
        var repo = new WorkspaceRepository(_dir, _store);
        var env = new EnvironmentModel { Variables = [new Variable("key", "was-secret") { Type = VariableType.Secret }] };
        repo.SaveEnvironment(env);
        Assert.NotEmpty(_store.Get($"environment/{env.Id}"));

        env.Variables[0].Type = VariableType.Default;
        repo.SaveEnvironment(env);
        Assert.Empty(_store.Get($"environment/{env.Id}"));
        Assert.Contains("was-secret", ReadFile("environments", $"{env.Id}.json"));

        env.Variables[0].Type = VariableType.Secret;
        repo.SaveEnvironment(env);
        repo.DeleteEnvironment(env.Id);
        Assert.Empty(_store.Get($"environment/{env.Id}"));
    }

    [Fact]
    public void Session_drafts_keep_their_credentials_in_the_store()
    {
        var draft = new HttpRequestModel { Auth = new AuthConfig { Type = AuthType.ApiKey, ApiKey = { Key = "X-Key", Value = "draft-key-999" } } };
        new WorkspaceRepository(_dir, _store).SaveSession(new SessionState { Tabs = [new SessionTab { RequestDraft = draft }] });

        Assert.DoesNotContain("draft-key-999", ReadFile("session.json"));
        Assert.Equal("draft-key-999", new WorkspaceRepository(_dir, _store).LoadSession().Tabs[0].RequestDraft!.Auth.ApiKey.Value);
    }

    [Fact]
    public void History_is_written_without_credentials()
    {
        var entry = new HistoryEntry { Request = new HttpRequestModel { Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "history-token" } } } };
        new WorkspaceRepository(_dir, _store).AppendHistory(entry);

        var loaded = Assert.Single(new WorkspaceRepository(_dir, _store).LoadHistory(30));
        Assert.Equal("", loaded.Request.Auth.Bearer.Token);
        Assert.Equal("history-token", entry.Request.Auth.Bearer.Token);
    }

    [Fact]
    public void Sent_request_text_is_masked()
    {
        var request = new HttpRequestModel
        {
            Url = "https://api.test/items?key={{apiKey}}",
            Auth = new AuthConfig { Type = AuthType.Bearer, Bearer = { Token = "{{token}}" } },
        };
        var context = new RequestContext
        {
            Environment = new EnvironmentModel
            {
                Variables =
                [
                    new Variable("apiKey", "k3y/with space") { Type = VariableType.Secret },
                    new Variable("token", "bearer-token-value"),
                    new Variable("pin", "123") { Type = VariableType.Secret },
                ],
            },
        };
        var text = RawRequestFormatter.Format(RequestPreparer.Prepare(request, context)) + "\npin=123";

        var masked = SecretMasker.MaskRequestText(text, SecretMasker.CollectSecrets(request, context));

        Assert.Contains("Authorization: Bearer ••••••", masked);
        Assert.DoesNotContain("bearer-token-value", masked);
        Assert.DoesNotContain("k3y", masked);
        Assert.Contains("pin=123", masked);
    }

    [Theory]
    [InlineData("{{token}}", false)]
    [InlineData(" {{a}} {{b}} ", false)]
    [InlineData("Bearer {{token}}", true)]
    [InlineData("abc", true)]
    [InlineData("", false)]
    public void Only_literal_auth_values_count_as_secrets(string value, bool literal) =>
        Assert.Equal(literal, SecretFields.IsLiteral(value));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class UnreadableStore : SecretStore
    {
        public bool Written { get; private set; }

        protected override byte[] Read() => throw new IOException("keychain locked");

        protected override void Write(byte[] data) => Written = true;
    }

    private sealed class FailingStore : SecretStore
    {
        protected override byte[] Read() => [];

        protected override void Write(byte[] data) => throw new IOException("keychain locked");
    }
}
