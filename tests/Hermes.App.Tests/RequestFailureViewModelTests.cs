using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>The failure as both panels show it: display values, the Details state and the copy command.</summary>
public class RequestFailureViewModelTests
{
    private static RequestFailure CreateFailure() => new()
    {
        Kind = RequestFailureKind.ConnectionRefused,
        Headline = "Server isn't accepting connections",
        Facts = "Nothing is listening on example.test.",
    };

    private static RequestFailureViewModel Create(RequestFailure failure, Func<string, Task>? copyText = null) =>
        new(failure, copyText ?? (_ => Task.CompletedTask));

    [Fact]
    public void Shows_the_headline_facts_and_nothing_was_sent_of_the_failure()
    {
        var failure = Create(CreateFailure() with { NothingWasSent = true });

        Assert.Equal("Server isn't accepting connections", failure.Headline);
        Assert.Equal("Nothing is listening on example.test.", failure.Facts);
        Assert.True(failure.NothingWasSent);
    }

    [Fact]
    public void Picks_the_icon_from_the_kind()
    {
        Assert.Equal("IconAlert", Create(CreateFailure()).IconKey);
        Assert.Equal("IconLock", Create(CreateFailure() with { Kind = RequestFailureKind.CertificateExpired }).IconKey);
        Assert.Equal("IconFile", Create(CreateFailure() with { Kind = RequestFailureKind.BodyFileUnreadable }).IconKey);
    }

    [Fact]
    public void Has_no_details_original_message_or_actions_when_the_failure_has_none()
    {
        var failure = Create(CreateFailure());

        Assert.False(failure.HasDetails);
        Assert.False(failure.HasOriginalMessage);
        Assert.Null(failure.OriginalMessage);
        Assert.False(failure.HasActionCaption);
        Assert.False(failure.HasActions);
        Assert.Empty(failure.Details);
    }

    [Fact]
    public void Exposes_details_original_message_caption_and_actions()
    {
        var failure = Create(CreateFailure() with
        {
            Details = [new FailureDetail("Host", "example.test")],
            OriginalMessage = "Connection refused",
            ActionCaption = "Only for servers you trust.",
            Actions = [RequestFailureAction.Retry, RequestFailureAction.EditUrl],
        });

        Assert.True(failure.HasDetails);
        Assert.Equal("Host", Assert.Single(failure.Details).Label);
        Assert.True(failure.HasOriginalMessage);
        Assert.Equal("Connection refused", failure.OriginalMessage);
        Assert.True(failure.HasActionCaption);
        Assert.Equal("Only for servers you trust.", failure.ActionCaption);
        Assert.True(failure.HasActions);
    }

    [Fact]
    public void An_original_message_alone_makes_details_available()
    {
        var failure = Create(CreateFailure() with { OriginalMessage = "boom" });

        Assert.True(failure.HasDetails);
        Assert.True(failure.HasOriginalMessage);
    }

    [Fact]
    public void An_empty_action_caption_is_not_shown()
    {
        Assert.False(Create(CreateFailure() with { ActionCaption = "" }).HasActionCaption);
    }

    [Fact]
    public void Offers_only_the_actions_the_failure_lists()
    {
        var failure = Create(CreateFailure() with { Actions = [RequestFailureAction.Retry] });

        Assert.True(failure.OffersAction(RequestFailureAction.Retry));
        Assert.False(failure.OffersAction(RequestFailureAction.EditUrl));
    }

    [Fact]
    public void Details_start_closed_and_raise_a_change_when_toggled()
    {
        var failure = Create(CreateFailure());
        var raised = new List<string?>();
        failure.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(failure.IsDetailsExpanded);
        failure.IsDetailsExpanded = true;

        Assert.True(failure.IsDetailsExpanded);
        Assert.Contains(nameof(RequestFailureViewModel.IsDetailsExpanded), raised);
    }

    [Fact]
    public async Task Copying_the_details_hands_the_clipboard_text_to_the_copy_function()
    {
        var source = CreateFailure() with { OriginalMessage = "Connection refused" };
        string? copied = null;
        var failure = Create(source, text => Task.FromResult(copied = text));

        await failure.CopyDetailsCommand.ExecuteAsync(null);

        Assert.Equal(source.ToClipboardText(), copied);
    }
}
