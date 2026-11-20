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

    /// <summary>Cancels at the one point that matters: the response is in hand and nothing is recorded yet.</summary>
    private sealed class CancelAfterResponseExecutor(IRequestExecutor inner, CancellationTokenSource toCancel) : IRequestExecutor
    {
        public async Task<ExecutionResult> ExecuteAsync(PreparedRequest request, CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(request, cancellationToken);
            await toCancel.CancelAsync();
            return result;
        }
    }

    /// <summary>What sending and then recording returned.</summary>
    private sealed record SendAndRecordResult(ExecutionResult Result, SavedResponse? Saved);

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

    private async Task<HttpRequestModel> SavedRequestAsync(string url, Action<HttpRequestModel>? configure = null)
    {
        var collection = await _temp.Workspace.CreateCollectionAsync("Pets");
        var request = new HttpRequestModel { Name = "List", Url = url };
        configure?.Invoke(request);
        await _temp.Workspace.AddNodeAsync(collection, null, request);
        return request;
    }

    /// <summary>Sends <paramref name="request"/> and records it the way the tab does, keeping the response with <paramref name="keepResponseWith"/>.</summary>
    private async Task<SendAndRecordResult> SendAndRecordAsync(HttpRequestModel request, Guid? keepResponseWith)
    {
        var pair = RequestPreparer.PrepareWithMasked(request, RequestContext.Empty);
        var outcome = await _service.SendAsync(pair.Request, TestContext.Current.CancellationToken);
        Assert.False(outcome.Cancelled);
        var saved = _service.Record(new CompletedSend
        {
            Model = request,
            MaskedRequest = pair.Masked,
            Result = outcome.Result,
            KeepResponseWith = keepResponseWith,
        }, TestContext.Current.CancellationToken);
        return new SendAndRecordResult(outcome.Result, saved);
    }

    [AvaloniaFact]
    public async Task A_response_is_saved_before_the_history_entry_is_written()
    {
        using var server = OkServer();
        var request = await SavedRequestAsync(server.Url);
        var savedWhenHistoryWasWritten = -1;
        _temp.Workspace.History.CollectionChanged += (_, _) =>
            savedWhenHistoryWasWritten = _temp.Workspace.LoadResponses(request.Id).Count;

        var sent = await SendAndRecordAsync(request, request.Id);

        Assert.Equal(1, savedWhenHistoryWasWritten);
        Assert.Equal(200, sent.Result.Response!.StatusCode);
        Assert.NotNull(sent.Saved);
        var entry = Assert.Single(_temp.Workspace.History);
        Assert.Equal(200, entry.StatusCode);
        Assert.Same(request, entry.Request);
    }

    [AvaloniaFact]
    public async Task The_saved_copy_of_the_sent_request_has_credentials_masked()
    {
        using var server = OkServer();
        var request = await SavedRequestAsync(server.Url, r => r.Headers = [new("Authorization", "Bearer hunter2-token")]);

        var sent = await SendAndRecordAsync(request, request.Id);

        Assert.DoesNotContain("hunter2-token", sent.Saved!.SentRequest);
        Assert.Contains("Authorization", sent.Saved.SentRequest);
    }

    [AvaloniaFact]
    public async Task With_no_request_to_keep_it_with_no_response_is_saved_but_history_is_written()
    {
        using var server = OkServer();
        var request = new HttpRequestModel { Url = server.Url };

        var sent = await SendAndRecordAsync(request, keepResponseWith: null);

        Assert.Equal(200, sent.Result.Response!.StatusCode);
        Assert.Null(sent.Saved);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
        Assert.Single(_temp.Workspace.History);
    }

    [AvaloniaFact]
    public async Task The_response_is_kept_with_the_request_given_when_recording_not_the_one_sent()
    {
        using var server = OkServer();
        var sentAs = await SavedRequestAsync(server.Url);
        var savedAs = await SavedRequestAsync(server.Url);

        var sent = await SendAndRecordAsync(sentAs, savedAs.Id);

        Assert.NotNull(sent.Saved);
        Assert.Single(_temp.Workspace.LoadResponses(savedAs.Id));
        Assert.Empty(_temp.Workspace.LoadResponses(sentAs.Id));
    }

    [AvaloniaFact]
    public async Task A_failure_is_returned_and_recorded_in_history_without_a_saved_response()
    {
        var request = await SavedRequestAsync($"http://127.0.0.1:{ClosedPort.Find()}/");

        var sent = await SendAndRecordAsync(request, request.Id);

        Assert.Null(sent.Result.Response);
        Assert.NotNull(sent.Result.Failure);
        Assert.Null(sent.Saved);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
        var entry = Assert.Single(_temp.Workspace.History);
        Assert.Null(entry.StatusCode);
        Assert.Equal(0, entry.DurationMs);
    }

    [AvaloniaFact]
    public async Task A_send_cancelled_in_flight_comes_back_cancelled()
    {
        using var server = NeverAnsweringServer();
        var request = await SavedRequestAsync(server.Url);
        using var cts = new CancellationTokenSource();

        var sending = _service.SendAsync(RequestPreparer.Prepare(request, RequestContext.Empty), cts.Token);
        await cts.CancelAsync();
        var outcome = await sending;

        Assert.True(outcome.Cancelled);
        Assert.Null(outcome.Result);
    }

    [AvaloniaFact]
    public async Task A_token_cancelled_after_the_response_arrived_makes_the_send_cancelled()
    {
        using var server = OkServer();
        var request = await SavedRequestAsync(server.Url);
        using var cts = new CancellationTokenSource();
        var service = new RequestSendService(_temp.Workspace, new CancelAfterResponseExecutor(new RequestExecutor(_clients), cts));

        var outcome = await service.SendAsync(RequestPreparer.Prepare(request, RequestContext.Empty), cts.Token);

        Assert.True(outcome.Cancelled);
        Assert.Null(outcome.Result);
    }

    [AvaloniaFact]
    public async Task Recording_with_a_cancelled_token_writes_no_history_and_no_response()
    {
        using var server = OkServer();
        var request = await SavedRequestAsync(server.Url);
        var pair = RequestPreparer.PrepareWithMasked(request, RequestContext.Empty);
        var outcome = await _service.SendAsync(pair.Request, TestContext.Current.CancellationToken);
        Assert.False(outcome.Cancelled);

        var saved = _service.Record(new CompletedSend
        {
            Model = request,
            MaskedRequest = pair.Masked,
            Result = outcome.Result,
            KeepResponseWith = request.Id,
        }, new CancellationToken(canceled: true));

        Assert.Null(saved);
        Assert.Empty(_temp.Workspace.History);
        Assert.Empty(_temp.Workspace.LoadResponses(request.Id));
    }
}
