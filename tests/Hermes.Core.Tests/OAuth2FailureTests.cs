using System.Net;
using System.Net.Sockets;
using Hermes.Core.Auth;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Variables;
using Hermes.TestSupport;

namespace Hermes.Core.Tests;

/// <summary>What the Auth panel is told when getting a token fails, in the words of the issue.</summary>
public sealed class OAuth2FailureTests : IDisposable
{
    private static readonly string[] TypeNames = ["Exception", "SocketException", "HttpRequestException", "HttpListenerException", "AuthenticationException"];

    private readonly OAuth2Service _oauth = new();

    private async Task<OAuth2TokenResult> GetTokenAsync(OAuth2Auth config, bool verifySsl = true, SslSettingSource sslSource = SslSettingSource.AppWide)
    {
        // A flow that wrongly starts waiting for the browser ends as a failed assertion, not as a hung test run.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        return await _oauth.GetTokenAsync(config, VariableScope.Empty, _ => Task.CompletedTask, verifySsl, limit.Token, sslSource);
    }

    private static OAuth2Auth ClientCredentials(string tokenUrl) =>
        new() { GrantType = OAuth2GrantType.ClientCredentials, AccessTokenUrl = tokenUrl, ClientId = "client" };

    /// <summary>The text the user reads without opening Details.</summary>
    private static string VisibleText(RequestFailure failure) => $"{failure.Headline} {failure.Facts} {failure.ActionCaption}";

    private static void AssertNoTypeNames(RequestFailure failure)
    {
        foreach (var typeName in TypeNames)
            Assert.DoesNotContain(typeName, VisibleText(failure));
    }

    [Fact]
    public async Task Unreachable_token_url_gets_the_same_wording_as_a_request()
    {
        var result = await GetTokenAsync(ClientCredentials($"http://127.0.0.1:{ClosedPort.Find()}/token"));

        var failure = Assert.IsType<RequestFailure>(result.Failure);
        Assert.Equal(RequestFailureKind.ConnectionRefused, failure.Kind);
        Assert.Equal("Server isn't accepting connections", failure.Headline);
        Assert.StartsWith("Nothing is listening on 127.0.0.1:", failure.Facts);
        AssertNoTypeNames(failure);
    }

