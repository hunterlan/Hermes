using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>
/// Turns an exception from a failed network call into a <see cref="RequestFailure"/>. What went wrong comes from
/// <see cref="NetworkFailureClassifier"/>; the wording here is the same on every operating system.
/// </summary>
public static class NetworkFailureDescriber
{
    /// <param name="exception">What the call threw.</param>
    /// <param name="requestedUri">The address the user asked for.</param>
    /// <param name="attemptedUri">The address the failed connection was for. When it differs from the requested one, a redirect led there.</param>
    public static RequestFailure Describe(Exception exception, Uri requestedUri, Uri attemptedUri)
    {
        var cause = NetworkFailureClassifier.Classify(exception);
        var host = attemptedUri.Host;
        var endpoint = $"{host}:{attemptedUri.Port}";
        var redirected = requestedUri != attemptedUri;
        var originalMessage = ExceptionText.Flatten(exception);

        var wording = cause switch
        {
            NetworkFailureCause.NameNotResolved => new FailureWording(RequestFailureKind.NameNotResolved, "Couldn't find the server",
                $"{host} doesn't resolve. Check the URL or your network or VPN.", true, RequestFailureAction.EditUrl),
            NetworkFailureCause.ConnectionRefused => new FailureWording(RequestFailureKind.ConnectionRefused, "Server isn't accepting connections",
                $"Nothing is listening on {endpoint}. Is the server running?", true, RequestFailureAction.Retry),
            NetworkFailureCause.ConnectionClosed => new FailureWording(RequestFailureKind.ConnectionClosed, "Connection was closed by the server",
                "The server closed the connection before the response finished.", false, RequestFailureAction.Retry),
            NetworkFailureCause.NetworkUnreachable => new FailureWording(RequestFailureKind.ConnectionFailed, "Couldn't reach the server",
                $"There is no route to {host}. Check your network or VPN.", true, RequestFailureAction.Retry),
            NetworkFailureCause.ConnectionTimedOut => new FailureWording(RequestFailureKind.ConnectionFailed, "Server didn't answer",
                $"{endpoint} didn't accept the connection in time.", true, RequestFailureAction.Retry),
            NetworkFailureCause.ConnectionFailed => new FailureWording(RequestFailureKind.ConnectionFailed, "Couldn't connect to the server",
                $"The connection to {endpoint} couldn't be made.", true, RequestFailureAction.Retry),
            _ => new FailureWording(RequestFailureKind.Other, "Request failed",
                CleanUp(originalMessage), false, null),
        };
        // After a redirect the first request went out, and the host named is the redirect's, not the one typed.
        if (redirected && cause != NetworkFailureCause.Unknown)
        {
            wording = wording with
            {
                Facts = RedirectNote.Prepend(host, wording.Facts),
                NothingWasSent = false,
                // The URL the user can edit isn't the one that failed.
                Action = wording.Action == RequestFailureAction.EditUrl ? RequestFailureAction.Retry : wording.Action,
            };
        }

        return new RequestFailure
        {
            Kind = wording.Kind,
            Headline = wording.Headline,
            Facts = wording.Facts,
            NothingWasSent = wording.NothingWasSent,
            Details = [new FailureDetail("Server", endpoint), new FailureDetail("Error", NetworkFailureClassifier.DescribeChain(exception))],
            // With no cause named, the message is already the facts.
            OriginalMessage = cause == NetworkFailureCause.Unknown ? null : originalMessage,
            Actions = wording.Action is { } next ? [next] : [],
        };
    }

    /// <summary>The wording and next step for one cause, before a redirect changes it.</summary>
    private sealed record FailureWording(RequestFailureKind Kind, string Headline, string Facts, bool NothingWasSent, RequestFailureAction? Action);

    /// <summary>Collapses whitespace and makes sure the text ends like a sentence.</summary>
    private static string CleanUp(string message)
    {
        var text = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 || text[^1] is '.' or '!' or '?' ? text : text + ".";
    }
}
