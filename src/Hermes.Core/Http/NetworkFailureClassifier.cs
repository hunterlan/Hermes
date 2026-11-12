using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Hermes.Core.Http;

/// <summary>
/// Says what went wrong with a network call from the exception types and error codes (<see cref="SocketError"/>,
/// <see cref="HttpRequestError"/>), never from message text, which differs between operating systems and languages.
/// </summary>
public static class NetworkFailureClassifier
{
    /// <summary>Looks through the exception and everything it wraps; the most specific cause found wins.</summary>
    public static NetworkFailureCause Classify(Exception exception)
    {
        // A generic "the connection failed" on an outer exception is kept only when nothing inside is more specific.
        var cause = NetworkFailureCause.Unknown;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var found = ClassifySingle(current);
            if (found == NetworkFailureCause.Unknown)
                continue;
            if (found != NetworkFailureCause.ConnectionFailed)
                return found;
            cause = found;
        }
        return cause;
    }

    /// <summary>
    /// The exception types and codes along the chain, e.g. "HttpRequestException (NameResolutionError) > SocketException
    /// (HostNotFound)", for the Details of a failure. The same on every operating system, unlike the messages.
    /// </summary>
    public static string DescribeChain(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var code = current switch
            {
                HttpRequestException { HttpRequestError: not HttpRequestError.Unknown } http => http.HttpRequestError.ToString(),
                HttpIOException io => io.HttpRequestError.ToString(),
                SocketException socket => socket.SocketErrorCode.ToString(),
                _ => null,
            };
            parts.Add(code is null ? current.GetType().Name : $"{current.GetType().Name} ({code})");
        }
        return string.Join(" > ", parts);
    }

    private static NetworkFailureCause ClassifySingle(Exception exception) => exception switch
    {
        SocketException socket => FromSocketError(socket.SocketErrorCode),
        HttpRequestException http => FromHttpError(http.HttpRequestError),
        HttpIOException io => FromHttpError(io.HttpRequestError),
        AuthenticationException => NetworkFailureCause.SecureConnectionFailed,
        // The handler's connect timeout cancels the call with a TimeoutException inside.
        TimeoutException => NetworkFailureCause.ConnectionTimedOut,
        _ => NetworkFailureCause.Unknown,
    };

    private static NetworkFailureCause FromHttpError(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => NetworkFailureCause.NameNotResolved,
        HttpRequestError.ConnectionError => NetworkFailureCause.ConnectionFailed,
        HttpRequestError.SecureConnectionError => NetworkFailureCause.SecureConnectionFailed,
        HttpRequestError.ResponseEnded => NetworkFailureCause.ConnectionClosed,
        _ => NetworkFailureCause.Unknown,
    };

    private static NetworkFailureCause FromSocketError(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => NetworkFailureCause.NameNotResolved,
        SocketError.ConnectionRefused => NetworkFailureCause.ConnectionRefused,
        SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown => NetworkFailureCause.ConnectionClosed,
        SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.NetworkDown => NetworkFailureCause.NetworkUnreachable,
        SocketError.TimedOut => NetworkFailureCause.ConnectionTimedOut,
        _ => NetworkFailureCause.Unknown,
    };
}
