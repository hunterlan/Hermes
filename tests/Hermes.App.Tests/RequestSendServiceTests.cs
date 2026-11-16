using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;
using Hermes.TestSupport;

namespace Hermes.App.Tests;

/// <summary>What a send records: the response with the saved request, then the history entry, and nothing for a send that was cancelled or abandoned.</summary>
public class RequestSendServiceTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly HttpClientProvider _clients = new();
    private readonly RequestSendService _service;

    public RequestSendServiceTests()
    {
        _service = new RequestSendService(_temp.Workspace, new RequestExecutor(_clients));
    }

    public void Dispose()
    {
        _clients.Dispose();
        _temp.Dispose();
    }

    private static LocalHttpServer OkServer() => new(async (ctx, _) =>
    {
        await ctx.Response.OutputStream.WriteAsync("{\"ok\":true}"u8.ToArray());
        ctx.Response.Close();
    });

    private static LocalHttpServer NeverAnsweringServer() => new(async (ctx, stop) =>
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stop);
        ctx.Response.Close();
    });

    private HttpRequestModel SavedRequest(string url, Action<HttpRequestModel>? configure = null)
    {
        var collection = _temp.Workspace.CreateCollection("Pets");
        var request = new HttpRequestModel { Name = "List", Url = url };
        configure?.Invoke(request);
        _temp.Workspace.AddNode(collection, null, request);
        return request;
    }

    private static RequestSendInput InputFor(HttpRequestModel request) => new()
    {
        Model = request,
        Prepared = RequestPreparer.PrepareWithMasked(request, RequestContext.Empty),
        RequestId = request.Id,
    };

    [AvaloniaFact]
    public async Task A_response_is_saved_before_the_history_entry_is_written()
    {
        using var server = OkServer();
        var request = SavedRequest(server.Url);
        var savedWhenHistoryWasWritten = -1;
        _temp.Workspace.History.CollectionChanged += (_, _) =>
            savedWhenHistoryWasWritten = _temp.Workspace.LoadResponses(request.Id).Count;

        var outcome = await _service.SendAsync(InputFor(request), TestContext.Current.CancellationToken);

        Assert.Equal(1, savedWhenHistoryWasWritten);
        Assert.False(outcome.Cancelled);
        Assert.Equal(200, outcome.Result!.Response!.StatusCode);
        Assert.NotNull(outcome.SavedResponse);
        var entry = Assert.Single(_temp.Workspace.History);
        Assert.Equal(200, entry.StatusCode);
        Assert.Same(request, entry.Request);
    }

    [AvaloniaFact]
    public async Task The_saved_copy_of_the_sent_request_has_credentials_masked()
    {
        using var server = OkServer();
        var request = SavedRequest(server.Url, r => r.Headers = [new("Authorization", "Bearer hunter2-token")]);

        var outcome = await _service.SendAsync(InputFor(request), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("hunter2-token", outcome.SavedResponse!.SentRequest);
        Assert.Contains("Authorization", outcome.SavedResponse.SentRequest);
    }

    [AvaloniaFact]
    public async Task A_request_that_is_not_saved_keeps_no_response_but_still_gets_a_history_entry()
    {
        using var server = OkServer();
        var request = new HttpRequestModel { Url = server.Url };

        var outcome = await _service.SendAsync(InputFor(request), TestContext.Current.CancellationToken);

        Assert.Equal(200, outcome.Result!.Response!.StatusCode);
        Assert.Null(outcome.SavedResponse);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
        Assert.Single(_temp.Workspace.History);
    }

    [AvaloniaFact]
    public async Task A_failure_is_returned_and_recorded_in_history_without_a_saved_response()
    {
        var request = SavedRequest($"http://127.0.0.1:{ClosedPort.Find()}/");

        var outcome = await _service.SendAsync(InputFor(request), TestContext.Current.CancellationToken);

        Assert.False(outcome.Cancelled);
        Assert.Null(outcome.Result!.Response);
        Assert.NotNull(outcome.Result.Failure);
        Assert.Null(outcome.SavedResponse);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
        var entry = Assert.Single(_temp.Workspace.History);
        Assert.Null(entry.StatusCode);
        Assert.Equal(0, entry.DurationMs);
    }

    [AvaloniaFact]
    public async Task A_cancelled_send_writes_no_history_and_no_response()
    {
        using var server = NeverAnsweringServer();
        var request = SavedRequest(server.Url);
        using var cts = new CancellationTokenSource();

        var sending = _service.SendAsync(InputFor(request), cts.Token);
        await cts.CancelAsync();
        var outcome = await sending;

        Assert.True(outcome.Cancelled);
        Assert.Null(outcome.SavedResponse);
        Assert.Empty(_temp.Workspace.History);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
    }

    [AvaloniaFact]
    public async Task A_token_cancelled_after_the_response_arrived_but_before_recording_writes_nothing()
    {
        using var server = OkServer();
        var request = SavedRequest(server.Url);
        using var cts = new CancellationTokenSource();
        var executor = new RequestExecutor(_clients);
        // Cancels at the one point that matters: the response is in hand and nothing is recorded yet.
        var service = new RequestSendService(_temp.Workspace, async (prepared, token) =>
        {
            var result = await executor.ExecuteAsync(prepared, token);
            await cts.CancelAsync();
            return result;
        });

        var outcome = await service.SendAsync(InputFor(request), cts.Token);

        Assert.True(outcome.Cancelled);
        Assert.Null(outcome.Result);
        Assert.Null(outcome.SavedResponse);
        Assert.Empty(_temp.Workspace.History);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
    }
}
