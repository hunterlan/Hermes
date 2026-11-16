using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>How a send ended, as returned by <see cref="RequestSendService"/>.</summary>
public sealed class RequestSendOutcome
{
    /// <summary>The send was cancelled: nothing was saved or recorded, and no result is kept.</summary>
    public bool Cancelled { get; init; }

    /// <summary>What the executor returned; null when <see cref="Cancelled"/>.</summary>
    public ExecutionResult? Result { get; init; }

    /// <summary>The response as stored with the request, or null when none was stored (not saved, or it couldn't be written).</summary>
    public SavedResponse? SavedResponse { get; init; }

    public static RequestSendOutcome CancelledSend { get; } = new() { Cancelled = true };
}
