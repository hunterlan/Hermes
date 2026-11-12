using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>A next step the person picked on a failure: which one, and the failure it was offered for.</summary>
public sealed class RequestFailureActionEventArgs(RequestFailureAction action, RequestFailure failure) : EventArgs
{
    public RequestFailureAction Action { get; } = action;

    public RequestFailure Failure { get; } = failure;
}
