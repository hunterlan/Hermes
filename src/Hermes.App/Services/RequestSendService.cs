using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Secrets;

namespace Hermes.App.Services;

/// <summary>
/// Sends a prepared request, then keeps its masked response with the saved request and writes the history entry,
/// in that order. The tab decides what to show; this decides what is recorded.
/// A cancelled token means the user cancelled or the tab was closed: nothing is recorded, even if a response had
/// just arrived.
/// </summary>
public sealed class RequestSendService(WorkspaceService workspace, IRequestExecutor executor)
{
    private readonly WorkspaceService _workspace = workspace;
    private readonly IRequestExecutor _executor = executor;

    public async Task<RequestSendOutcome> SendAsync(RequestSendInput input, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(input.Prepared.Request, cancellationToken);
        if (result.Cancelled || cancellationToken.IsCancellationRequested)
            return RequestSendOutcome.CancelledSend;

        // Responses are kept with saved requests, so reopening shows the last result and earlier ones.
        // The saved copy has credentials masked; the one on screen stays exact.
        SavedResponse? saved = null;
        if (result.Response is { } response && _workspace.Find(input.RequestId) is not null)
        {
            saved = _workspace.SaveResponse(input.RequestId, new SavedResponse
            {
                Response = response,
                SentRequest = SecretMasker.MaskCredentialHeaders(RawRequestFormatter.Format(input.Prepared.Masked)),
            });
        }

        _workspace.AddHistory(new HistoryEntry
        {
            Request = input.Model,
            StatusCode = result.Response?.StatusCode,
            DurationMs = (long)(result.Response?.TotalTime.TotalMilliseconds ?? 0),
        });

        return RequestSendOutcome.Completed(result, saved);
    }
}