    [Fact]
    public async Task Certificate_failure_for_an_app_wide_setting_points_to_the_settings()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());

        var result = await GetTokenAsync(ClientCredentials(server.Url + "token"));

        var failure = Assert.IsType<RequestFailure>(result.Failure);
        Assert.Equal(RequestFailureKind.CertificateSelfSigned, failure.Kind);
        Assert.Equal("Certificate is self-signed", failure.Headline);
        Assert.Equal("The app-wide SSL setting applies to token requests.", failure.ActionCaption);
        Assert.Equal([RequestFailureAction.OpenAppSettings], failure.Actions);
        AssertNoTypeNames(failure);
    }

    [Fact]
    public async Task Certificate_failure_for_a_request_override_points_to_the_request_settings()
    {
        using var server = new LocalHttpsServer(TestCertificates.SelfSigned());

        var result = await GetTokenAsync(ClientCredentials(server.Url + "token"), sslSource: SslSettingSource.Request);

        var failure = Assert.IsType<RequestFailure>(result.Failure);
        Assert.Equal("Token requests follow this request's SSL setting.", failure.ActionCaption);
        Assert.Equal([RequestFailureAction.OpenRequestSettings], failure.Actions);
    }

    // HttpListener binds only one loopback address for "localhost", so the port must be found taken on either.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Callback_port_in_use_asks_for_another_port(string holderAddress)
    {
        var holder = IPAddress.Parse(holderAddress);
        Assert.SkipUnless(holder.AddressFamily == AddressFamily.InterNetwork || Socket.OSSupportsIPv6, "This computer has no IPv6.");
        var taken = new TcpListener(holder, 0);
        taken.Start();
        try
        {
            var port = ((IPEndPoint)taken.LocalEndpoint).Port;
            var config = new OAuth2Auth
            {
                GrantType = OAuth2GrantType.AuthorizationCode,
                AuthUrl = "https://auth.test/authorize",
                AccessTokenUrl = "https://auth.test/token",
                ClientId = "client",
                RedirectUri = $"http://localhost:{port}/callback",
            };

            var result = await GetTokenAsync(config);

            var failure = Assert.IsType<RequestFailure>(result.Failure);
            Assert.Equal(RequestFailureKind.CallbackPortInUse, failure.Kind);
            Assert.Equal($"Port {port} is already in use", failure.Headline);
            Assert.Equal("Choose another callback port.", failure.Facts);
            Assert.Equal([RequestFailureAction.EditCallbackUrl], failure.Actions);
            AssertNoTypeNames(failure);
        }
        finally
        {
            taken.Stop();
        }
    }

    [Fact]
    public async Task Token_endpoint_error_is_shown_as_its_code_and_description()
    {
        using var server = new LocalHttpServer(async (context, stop) =>
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync("""{"error":"invalid_client","error_description":"Client authentication failed"}"""u8.ToArray(), stop);
            context.Response.Close();
        });

        var result = await GetTokenAsync(ClientCredentials(server.Url + "token"));

        var failure = Assert.IsType<RequestFailure>(result.Failure);
        Assert.Equal(RequestFailureKind.TokenRequestRefused, failure.Kind);
        Assert.Equal("The token server doesn't accept this client", failure.Headline);
        Assert.Contains("Client authentication failed", failure.Facts);
        Assert.DoesNotContain("{", VisibleText(failure));
        Assert.Contains(failure.Details, detail => detail is { Label: "Error code", Value: "invalid_client" });
        AssertNoTypeNames(failure);
    }

    [Fact]
    public async Task Token_endpoint_error_without_a_description_is_still_explained()
    {
        using var server = new LocalHttpServer(async (context, stop) =>
        {
            context.Response.StatusCode = 400;
            await context.Response.OutputStream.WriteAsync("""{"error":"invalid_client"}"""u8.ToArray(), stop);
            context.Response.Close();
        });

        var result = await GetTokenAsync(ClientCredentials(server.Url + "token"));

        var failure = Assert.IsType<RequestFailure>(result.Failure);
        Assert.Equal("The token server doesn't accept this client", failure.Headline);
        Assert.DoesNotContain("The server said", failure.Facts);
    }

    [Fact]
    public void Unknown_error_codes_keep_the_code_and_the_description_but_the_headline_is_generic()
    {
        var failure = OAuth2FailureDescriber.FromServerError("tenant_frozen", "Your <tenant> is frozen");

        Assert.Equal("The token server refused the request", failure.Headline);
        Assert.Contains("tenant_frozen", failure.Facts);
        Assert.Contains("Your <tenant> is frozen", failure.Facts);
    }

    [Fact]
    public void A_description_never_becomes_the_headline()
    {
        var failure = OAuth2FailureDescriber.FromServerError("invalid_grant", "Something the server wrote");

        Assert.DoesNotContain("Something the server wrote", failure.Headline);
    }

    [Fact]
    public void The_catch_all_hides_the_exception_under_details()
    {
        var failure = OAuth2FailureDescriber.FromUnexpected(new InvalidOperationException("Object reference not set to an instance of an object."));

        Assert.Equal(RequestFailureKind.UnexpectedTokenFailure, failure.Kind);
        Assert.Equal("Couldn't get a token", failure.Headline);
        Assert.EndsWith("Copy the details if you report this.", failure.Facts);
        Assert.DoesNotContain("Object reference", VisibleText(failure));
        Assert.Contains("Object reference not set", failure.OriginalMessage);
        Assert.Contains(failure.Details, detail => detail is { Label: "Error", Value: "InvalidOperationException" });
    }

    public void Dispose() => _oauth.Dispose();
}
