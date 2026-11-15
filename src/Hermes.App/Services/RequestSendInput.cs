using Hermes.Core.Models;
using Hermes.Core.Requests;

namespace Hermes.App.Services;

/// <summary>What <see cref="RequestSendService"/> needs to send one request and record the result.</summary>
public sealed class RequestSendInput
{
    /// <summary>The request as edited, kept in the history entry.</summary>
    public required HttpRequestModel Model { get; init; }

    /// <summary>The request to send and its copy with secrets masked, which is what gets saved.</summary>
    public required PreparedRequestPair Prepared { get; init; }

    /// <summary>The request's id. The response is kept with it only if a request with this id is saved when the send ends.</summary>
    public required Guid RequestId { get; init; }

    /// <summary>
    /// Asked once the request has finished: true when the tab that sent it was closed meanwhile, in which case
    /// nothing is saved and no history entry is written.
    /// </summary>
    public Func<bool>? IsAbandoned { get; init; }
}
