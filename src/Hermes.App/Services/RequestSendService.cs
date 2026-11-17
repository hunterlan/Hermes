using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Secrets;

namespace Hermes.App.Services;

/// <summary>
/// Sends a request in two steps, so the result is on screen before anything is written: <see cref="SendAsync"/>
/// executes it, the tab shows the result, then <see cref="Record"/> keeps the masked response with the saved request
/// and writes the history entry, in that order.
/// A cancelled token means the user cancelled or the tab was closed: nothing is recorded, even if a response had
/// just arrived.
/// </summary>
public sealed class RequestSendService(WorkspaceService workspace, IRequestExecutor executor)
{
    private readonly WorkspaceService _workspace = workspace;
    private readonly IRequestExecutor _executor = executor;

    public async Task<RequestSendOutcome> SendAsync(PreparedRequest request, CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(request, cancellationToken);
        return result.Cancelled || cancellationToken.IsCancellationRequested
            ? RequestSendOutcome.CancelledSend
            : RequestSendOutcome.Completed(result);
    }

    /// <summary>
    /// Keeps the response with <see cref="CompletedSend.KeepResponseWith"/>, then adds the history entry. Returns the
    /// response as stored, or null when none was (no request to keep it with, no response, or it couldn't be written).
    /// UI thread only: the history entry goes into <see cref="WorkspaceService.History"/>, which the UI shows, and a
    /// storage problem is reported to the UI.
    /// </summary>
    public SavedResponse? Record(CompletedSend send, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        // Responses are kept with saved requests, so reopening shows the last result and earlier ones.
        // The saved copy has credentials masked; the one on screen stays exact.
        SavedResponse? saved = null;
        if (send.Result.Response is { } response && send.KeepResponseWith is { } requestId)
        {
            saved = _workspace.SaveResponse(requestId, new SavedResponse
            {
                Response = response,
                SentRequest = SecretMasker.MaskCredentialHeaders(RawRequestFormatter.Format(send.MaskedRequest)),
            });
        }

        _workspace.AddHistory(new HistoryEntry
        {
            Request = send.Model,
            StatusCode = send.Result.Response?.StatusCode,
            DurationMs = (long)(send.Result.Response?.TotalTime.TotalMilliseconds ?? 0),
        });

        return saved;
    }
}
