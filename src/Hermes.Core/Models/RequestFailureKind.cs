namespace Hermes.Core.Models;

/// <summary>What kind of problem stopped a request. The view picks its icon from this; add a member per new case.</summary>
public enum RequestFailureKind
{
    /// <summary>Anything without a case of its own; the failure's facts carry the message.</summary>
    Other,

    /// <summary>The secure connection failed for a reason Hermes can't name more closely.</summary>
    SecureConnectionFailed,

    /// <summary>The server answered an HTTPS request in plain HTTP, or in something that isn't TLS.</summary>
    PlainHttpAnswered,

    /// <summary>The certificate has expired, or isn't valid yet.</summary>
    CertificateExpired,

    /// <summary>The server signed its own certificate.</summary>
    CertificateSelfSigned,

    /// <summary>The certificate was issued by an authority this computer doesn't trust.</summary>
    CertificateUntrustedAuthority,

    /// <summary>The certificate was issued for other host names than the one requested.</summary>
    CertificateNameMismatch,

    /// <summary>The host name has no address.</summary>
    NameNotResolved,

    /// <summary>The host is there, but nothing accepts connections on the port.</summary>
    ConnectionRefused,

    /// <summary>The server closed or reset the connection, or ended the response early.</summary>
    ConnectionClosed,

    /// <summary>The connection couldn't be made: no route, no answer in time, or a reason .NET doesn't name.</summary>
    ConnectionFailed,

    /// <summary>The URL can't be turned into a request: empty, no host, an unresolved variable, bad characters.</summary>
    InvalidUrl,

    /// <summary>A file the body refers to is missing, a folder, or can't be opened.</summary>
    BodyFileUnreadable,

    /// <summary>A header's name or value can't be sent.</summary>
    InvalidHeader,

    /// <summary>The callback port for the sign-in response is taken by another program.</summary>
    CallbackPortInUse,

    /// <summary>The token server answered with an OAuth error such as invalid_client.</summary>
    TokenRequestRefused,

    /// <summary>The token server answered, but not with an access token or an OAuth error.</summary>
    TokenResponseInvalid,

    /// <summary>Something went wrong while getting a token that Hermes has no case for. Likely a bug.</summary>
    UnexpectedTokenFailure,
}
