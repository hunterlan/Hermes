using System.Diagnostics.CodeAnalysis;
using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>How a send ended, as returned by <see cref="RequestSendService.SendAsync"/>: cancelled, or with a result.</summary>
public sealed class RequestSendOutcome
{
    private RequestSendOutcome(ExecutionResult? result) => Result = result;

    public static RequestSendOutcome CancelledSend { get; } = new(null);

    /// <summary>What the executor returned; null only when <see cref="Cancelled"/>.</summary>
    public ExecutionResult? Result { get; }

    /// <summary>The send was cancelled: there is nothing to show or record.</summary>
    [MemberNotNullWhen(false, nameof(Result))]
    public bool Cancelled => Result is null;

    public static RequestSendOutcome Completed(ExecutionResult result) => new(result);
}
