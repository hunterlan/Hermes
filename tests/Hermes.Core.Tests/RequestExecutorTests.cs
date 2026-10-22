using System.Net;
using System.Net.Sockets;
using System.Text;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Tests;

/// <summary>Runs against a tiny local HttpListener, so the real handler pipeline is exercised.</summary>
public sealed class RequestExecutorTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _baseUrl;
    private readonly HttpClientProvider _clients = new();
    private readonly CancellationTokenSource _stop = new();

    public RequestExecutorTests()
    {
        var port = FreePort();
        _baseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }

            var path = ctx.Request.Url!.AbsolutePath;
            if (path == "/delay")
            {
                try { await Task.Delay(5000, _stop.Token); } catch { }
            }
            if (path == "/redirect")
            {
                ctx.Response.StatusCode = 302;
                ctx.Response.RedirectLocation = _baseUrl + "echo";
                ctx.Response.Close();
                continue;
            }
            if (path == "/cookie")
                ctx.Response.AppendHeader("Set-Cookie", "session=abc; Path=/");

            using var reader = new StreamReader(ctx.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            var echo = $"{ctx.Request.HttpMethod} {ctx.Request.Url!.PathAndQuery}\nX-Test: {ctx.Request.Headers["X-Test"]}\nContent-Type: {ctx.Request.ContentType}\n{body}";
            var bytes = Encoding.UTF8.GetBytes(echo);
            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            try
            {
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
            catch { }
        }
    }

    private Task<ExecutionResult> Send(HttpRequestModel request, CancellationToken ct = default) =>
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
    public async Task Cancellation_and_timeout()
    {
        using var cts = new CancellationTokenSource(200);
        var cancelled = await Send(new HttpRequestModel { Url = _baseUrl + "delay" }, cts.Token);
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
        var result = await Send(new HttpRequestModel { Url = $"http://127.0.0.1:{FreePort()}/" });
        Assert.NotNull(result.Error);
        Assert.Null(result.Response);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
        _clients.Dispose();
    }
}
