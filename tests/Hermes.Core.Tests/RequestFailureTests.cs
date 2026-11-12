using System.Net;
using System.Net.Sockets;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.TestSupport;

namespace Hermes.Core.Tests;

/// <summary>How requests that fail before a response are worded, and that it comes from the failure, not its message.</summary>
public sealed class RequestFailureTests : IDisposable
{
    private readonly HttpClientProvider _clients = new();

    private Task<ExecutionResult> Send(HttpRequestModel request) =>
        new RequestExecutor(_clients).ExecuteAsync(RequestPreparer.Prepare(request, RequestContext.Empty), TestContext.Current.CancellationToken);

    private async Task<RequestFailure> SendExpectingFailure(HttpRequestModel request)
    {
        var result = await Send(request);
        Assert.Null(result.Response);
        return Assert.IsType<RequestFailure>(result.Failure);
    }

    // ---- network ----

    [Fact]
    public async Task Unresolvable_host_names_the_host_and_offers_to_edit_the_url()
    {
        var failure = await SendExpectingFailure(new HttpRequestModel { Url = "http://no-such-host.invalid/path" });

        Assert.Equal(RequestFailureKind.NameNotResolved, failure.Kind);
        Assert.Equal("Couldn't find the server", failure.Headline);
        Assert.Equal("no-such-host.invalid doesn't resolve. Check the URL or your network or VPN.", failure.Facts);
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.EditUrl], failure.Actions);
        Assert.Contains("NameResolutionError", failure.Details.Single(detail => detail.Label == "Error").Value);
    }

    [Fact]
    public async Task Closed_port_names_host_and_port_and_offers_retry()
    {
        var port = ClosedPort.Find();

        var failure = await SendExpectingFailure(new HttpRequestModel { Url = $"http://localhost:{port}/" });

        Assert.Equal(RequestFailureKind.ConnectionRefused, failure.Kind);
        Assert.Equal("Server isn't accepting connections", failure.Headline);
        Assert.Equal($"Nothing is listening on localhost:{port}. Is the server running?", failure.Facts);
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.Retry], failure.Actions);
    }

    [Fact]
    public async Task Connection_reset_by_the_server_says_it_was_closed()
    {
        using var port = new ResettingPort();

        var failure = await SendExpectingFailure(new HttpRequestModel { Url = $"http://127.0.0.1:{port.Port}/" });

        Assert.Equal(RequestFailureKind.ConnectionClosed, failure.Kind);
        Assert.Equal("Connection was closed by the server", failure.Headline);
        Assert.Equal("The server closed the connection before the response finished.", failure.Facts);
        Assert.False(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.Retry], failure.Actions);
    }

    [Fact]
    public async Task Failure_after_a_redirect_names_the_redirect_target_and_says_a_request_went_out()
    {
        var port = ClosedPort.Find();
        using var redirecting = new LocalHttpServer((context, _) =>
        {
            context.Response.StatusCode = 302;
            context.Response.RedirectLocation = $"http://127.0.0.1:{port}/moved";
            context.Response.Close();
            return Task.CompletedTask;
        });

        var failure = await SendExpectingFailure(new HttpRequestModel { Url = redirecting.Url });

        Assert.Equal(RequestFailureKind.ConnectionRefused, failure.Kind);
        Assert.Equal($"The request was redirected to 127.0.0.1. Nothing is listening on 127.0.0.1:{port}. Is the server running?", failure.Facts);
        Assert.False(failure.NothingWasSent);
    }

    [Fact]
    public async Task Original_message_is_kept_once_in_the_details_and_the_copied_text()
    {
        var port = ClosedPort.Find();

        var failure = await SendExpectingFailure(new HttpRequestModel { Url = $"http://127.0.0.1:{port}/" });

        Assert.False(string.IsNullOrWhiteSpace(failure.OriginalMessage));
        Assert.True(failure.HasDetails);
        // .NET repeats "Connection refused" in the outer and the inner exception; the copy keeps it once.
        var copied = failure.ToClipboardText();
        Assert.Contains(failure.OriginalMessage, copied);
        Assert.Equal(1, CountOccurrences(copied, failure.OriginalMessage));
    }

    private static int CountOccurrences(string text, string part) => text.Split(part).Length - 1;

    // ---- classification by type and code, never by message ----

    [Theory]
    [InlineData(SocketError.HostNotFound, NetworkFailureCause.NameNotResolved)]
    [InlineData(SocketError.TryAgain, NetworkFailureCause.NameNotResolved)]
    [InlineData(SocketError.ConnectionRefused, NetworkFailureCause.ConnectionRefused)]
    [InlineData(SocketError.ConnectionReset, NetworkFailureCause.ConnectionClosed)]
    [InlineData(SocketError.ConnectionAborted, NetworkFailureCause.ConnectionClosed)]
    [InlineData(SocketError.NetworkUnreachable, NetworkFailureCause.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable, NetworkFailureCause.NetworkUnreachable)]
    [InlineData(SocketError.TimedOut, NetworkFailureCause.ConnectionTimedOut)]
    [InlineData(SocketError.AccessDenied, NetworkFailureCause.Unknown)]
    public void Socket_errors_are_classified_by_code_whatever_the_message_says(SocketError code, NetworkFailureCause expected)
    {
        var exception = new HttpRequestException(HttpRequestError.ConnectionError, "unrelated words", new SocketException((int)code));

        Assert.Equal(expected == NetworkFailureCause.Unknown ? NetworkFailureCause.ConnectionFailed : expected, NetworkFailureClassifier.Classify(exception));
    }

    [Fact]
    public void Http_request_errors_are_classified_by_their_error_value()
    {
        Assert.Equal(NetworkFailureCause.NameNotResolved,
            NetworkFailureClassifier.Classify(new HttpRequestException(HttpRequestError.NameResolutionError, "x")));
        Assert.Equal(NetworkFailureCause.ConnectionClosed,
            NetworkFailureClassifier.Classify(new HttpIOException(HttpRequestError.ResponseEnded, "x")));
        Assert.Equal(NetworkFailureCause.SecureConnectionFailed,
            NetworkFailureClassifier.Classify(new HttpRequestException(HttpRequestError.SecureConnectionError, "x")));
        Assert.Equal(NetworkFailureCause.Unknown,
            NetworkFailureClassifier.Classify(new HttpRequestException(HttpRequestError.InvalidResponse, "Connection refused")));
    }

    [Fact]
    public void A_socket_error_inside_an_io_exception_is_found()
    {
        var exception = new IOException("Unable to read data from the transport connection", new SocketException((int)SocketError.ConnectionReset));

        Assert.Equal(NetworkFailureCause.ConnectionClosed, NetworkFailureClassifier.Classify(exception));
        Assert.Equal("IOException > SocketException (ConnectionReset)", NetworkFailureClassifier.DescribeChain(exception));
    }

    [Fact]
    public void Messages_that_repeat_an_inner_message_are_joined_without_the_repeat()
    {
        var exception = new HttpRequestException("Connection refused (localhost:5000)", new SocketException((int)SocketError.ConnectionRefused));

        // The inner message is the OS's own wording, so it can differ; what matters is a repeated one is dropped.
        var inner = exception.InnerException!.Message;
        var repeating = new HttpRequestException($"{inner} (localhost:5000) ({inner})", new InvalidOperationException(inner));
        Assert.Equal($"{inner} (localhost:5000) ({inner})", ExceptionText.Flatten(repeating));
    }

    // ---- building the request ----

    [Theory]
    [InlineData("", "The URL is empty.")]
    [InlineData("   ", "The URL is empty.")]
    [InlineData("http://", "The URL has no host name.")]
    [InlineData("ftp://example.com/file", "The scheme ftp:// isn't supported. Use http:// or https://.")]
    [InlineData("{{baseUrl}}/users", "The URL still has a variable that isn't resolved: {{baseUrl}}. Set it in the active environment.")]
    public async Task Invalid_url_says_what_is_wrong_and_offers_to_edit_it(string url, string expectedFacts)
    {
        var failure = await SendExpectingFailure(new HttpRequestModel { Url = url });

        Assert.Equal(RequestFailureKind.InvalidUrl, failure.Kind);
        Assert.Equal("URL isn't valid", failure.Headline);
        Assert.Equal(expectedFacts, failure.Facts);
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.EditUrl], failure.Actions);
    }

    [Fact]
    public async Task Missing_body_file_names_the_path_and_offers_to_choose_another()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hermes-missing-{Guid.NewGuid():N}", "payload.bin");

        var failure = await SendExpectingFailure(new HttpRequestModel { Method = "POST", Url = "http://127.0.0.1:1/", Body = { Mode = BodyMode.Binary, BinaryFilePath = path } });

        Assert.Equal(RequestFailureKind.BodyFileUnreadable, failure.Kind);
        Assert.Equal("Body file can't be read", failure.Headline);
        Assert.Equal($"{path} doesn't exist.", failure.Facts);
        Assert.Equal(path, failure.Subject);
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.ChooseFile], failure.Actions);
        Assert.False(string.IsNullOrWhiteSpace(failure.OriginalMessage));
    }

    [Fact]
    public async Task Missing_form_data_file_is_reported_like_a_missing_binary_body()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hermes-missing-{Guid.NewGuid():N}.txt");

        var failure = await SendExpectingFailure(new HttpRequestModel
        {
            Method = "POST",
            Url = "http://127.0.0.1:1/",
            Body = { Mode = BodyMode.FormData, FormData = [new KeyValueItem("upload", path) { Kind = KeyValueKind.File }] },
        });

        Assert.Equal(RequestFailureKind.BodyFileUnreadable, failure.Kind);
        Assert.Equal($"{path} doesn't exist.", failure.Facts);
        Assert.Equal(path, failure.Subject);
    }

    [Fact]
    public async Task Body_path_that_is_a_folder_says_so()
    {
        var folder = Directory.CreateTempSubdirectory("hermes-body-folder-");
        try
        {
            var failure = await SendExpectingFailure(new HttpRequestModel { Method = "POST", Url = "http://127.0.0.1:1/", Body = { Mode = BodyMode.Binary, BinaryFilePath = folder.FullName } });

            Assert.Equal(RequestFailureKind.BodyFileUnreadable, failure.Kind);
            Assert.Equal($"{folder.FullName} is a folder, not a file.", failure.Facts);
        }
        finally
        {
            folder.Delete();
        }
    }

    [Fact]
    public async Task Header_name_with_a_space_names_the_header_and_offers_to_show_headers()
    {
        var failure = await SendExpectingFailure(new HttpRequestModel { Url = "http://127.0.0.1:1/", Headers = [new KeyValueItem("Bad Name", "x")] });

        Assert.Equal(RequestFailureKind.InvalidHeader, failure.Kind);
        Assert.Equal("Header name isn't valid", failure.Headline);
        Assert.Contains("\"Bad Name\"", failure.Facts);
        Assert.Equal("Bad Name", failure.Subject);
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.ShowHeaders], failure.Actions);
    }

    [Fact]
    public void Header_value_that_is_rejected_names_header_and_value()
    {
        var exception = new InvalidHeaderException("Content-Type", "abc", InvalidHeaderProblem.Value);

        var failure = RequestBuildFailureDescriber.Describe(exception, new PreparedRequest());

        Assert.Equal(RequestFailureKind.InvalidHeader, failure.Kind);
        Assert.Equal("Header value isn't valid", failure.Headline);
        Assert.Equal("Header Content-Type has a value that can't be sent: abc", failure.Facts);
        Assert.Equal([RequestFailureAction.ShowHeaders], failure.Actions);
    }

    [Theory]
    [InlineData("example.com/users")]
    [InlineData("localhost:5000/api")]
    public void Url_without_a_scheme_is_told_to_start_with_http(string url)
    {
        var failure = RequestBuildFailureDescriber.Describe(new UriFormatException(), new PreparedRequest { Url = url });

        Assert.Equal("The URL doesn't start with http:// or https://.", failure.Facts);
    }

    [Theory]
    [InlineData("abc\r\nInjected: yes", "abc\\r\\nInjected: yes")]
    [InlineData("abc\ndef", "abc\\ndef")]
    public async Task Header_value_with_a_line_break_is_rejected_before_sending(string value, string shown)
    {
        var failure = await SendExpectingFailure(new HttpRequestModel { Url = "http://127.0.0.1:1/", Headers = [new KeyValueItem("X-Test", value)] });

        Assert.Equal(RequestFailureKind.InvalidHeader, failure.Kind);
        Assert.Equal("Header value isn't valid", failure.Headline);
        Assert.Equal($"Header X-Test has a value that can't be sent: {shown}", failure.Facts);
        Assert.True(failure.NothingWasSent);
        Assert.Equal([RequestFailureAction.ShowHeaders], failure.Actions);
    }

    [Fact]
    public async Task Form_data_file_opened_before_a_missing_one_is_released()
    {
        var folder = Directory.CreateTempSubdirectory("hermes-form-files-");
        try
        {
            var present = Path.Combine(folder.FullName, "a.txt");
            File.WriteAllText(present, "a");
            var missing = Path.Combine(folder.FullName, "missing.txt");

            var failure = await SendExpectingFailure(new HttpRequestModel
            {
                Method = "POST",
                Url = "http://127.0.0.1:1/",
                Body =
                {
                    Mode = BodyMode.FormData,
                    FormData = [new KeyValueItem("a", present) { Kind = KeyValueKind.File }, new KeyValueItem("b", missing) { Kind = KeyValueKind.File }],
                },
            });

            Assert.Equal(RequestFailureKind.BodyFileUnreadable, failure.Kind);
            Assert.Equal(missing, failure.Subject);
            // Exclusive access fails while any handle to the file is still open.
            using (File.Open(present, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Binary_file_opened_before_a_bad_header_is_released()
    {
        var folder = Directory.CreateTempSubdirectory("hermes-binary-file-");
        try
        {
            var present = Path.Combine(folder.FullName, "payload.bin");
            File.WriteAllText(present, "a");

            var failure = await SendExpectingFailure(new HttpRequestModel
            {
                Method = "POST",
                Url = "http://127.0.0.1:1/",
                Headers = [new KeyValueItem("Bad Name", "x")],
                Body = { Mode = BodyMode.Binary, BinaryFilePath = present },
            });

            Assert.Equal(RequestFailureKind.InvalidHeader, failure.Kind);
            using (File.Open(present, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Header_value_outside_ascii_is_reported_and_a_credential_value_is_not_repeated()
    {
        var plain = await SendExpectingFailure(new HttpRequestModel { Url = "http://127.0.0.1:1/", Headers = [new KeyValueItem("X-Name", "héllo")] });
        var credential = await SendExpectingFailure(new HttpRequestModel { Url = "http://127.0.0.1:1/", Headers = [new KeyValueItem("X-Api-Key", "sëcret")] });

        Assert.Equal("Header X-Name has characters outside ASCII, which a header can't carry: héllo", plain.Facts);
        Assert.DoesNotContain("sëcret", credential.ToClipboardText());
        Assert.Contains("X-Api-Key", credential.Facts);
    }

    [Fact]
    public async Task A_valid_request_with_headers_is_still_sent()
    {
        using var server = new LocalHttpServer(async (context, _) =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.Close();
            await Task.CompletedTask;
        });

        var result = await Send(new HttpRequestModel { Url = server.Url, Headers = [new KeyValueItem("Accept", "text/plain"), new KeyValueItem("Content-Type", "text/plain")] });

        Assert.Null(result.Failure);
        Assert.Equal(200, result.Response!.StatusCode);
    }

    [Fact]
    public void Connect_timeout_of_the_handler_is_classified_as_the_server_not_answering()
    {
        var exception = new TaskCanceledException("A task was canceled.", new TimeoutException("The operation was canceled."));

        Assert.Equal(NetworkFailureCause.ConnectionTimedOut, NetworkFailureClassifier.Classify(exception));
    }

    [Fact]
    public async Task Connection_that_is_never_accepted_is_a_connection_failure_not_a_request_timeout()
    {
        // 10.255.255.1 is a private address nothing answers on: the connect hangs until the handler's own
        // connect timeout (kept short here) gives up, or the network refuses at once. Neither is the request's timer.
        using var clients = new HttpClientProvider(connectTimeout: TimeSpan.FromMilliseconds(300));
        var executor = new RequestExecutor(clients);
        var request = RequestPreparer.Prepare(new HttpRequestModel { Url = "http://10.255.255.1/", Settings = { TimeoutMs = 0 } }, RequestContext.Empty);

        var failure = (await executor.ExecuteAsync(request, TestContext.Current.CancellationToken)).Failure;

        Assert.NotNull(failure);
        Assert.DoesNotContain("timed out after", failure.Facts);
        Assert.Equal([RequestFailureAction.Retry], failure.Actions);
        Assert.Contains("10.255.255.1:80", failure.Details.Single(detail => detail.Label == "Server").Value);

        // Some networks refuse the address at once; then the connect timeout was never involved.
        if (!failure.Details.Single(detail => detail.Label == "Error").Value.Contains(nameof(TimeoutException)))
            Assert.Skip("The network answered 10.255.255.1 at once, so the connect timeout path was not exercised.");
        Assert.Equal(RequestFailureKind.ConnectionFailed, failure.Kind);
    }

    public void Dispose() => _clients.Dispose();
}
