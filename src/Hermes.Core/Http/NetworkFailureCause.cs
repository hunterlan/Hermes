namespace Hermes.Core.Http;

/// <summary>What stopped a network call, as far as the exception types say. Same on every operating system.</summary>
public enum NetworkFailureCause
{
    /// <summary>Nothing in the exception names a cause Hermes can word more closely.</summary>
    Unknown,

    /// <summary>The host name has no address.</summary>
    NameNotResolved,

    /// <summary>The computer was reached, but nothing accepts connections on the port.</summary>
    ConnectionRefused,

    /// <summary>The server reset or closed the connection, or ended the response early.</summary>
    ConnectionClosed,

    /// <summary>There is no route to the host or network.</summary>
    NetworkUnreachable,

    /// <summary>The server didn't answer while the connection was being made.</summary>
    ConnectionTimedOut,

    /// <summary>The connection couldn't be made, for a reason the exceptions don't say.</summary>
    ConnectionFailed,

    /// <summary>The TLS handshake or certificate check failed.</summary>
    SecureConnectionFailed,
}
