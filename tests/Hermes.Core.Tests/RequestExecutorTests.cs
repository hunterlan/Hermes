using System.Net;
using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.TestSupport;

namespace Hermes.Core.Tests;

/// <summary>Runs against a tiny local HttpListener, so the real handler pipeline is exercised.</summary>
public sealed class RequestExecutorTests : IDisposable
{
    private readonly LocalHttpServer _server;
    private readonly string _baseUrl;
    private readonly HttpClientProvider _clients = new();

    public RequestExecutorTests()
    {
        _server = new LocalHttpServer(ServeAsync);
        _baseUrl = _server.Url;
    }

    private async Task ServeAsync(HttpListenerContext ctx, CancellationToken stop)
    {
        var path = ctx.Request.Url!.AbsolutePath;
        if (path == "/delay")
            await Task.Delay(5000, stop);
        if (path == "/slow")
            await Task.Delay(300, stop);
        if (path == "/redirect")
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = _baseUrl + "echo";
            ctx.Response.Close();
            return;
        }
        if (path == "/cookie")
            ctx.Response.AppendHeader("Set-Cookie", "session=abc; Path=/");

        using var reader = new StreamReader(ctx.Request.InputStream);
        var body = await reader.ReadToEndAsync(stop);
        var echo = $"{ctx.Request.HttpMethod} {ctx.Request.Url!.PathAndQuery}\nX-Test: {ctx.Request.Headers["X-Test"]}\nContent-Type: {ctx.Request.ContentType}\n{body}";
        var bytes = Encoding.UTF8.GetBytes(echo);
        ctx.Response.StatusCode = 201;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.Response.OutputStream.WriteAsync(bytes, stop);
        ctx.Response.Close();
    }

    private Task<ExecutionResult> Send(HttpRequestModel request) => SendCancellable(request, TestContext.Current.CancellationToken);

    private Task<ExecutionResult> SendCancellable(HttpRequestModel request, CancellationToken ct) =>
        new RequestExecutor(_clients).ExecuteAsync(RequestPreparer.Prepare(request, RequestContext.Empty), ct);

    [Fact]
    public async Task Sends_method_headers_and_raw_body()
    {
        var result = await Send(new HttpRequestModel
        {
            Method = "PUT",
            Url = _baseUrl + "echo?a=1",
            Headers = [new KeyValueItem("X-Test", "yes")],
            Body = { Mode = BodyMode.Raw, Raw = "{\"x\":1}", RawLanguage = RawLanguage.Json },
        });

        Assert.Null(result.Error);
        var response = result.Response!;
        Assert.Equal(201, response.StatusCode);
        var text = Encoding.UTF8.GetString(response.Body);
        Assert.StartsWith("PUT /echo?a=1", text);
        Assert.Contains("X-Test: yes", text);
        Assert.Contains("Content-Type: application/json", text);
        Assert.EndsWith("{\"x\":1}", text);
        Assert.Equal("text/plain", response.ContentType);
        Assert.True(response.TotalTime >= response.TimeToFirstByte);
    }

    [Fact]
    public async Task Sends_urlencoded_form()
    {
        var result = await Send(new HttpRequestModel
        {
            Method = "POST",
            Url = _baseUrl + "echo",
            Body = { Mode = BodyMode.UrlEncoded, UrlEncoded = [new KeyValueItem("a b", "c&d"), new KeyValueItem("off", "x", enabled: false)] },
        });
        Assert.EndsWith("a%20b=c%26d", Encoding.UTF8.GetString(result.Response!.Body));
    }

    [Fact]
    public async Task Sends_multipart_form()
    {
        var result = await Send(new HttpRequestModel
        {
            Method = "POST",
            Url = _baseUrl + "echo",
            Body = { Mode = BodyMode.FormData, FormData = [new KeyValueItem("field", "value")] },
        });
        var text = Encoding.UTF8.GetString(result.Response!.Body);
        Assert.Contains("multipart/form-data; boundary=", text);
        Assert.Contains("name=\"field\"", text);
    }

    [Fact]
    public async Task Redirects_follow_setting()
    {
        var followed = await Send(new HttpRequestModel { Url = _baseUrl + "redirect" });
        Assert.Equal(201, followed.Response!.StatusCode);

        var notFollowed = await Send(new HttpRequestModel { Url = _baseUrl + "redirect", Settings = { FollowRedirects = false } });
        Assert.Equal(302, notFollowed.Response!.StatusCode);
    }

    [Fact]
    public async Task Stores_cookies()
    {
        var result = await Send(new HttpRequestModel { Url = _baseUrl + "cookie" });
        Assert.Contains(result.Response!.Cookies, c => c.Name == "session" && c.Value == "abc");
        Assert.NotEmpty(_clients.GetAllCookies());
    }

    [Fact]
    public async Task Clearing_cookies_leaves_requests_in_flight_alone()
    {
        await Send(new HttpRequestModel { Url = _baseUrl + "cookie" });

        var inFlight = Send(new HttpRequestModel { Url = _baseUrl + "slow" });
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _clients.ClearCookies();
        var result = await inFlight;

        Assert.Null(result.Error);
        Assert.Equal(201, result.Response!.StatusCode);
        Assert.Empty(_clients.GetAllCookies());
    }

    [Fact]
    public async Task Files_already_opened_are_closed_when_a_later_file_is_missing()
    {
        var dir = Directory.CreateTempSubdirectory("hermes-multipart-");
        var present = Path.Combine(dir.FullName, "a.txt");
        File.WriteAllText(present, "a");

        var result = await Send(new HttpRequestModel
        {
            Method = "POST",
            Url = _baseUrl + "echo",
            Body =
            {
                Mode = BodyMode.FormData,
                FormData =
                [
                    new KeyValueItem("a", present) { Kind = KeyValueKind.File },
                    new KeyValueItem("b", Path.Combine(dir.FullName, "missing.txt")) { Kind = KeyValueKind.File },
                ],
            },
        });

        Assert.NotNull(result.Error);
        // Exclusive access fails while any handle to the file is still open.
        using (File.Open(present, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        dir.Delete(recursive: true);
    }

    [Fact]
    public async Task Cancellation_and_timeout()
    {
        using var cts = new CancellationTokenSource(200);
        var cancelled = await SendCancellable(new HttpRequestModel { Url = _baseUrl + "delay" }, cts.Token);
        Assert.True(cancelled.Cancelled);

        var timedOut = await Send(new HttpRequestModel { Url = _baseUrl + "delay", Settings = { TimeoutMs = 200 } });
        Assert.Contains("timed out", timedOut.Error);
    }

    [Fact]
    public async Task Invalid_url_is_an_error_not_an_exception()
    {
        var result = await Send(new HttpRequestModel { Url = "http://" });
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Connection_failure_is_reported()
    {
        using var port = new ResettingPort();
        var result = await Send(new HttpRequestModel { Url = $"http://127.0.0.1:{port.Port}/" });
        Assert.NotNull(result.Error);
        Assert.Null(result.Response);
    }

    public void Dispose()
    {
        _server.Dispose();
        _clients.Dispose();
    }
}
