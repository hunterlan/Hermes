using System.Diagnostics.CodeAnalysis;
using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>How a send ended, as returned by <see cref="RequestSendService"/>: cancelled, or with a result.</summary>
public sealed class RequestSendOutcome
{
    private RequestSendOutcome(ExecutionResult? result, SavedResponse? savedResponse)
    {
        Result = result;
        SavedResponse = savedResponse;
    }

    public static RequestSendOutcome CancelledSend { get; } = new(null, null);

    /// <summary>What the executor returned; null only when <see cref="Cancelled"/>.</summary>
    public ExecutionResult? Result { get; }

    /// <summary>The send was cancelled: nothing was saved or recorded, and no result is kept.</summary>
    [MemberNotNullWhen(false, nameof(Result))]
    public bool Cancelled => Result is null;

    /// <summary>The response as stored with the request, or null when none was stored (not saved, or it couldn't be written).</summary>
    public SavedResponse? SavedResponse { get; }

    public static RequestSendOutcome Completed(ExecutionResult result, SavedResponse? savedResponse) => new(result, savedResponse);
}
