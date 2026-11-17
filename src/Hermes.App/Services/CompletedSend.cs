using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>A send that finished, with what <see cref="RequestSendService.Record"/> needs to keep it.</summary>
public sealed class CompletedSend
{
    /// <summary>The request as edited when it was sent, kept in the history entry.</summary>
    public required HttpRequestModel Model { get; init; }

    /// <summary>The sent request with secrets masked, which is what gets saved with the response.</summary>
    public required PreparedRequest MaskedRequest { get; init; }

    /// <summary>What the executor returned.</summary>
    public required ExecutionResult Result { get; init; }

    /// <summary>
    /// The saved request to keep the response with, or null to keep none. Decided by the tab when the response
    /// arrives, not when the send started: Save As during the send makes the tab a different request.
    /// </summary>
    public required Guid? KeepResponseWith { get; init; }
}
