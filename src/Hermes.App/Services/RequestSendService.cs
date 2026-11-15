using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Secrets;

namespace Hermes.App.Services;

/// <summary>
/// Sends a prepared request, then keeps its masked response with the saved request and writes the history entry,
/// in that order. The tab decides what to show; this decides what is recorded.
/// </summary>
public sealed class RequestSendService(WorkspaceService workspace, RequestExecutor executor)
{
    public async Task<RequestSendOutcome> SendAsync(RequestSendInput input, CancellationToken cancellationToken)
    {
        var result = await executor.ExecuteAsync(input.Prepared.Request, cancellationToken);
        // A tab closed during the send leaves no response and no history behind.
        if (input.IsAbandoned?.Invoke() == true)
            return RequestSendOutcome.AbandonedSend;

        // Responses are kept with saved requests, so reopening shows the last result and earlier ones.
        // The saved copy has credentials masked; the one on screen stays exact.
        SavedResponse? saved = null;
        if (!result.Cancelled && result.Response is { } response && workspace.Find(input.RequestId) is not null)
        {
            saved = workspace.SaveResponse(input.RequestId, new SavedResponse
            {
                Response = response,
                SentRequest = SecretMasker.MaskCredentialHeaders(RawRequestFormatter.Format(input.Prepared.Masked)),
            });
        }

        if (!result.Cancelled)
        {
            workspace.AddHistory(new HistoryEntry
            {
                Request = input.Model,
                StatusCode = result.Response?.StatusCode,
                DurationMs = (long)(result.Response?.TotalTime.TotalMilliseconds ?? 0),
            });
        }

        return new RequestSendOutcome { Result = result, SavedResponse = saved };
    }
}
