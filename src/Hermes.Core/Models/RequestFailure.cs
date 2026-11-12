using System.Text;

namespace Hermes.Core.Models;

/// <summary>
/// Why a request failed, laid out the way the response pane shows it: a headline in plain words, the facts that
/// explain it, next steps, and details that stay collapsed (labelled lines plus the original message).
/// </summary>
public sealed record RequestFailure
{
    public required RequestFailureKind Kind { get; init; }

    /// <summary>What went wrong, in a few plain words. No exception or enum names.</summary>
    public required string Headline { get; init; }

    /// <summary>The host, date or name that explains the failure, as a sentence.</summary>
    public required string Facts { get; init; }

    /// <summary>True when the failure happened before the request left, so the view can say nothing was sent.</summary>
    public bool NothingWasSent { get; init; }

    /// <summary>Labelled lines for the collapsed Details, e.g. the certificate's subject and issuer.</summary>
    public IReadOnlyList<FailureDetail> Details { get; init; } = [];

    /// <summary>The message as the system reported it, shown in Details and copied with them.</summary>
    public string? OriginalMessage { get; init; }

    /// <summary>What the next steps work on, when they need to know: the path of the file, or the name of the header.</summary>
    public string? Subject { get; init; }

    /// <summary>Next steps, in the order they are offered.</summary>
    public IReadOnlyList<RequestFailureAction> Actions { get; init; } = [];

    /// <summary>Said next to the actions, e.g. the risk of turning a check off.</summary>
    public string? ActionCaption { get; init; }

    public bool HasDetails => Details.Count > 0 || OriginalMessage is not null;

    /// <summary>A failure with no case of its own: the message is shown as it is.</summary>
    public static RequestFailure FromMessage(string message) => new()
    {
        Kind = RequestFailureKind.Other,
        Headline = "Request failed",
        Facts = message,
    };

    /// <summary>The failure as plain text for a bug report: headline, facts, the labelled lines, then the original message.</summary>
    public string ToClipboardText()
    {
        var text = new StringBuilder();
        text.AppendLine(Headline);
        text.AppendLine(Facts);
        if (Details.Count > 0)
            text.AppendLine();
        foreach (var detail in Details)
            text.AppendLine($"{detail.Label}: {detail.Value}");
        if (OriginalMessage is not null)
        {
            text.AppendLine();
            text.AppendLine(OriginalMessage);
        }
        return text.ToString().TrimEnd();
    }
}
