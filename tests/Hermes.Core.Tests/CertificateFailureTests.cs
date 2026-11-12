using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.TestSupport;

namespace Hermes.Core.Tests;

/// <summary>Failed certificate checks against a local HTTPS server, and how they are worded.</summary>
public sealed class CertificateFailureTests : IDisposable
{
    private readonly HttpClientProvider _clients = new();
    private readonly List<LocalHttpsServer> _servers = [];

    private LocalHttpsServer StartServer(X509Certificate2 certificate)
    {
        var server = new LocalHttpsServer(certificate);
        _servers.Add(server);
        return server;
    }

    private Task<ExecutionResult> Send(string url, bool? verifySsl = null) =>
        new RequestExecutor(_clients).ExecuteAsync(
            RequestPreparer.Prepare(new HttpRequestModel { Url = url, Settings = { VerifySsl = verifySsl } }, RequestContext.Empty),
            TestContext.Current.CancellationToken);

    private static string Detail(RequestFailure failure, string label) => failure.Details.Single(detail => detail.Label == label).Value;

    [Fact]
    public async Task Expired_certificate_names_the_host_and_the_date()
    {
        var server = StartServer(TestCertificates.Expired());

        var failure = (await Send(server.Url)).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.CertificateExpired, failure.Kind);
        Assert.Equal("Certificate expired", failure.Headline);
        var expiry = DateTime.Now.AddYears(-2).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("The certificate for 127.0.0.1 expired on ", failure.Facts);
        Assert.Contains(expiry, failure.Facts);
        Assert.Equal("Expired Test Server", Detail(failure, "Issued to"));
        Assert.Contains("NotTimeValid", Detail(failure, "Check failed"));
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.TurnOffSslVerification, RequestFailureAction.OpenRequestSettings], failure.Actions);
    }

    [Fact]
    public async Task Self_signed_certificate_is_told_apart_from_an_unknown_authority()
    {
        var server = StartServer(TestCertificates.SelfSigned());

        var failure = (await Send(server.Url)).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.CertificateSelfSigned, failure.Kind);
        Assert.Equal("Certificate is self-signed", failure.Headline);
        Assert.Equal("Signed by the server itself, not by a trusted authority. Common for local and test servers.", failure.Facts);
        Assert.Equal("Self-Signed Test Server", Detail(failure, "Issued by"));
    }

    [Fact]
    public async Task Certificate_from_an_unknown_authority_names_the_issuer()
    {
        var server = StartServer(TestCertificates.FromUntrustedAuthority("Test Untrusted Authority"));

        var failure = (await Send(server.Url)).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.CertificateUntrustedAuthority, failure.Kind);
        Assert.Equal("Certificate isn't from a trusted authority", failure.Headline);
        Assert.Equal("Issued by Test Untrusted Authority, which this computer doesn't trust.", failure.Facts);
    }

    [Fact]
    public async Task Certificate_for_another_host_lists_its_names()
    {
        var server = StartServer(TestCertificates.ForOtherHost("other.example.test"));

        var failure = (await Send(server.Url)).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.CertificateNameMismatch, failure.Kind);
        Assert.Equal("Certificate is for a different address", failure.Headline);
        Assert.Equal("Issued for other.example.test, not 127.0.0.1. Check the URL.", failure.Facts);
        Assert.Contains("RemoteCertificateNameMismatch", Detail(failure, "Check failed"));
    }

    [Fact]
    public async Task Details_list_the_facts_and_leave_out_the_raw_certificate_message()
    {
        var server = StartServer(TestCertificates.SelfSigned());

        var failure = (await Send(server.Url)).Failure;

        Assert.NotNull(failure);
        Assert.True(failure.HasDetails);
        Assert.Null(failure.OriginalMessage);
        var text = failure.ToClipboardText();
        Assert.StartsWith("Certificate is self-signed", text);
        Assert.Contains("Issued by: Self-Signed Test Server", text);
        Assert.DoesNotContain("RemoteCertificateValidationCallback", text);
    }

    [Fact]
    public async Task Turning_verification_off_for_the_request_lets_it_through()
    {
        var server = StartServer(TestCertificates.SelfSigned());

        var result = await Send(server.Url, verifySsl: false);

        Assert.Null(result.Failure);
        Assert.Equal(LocalHttpsServer.ResponseBody, System.Text.Encoding.UTF8.GetString(result.Response!.Body));
    }

    [Fact]
    public async Task A_failure_is_reported_again_when_the_same_host_is_tried_twice()
    {
        var server = StartServer(TestCertificates.SelfSigned());

        await Send(server.Url);
        var second = (await Send(server.Url)).Failure;

        Assert.Equal(RequestFailureKind.CertificateSelfSigned, second?.Kind);
    }

    [Fact]
    public void Not_yet_valid_certificate_says_when_it_starts()
    {
        var failure = CertificateFailureDescriber.Describe(CreateFacts(chainStatus: X509ChainStatusFlags.NotTimeValid,
            notBefore: new DateTime(2031, 3, 4), notAfter: new DateTime(2032, 3, 4)), "bad", new DateTime(2026, 1, 1), "example.test");

        Assert.Equal(RequestFailureKind.CertificateExpired, failure.Kind);
        Assert.Equal("Certificate isn't valid yet", failure.Headline);
        Assert.Equal("The certificate for example.test isn't valid until 4 Mar 2031.", failure.Facts);
    }

    [Fact]
    public void Expired_certificate_in_the_chain_with_a_valid_leaf_is_not_called_expired_on_a_future_date()
    {
        var failure = CertificateFailureDescriber.Describe(CreateFacts(chainStatus: X509ChainStatusFlags.NotTimeValid,
            notBefore: new DateTime(2020, 1, 1), notAfter: new DateTime(2030, 1, 1)), "bad", new DateTime(2026, 1, 1), "example.test");

        Assert.Equal(RequestFailureKind.CertificateExpired, failure.Kind);
        Assert.Equal("A certificate in the chain has expired", failure.Headline);
        Assert.DoesNotContain("NotTimeValid", failure.Headline + failure.Facts);
        Assert.DoesNotContain("2030", failure.Facts);
    }

    [Fact]
    public void Failure_on_a_host_reached_by_redirect_says_so_and_does_not_claim_nothing_was_sent()
    {
        var failure = CertificateFailureDescriber.Describe(CreateFacts(chainStatus: X509ChainStatusFlags.UntrustedRoot),
            "bad", DateTime.Now, "first.example.test");

        Assert.StartsWith("The request was redirected to example.test. ", failure.Facts);
        Assert.False(failure.NothingWasSent);
    }

    [Fact]
    public async Task Redirect_to_a_server_with_a_bad_certificate_is_reported_for_that_server()
    {
        var secure = StartServer(TestCertificates.SelfSigned());
        using var redirecting = new LocalHttpServer((ctx, _) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = secure.Url.Replace("127.0.0.1", "localhost");
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        var failure = (await Send(redirecting.Url)).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.CertificateSelfSigned, failure.Kind);
        Assert.StartsWith("The request was redirected to localhost. ", failure.Facts);
        Assert.False(failure.NothingWasSent);
    }

    [Fact]
    public async Task Redirect_to_a_plain_http_port_names_the_target_host_and_offers_no_edit_url()
    {
        using var plain = new PlainHttpListener();
        using var redirecting = new LocalHttpServer((ctx, _) =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = $"https://localhost:{plain.Port}/";
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        var failure = (await Send(redirecting.Url)).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.PlainHttpAnswered, failure.Kind);
        Assert.Equal("The request was redirected to localhost. localhost answered, but not over a secure connection. "
            + "If it only serves plain HTTP, change the URL to http://.", failure.Facts);
        Assert.False(failure.NothingWasSent);
        Assert.Empty(failure.Actions);
    }

    [Fact]
    public async Task Requests_failing_together_on_one_host_are_all_explained()
    {
        var server = StartServer(TestCertificates.SelfSigned());

        var failures = await Task.WhenAll(Send(server.Url), Send(server.Url), Send(server.Url));

        Assert.All(failures, result => Assert.Equal(RequestFailureKind.CertificateSelfSigned, result.Failure?.Kind));
    }

    [Fact]
    public void The_failure_log_stays_bounded()
    {
        var log = new CertificateFailureLog();
        for (var number = 0; number < 200; number++)
            log.Record(CreateFacts() with { Host = $"host{number}.test" });

        Assert.NotEqual("host0.test", log.Find("host0.test", 0)?.Host);
        Assert.Equal("host199.test", log.Find("host199.test", 0)?.Host);
    }

    [Fact]
    public void Missing_certificate_gives_the_general_wording_and_keeps_the_message_in_details()
    {
        var failure = CertificateFailureDescriber.Describe(null, "The  handshake\nfailed", DateTime.Now, "example.test");

        Assert.Equal(RequestFailureKind.SecureConnectionFailed, failure.Kind);
        Assert.Equal("Secure connection failed", failure.Headline);
        Assert.Equal("Hermes and example.test couldn't agree on how to secure the connection. "
            + "The server may only support old TLS versions or ciphers this computer no longer allows.", failure.Facts);
        Assert.DoesNotContain("handshake", failure.Facts);
        Assert.True(failure.NothingWasSent);
        Assert.Equal("The  handshake\nfailed", failure.OriginalMessage);
        Assert.Empty(failure.Actions);
        Assert.Null(failure.ActionCaption);
    }

    [Fact]
    public void Authentication_failure_without_a_certificate_check_does_not_show_dotnet_wording()
    {
        var failure = CertificateFailureDescriber.Describe(null, "Authentication failed, see inner exception.", DateTime.Now, "old.example.test");

        Assert.Equal(RequestFailureKind.SecureConnectionFailed, failure.Kind);
        Assert.DoesNotContain("inner exception", failure.Facts);
        Assert.Equal("Authentication failed, see inner exception.", failure.OriginalMessage);
    }

    [Fact]
    public async Task Https_request_to_a_plain_http_port_says_the_server_didnt_answer_over_https()
    {
        using var listener = new PlainHttpListener();

        var failure = (await Send($"https://127.0.0.1:{listener.Port}/")).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.PlainHttpAnswered, failure.Kind);
        Assert.Equal("Server didn't answer over HTTPS", failure.Headline);
        Assert.Equal("127.0.0.1 answered, but not over a secure connection. If it only serves plain HTTP, change the URL to http://.", failure.Facts);
        Assert.True(failure.NothingWasSent);
        var visibleText = failure.Headline + " " + failure.Facts;
        Assert.DoesNotContain("frame", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", visibleText);
        Assert.Equal(CertificateFailureDescriber.PlainHttpAnsweredMessage, failure.OriginalMessage);
        Assert.Equal([RequestFailureAction.EditUrl], failure.Actions);
        Assert.StartsWith("HttpRequestException (SecureConnectionError) > AuthenticationException", Detail(failure, "Error"));
    }

    [Fact]
    public async Task Handshake_that_fails_without_a_certificate_check_shows_the_exception_chain_in_details()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        var failure = (await Send($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/")).Failure;

        Assert.NotNull(failure);
        Assert.Equal(RequestFailureKind.SecureConnectionFailed, failure.Kind);
        Assert.Equal("Secure connection failed", failure.Headline);
        Assert.DoesNotContain("inner exception", failure.Facts);
        Assert.Empty(failure.Actions);
        var chain = Detail(failure, "Error");
        Assert.StartsWith("HttpRequestException (SecureConnectionError) > ", chain);
        Assert.DoesNotContain("see inner exception", failure.OriginalMessage);
    }

    [Fact]
    public void Chain_problem_without_a_case_of_its_own_gives_the_general_failure_with_the_check_named()
    {
        var failure = CertificateFailureDescriber.Describe(CreateFacts(chainStatus: X509ChainStatusFlags.Revoked), "revoked", DateTime.Now, "example.test");

        Assert.Equal(RequestFailureKind.SecureConnectionFailed, failure.Kind);
        Assert.Equal("The certificate for example.test didn't pass a security check. Details say which one.", failure.Facts);
        Assert.Equal("Revoked", Detail(failure, "Check failed"));
    }

    [Fact]
    public void Many_names_are_shortened()
    {
        var names = Enumerable.Range(1, 8).Select(number => $"host{number}.test").ToList();
        var failure = CertificateFailureDescriber.Describe(CreateFacts(
            policyErrors: System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch, names: names), "bad", DateTime.Now, "example.test");

        Assert.Equal("Issued for host1.test, host2.test, host3.test, host4.test, host5.test and 3 more, not example.test. Check the URL.", failure.Facts);
    }

    [Fact]
    public void Non_certificate_failures_keep_their_message_as_a_general_failure()
    {
        var failure = RequestFailure.FromMessage("Request timed out after 200 ms.");

        Assert.Equal(RequestFailureKind.Other, failure.Kind);
        Assert.Equal("Request timed out after 200 ms.", failure.Facts);
        Assert.False(failure.NothingWasSent);
        Assert.Empty(failure.Actions);
        Assert.False(failure.HasDetails);
        Assert.Equal("Request failed\nRequest timed out after 200 ms.", failure.ToClipboardText().ReplaceLineEndings("\n"));
    }

    private static CertificateFacts CreateFacts(X509ChainStatusFlags chainStatus = X509ChainStatusFlags.NoError,
        System.Net.Security.SslPolicyErrors policyErrors = System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors,
        DateTime? notBefore = null, DateTime? notAfter = null, IReadOnlyList<string>? names = null) => new()
        {
            Host = "example.test",
            Subject = "example.test",
            AlternativeNames = names ?? ["example.test"],
            Issuer = "Some Authority",
            NotBefore = notBefore ?? new DateTime(2020, 1, 1),
            NotAfter = notAfter ?? new DateTime(2030, 1, 1),
            IsSelfSigned = false,
            PolicyErrors = policyErrors,
            ChainStatus = chainStatus,
        };

    public void Dispose()
    {
        foreach (var server in _servers)
            server.Dispose();
        _clients.Dispose();
    }
}
