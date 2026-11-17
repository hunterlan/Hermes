using System.Net.Sockets;

namespace Hermes.Core.Http;

/// <summary>
/// Gives a <see cref="SocketException"/> that escaped <c>HttpClient.SendAsync</c> on its own the context it lacks.
/// The handler wraps every socket failure of a request in an <see cref="HttpRequestException"/> or an
/// <see cref="IOException"/>; a bare one comes from setting up the connection, before anything was sent. That happens
/// when the server resets a just-made connection: .NET then reads the remote end point and gets ENOTCONN (Linux) or
/// EINVAL (macOS), codes that say nothing about the server by themselves.
/// </summary>
public static class ConnectionSetupFailure
{
    /// <summary>
    /// A bare socket exception becomes a "connection error" that keeps it as the inner exception, so a refusal or
    /// timeout inside is still found, and anything else reads as a connection that couldn't be made. Any other
    /// exception is returned as it is.
    /// </summary>
    public static Exception Wrap(Exception exception) => exception is SocketException
        ? new HttpRequestException(HttpRequestError.ConnectionError, "The connection couldn't be set up.", exception)
        : exception;
}
