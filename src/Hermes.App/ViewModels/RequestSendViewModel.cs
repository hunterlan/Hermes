using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Requests;

namespace Hermes.App.ViewModels;

/// <summary>
/// The Send button of a request tab: asks for a yes when the request needs one, sends the request as it is at that
/// moment, shows the result in the response and then records it, or cancels the send in flight. Disposing it (the tab
/// was closed) cancels the send, and a send that ends anyway shows and records nothing.
/// </summary>
public sealed partial class RequestSendViewModel(IRequestSource request, TabServices services, ResponseViewModel response)
    : ObservableObject, IDisposable
{
    private CancellationTokenSource? _sendCts;
    private bool _isClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendButtonText))]
    [NotifyPropertyChangedFor(nameof(SendButtonCommand))]
    public partial bool IsSending { get; set; }

    public string SendButtonText => IsSending ? "Cancel" : "Send";

    /// <summary>What the Send button does: send, or cancel the request in flight.</summary>
    public ICommand SendButtonCommand => IsSending ? CancelSendCommand : SendCommand;

    /// <summary>Ctrl+Enter: sends, or cancels the request in flight. Doesn't wait for the response.</summary>
    public void SendOrCancel()
    {
        if (SendButtonCommand.CanExecute(null))
            SendButtonCommand.Execute(null);
    }

    [RelayCommand]
    private void CancelSend() => _sendCts?.Cancel();

    /// <summary>The tab was closed: cancels the send in flight, which then updates and records nothing.</summary>
    public void Dispose()
    {
        _isClosed = true;
        CancelSend();
    }

    [RelayCommand]
    private async Task Send()
    {
        var model = request.BuildModel();
        // One context for the request and its masked copy: switching environments while it's in flight changes neither.
        var context = request.CreateContext();
        var pair = RequestPreparer.PrepareWithMasked(model, context);
        var prepared = pair.Request;
        if (!await ConfirmSendAsync(model, context, prepared) || _isClosed)
            return;

        IsSending = true;
        response.Sent.SetSentRequest(prepared);
        response.BeginLoading();
        _sendCts = new CancellationTokenSource();
        try
        {
            var outcome = await services.Sender.SendAsync(prepared, _sendCts.Token);
            // A tab closed during the send updates and records nothing; closing cancels the send.
            if (_isClosed)
                return;
            if (outcome.Cancelled)
            {
                response.SetCancelled();
                return;
            }
            // Shown first, so a problem while recording can't keep the response off screen.
            var result = outcome.Result;
            if (result.Response is { } received)
                response.SetResponse(received, sentWith: context.Environment);
            else
                response.SetFailure(result.Failure ?? RequestFailure.FromMessage("Unknown error"));

            // The request this tab is now: Save As during the send made it a new one, and the response goes with that.
            var saved = await services.Sender.RecordAsync(new CompletedSend
            {
                Model = model,
                MaskedRequest = pair.Masked,
                Result = result,
                KeepResponseWith = request.IsPersisted ? request.RequestId : null,
            }, _sendCts.Token);
            // The response is on disk either way; a tab closed while it was written has no list to add it to.
            if (saved is not null && !_isClosed)
                response.Saved.AddSaved(saved);
        }
        finally
        {
            IsSending = false;
            _sendCts.Dispose();
            _sendCts = null;
        }
    }

    /// <summary>Variables used by the request that resolve to nothing; they would be sent as literal {{name}} text.</summary>
    public IReadOnlyList<string> FindUnresolvedVariables() =>
        UnresolvedVariableFinder.Find(request.BuildModel(), request.CreateContext());

    /// <summary>Unresolved variables and environments marked "ask before sending" need a yes first.</summary>
    private async Task<bool> ConfirmSendAsync(HttpRequestModel model, RequestContext context, PreparedRequest prepared)
    {
        var environment = services.Workspace.ActiveEnvironment;
        var unresolved = UnresolvedVariableFinder.Find(model, context);
        if (unresolved.Count > 0)
        {
            var names = string.Join(", ", unresolved.Select(n => "{{" + n + "}}"));
            var where = environment is null ? "with no environment selected" : $"in {environment.Name}";
            if (!await services.Dialogs.ConfirmAsync("Unresolved variables",
                    $"{names} {(unresolved.Count == 1 ? "has" : "have")} no value {where}, so the literal text would be sent.",
                    "Send anyway", destructive: false))
                return false;
        }
        if (environment is { ConfirmBeforeSend: true })
        {
            return await services.Dialogs.ConfirmAsync($"Send to {environment.Name}?", $"{prepared.Method} {prepared.Url}",
                $"Send to {environment.Name}");
        }
        return true;
    }
}
