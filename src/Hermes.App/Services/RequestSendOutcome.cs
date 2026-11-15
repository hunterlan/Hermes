using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>How a send ended, as returned by <see cref="RequestSendService"/>.</summary>
public sealed class RequestSendOutcome
{
    /// <summary>The tab was closed during the send: nothing was saved or recorded, and the result is dropped.</summary>
    public bool Abandoned { get; init; }

    /// <summary>What the executor returned; null when <see cref="Abandoned"/>.</summary>
    public ExecutionResult? Result { get; init; }

    /// <summary>The response as stored with the request, or null when none was stored (not saved, or it couldn't be written).</summary>
    public SavedResponse? SavedResponse { get; init; }

    public bool Cancelled => Result?.Cancelled == true;

    public static RequestSendOutcome AbandonedSend { get; } = new() { Abandoned = true };
}
