using System.Net;
using System.Net.Sockets;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.Core.Auth;

/// <summary>
/// Turns what went wrong while getting an OAuth 2.0 token into a <see cref="RequestFailure"/>. Network and certificate
/// failures use the same classification and wording as request errors; the cases of token requests are added here.
/// </summary>
public static class OAuth2FailureDescriber
{
    public const string GenericHeadline = "Couldn't get a token";

    // What HttpListener reports when the port is taken. Unix passes the errno of the system through, and the
    // numbers differ by kernel; Windows reports the http.sys error for a prefix that is already registered.
    private const int AddressInUseLinux = 98;
    private const int AddressInUseMac = 48;
    private const int AlreadyExistsWindows = 183;
    private const int SharingViolationWindows = 32;

    private const int MaxServerTextLength = 300;
    private const string UnknownErrorHeadline = "The token server refused the request";

    private static readonly Dictionary<string, (string Headline, string Facts, RequestFailureAction? Action)> KnownErrors = new(StringComparer.Ordinal)
    {
        ["invalid_client"] = ("The token server doesn't accept this client",
            "The Client ID or Client Secret is wrong, or is sent the wrong way. Check them and Client Authentication.", null),
        ["invalid_grant"] = ("The token server rejected the authorization",
            "The authorization code, refresh token or user credentials are invalid, expired or already used. Try again.", RequestFailureAction.Retry),
        ["invalid_request"] = ("The token server rejected the request",
            "A required value is missing or wrong. Check the fields above.", null),
        ["unauthorized_client"] = ("This client isn't allowed to get a token this way",
            "The token server doesn't allow this client to use the selected grant type.", null),
        ["unsupported_grant_type"] = ("The token server doesn't support this grant type",
            "Choose another grant type.", null),
        ["invalid_scope"] = ("The token server rejected the scope",
            "The scope is invalid, unknown or not allowed for this client. Check the Scope field.", null),
        ["access_denied"] = ("Access was denied",
            "The authorization was not granted.", RequestFailureAction.Retry),
    };

    /// <summary>A failure that is already a sentence, such as an invalid URL or an authorization that timed out.</summary>
    public static RequestFailure FromMessage(string message, bool offerRetry = false) => new()
    {
        Kind = RequestFailureKind.Other,
        Headline = GenericHeadline,
        Facts = message,
        Actions = offerRetry ? [RequestFailureAction.Retry] : [],
    };

    /// <summary>The server answered with an OAuth <c>error</c>. The description is the server's text and never becomes a headline.</summary>
    public static RequestFailure FromServerError(string error, string? description, HttpStatusCode? status = null)
    {
        var known = KnownErrors.GetValueOrDefault(error);
        var headline = known.Headline ?? UnknownErrorHeadline;
        var facts = known.Facts is null
            ? DescribeUnknownError(error, description)
            : string.IsNullOrWhiteSpace(description) ? known.Facts : $"{known.Facts} The server said: {Truncate(description)}";

        var details = new List<FailureDetail> { new("Error code", error) };
        if (!string.IsNullOrWhiteSpace(description))
            details.Add(new FailureDetail("Description", description));
        if (status is { } code)
            details.Add(new FailureDetail("HTTP status", $"{(int)code} {code}"));

        return new RequestFailure
        {
            Kind = RequestFailureKind.TokenRequestRefused,
            Headline = headline,
            Facts = facts,
            Details = details,
            Actions = known.Action is { } action ? [action] : [],
        };
    }

    /// <summary>The authorization server sent back an <c>error</c> instead of a code, e.g. when the user denied access.</summary>
    public static RequestFailure FromAuthorizationError(string error, string? description)
    {
        if (error == "access_denied")
            return FromServerError(error, description);

        var details = new List<FailureDetail> { new("Error code", error) };
        if (!string.IsNullOrWhiteSpace(description))
            details.Add(new FailureDetail("Description", description));
        return new RequestFailure
        {
            Kind = RequestFailureKind.TokenRequestRefused,
            Headline = "The authorization server refused the request",
            Facts = DescribeUnknownError(error, description),
            Details = details,
        };
    }

    private static string DescribeUnknownError(string error, string? description)
    {
        var facts = $"It answered with the error \"{Truncate(error)}\".";
        return string.IsNullOrWhiteSpace(description) ? facts : $"{facts} The server said: {Truncate(description)}";
    }

    /// <summary>
    /// The token server answered with something that is neither a token nor an OAuth error. A 401 or 403 without an
    /// OAuth <c>error</c> most likely means the client credentials were rejected, so it says that.
    /// </summary>
    public static RequestFailure FromUnexpectedResponse(string reason, string body, HttpStatusCode status)
    {
        var details = new List<FailureDetail> { new("HTTP status", $"{(int)status} {status}") };
        if (!string.IsNullOrWhiteSpace(body))
            details.Add(new FailureDetail("Response", Truncate(body)));

        var rejected = status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        return new RequestFailure
        {
            Kind = rejected ? RequestFailureKind.TokenRequestRefused : RequestFailureKind.TokenResponseInvalid,
            Headline = rejected ? "The token server rejected the client credentials" : "The token server's answer wasn't understood",
            Facts = rejected ? $"It answered {(int)status} {status}. Check the Client ID and Client Secret, and Client Authentication." : reason,
            Details = details,
        };
    }

    /// <summary>The callback listener couldn't start, or the callback port is taken on a loopback address.</summary>
    public static RequestFailure FromCallbackListener(Exception exception, string host, int port)
    {
        var inUse = IsAddressInUse(exception);
        return new RequestFailure
        {
            Kind = inUse ? RequestFailureKind.CallbackPortInUse : RequestFailureKind.Other,
            Headline = inUse ? $"Port {port} is already in use" : "Couldn't listen for the sign-in response",
            Facts = inUse ? "Choose another callback port." : $"Hermes couldn't start listening on {host}:{port}. Check the Callback URL.",
            NothingWasSent = true,
            Details = [new FailureDetail("Callback address", $"{host}:{port}"), new FailureDetail("Error", NetworkFailureClassifier.DescribeChain(exception))],
            OriginalMessage = inUse ? null : ExceptionText.Flatten(exception),
            Actions = [RequestFailureAction.EditCallbackUrl],
        };
    }

    /// <summary>The token request didn't get an answer in time.</summary>
    public static RequestFailure FromTimeout(Uri tokenUri) => new()
    {
        Kind = RequestFailureKind.ConnectionFailed,
        Headline = "Server didn't answer",
        Facts = $"{tokenUri.Host}:{tokenUri.Port} didn't answer the token request in time.",
        Details = [new FailureDetail("Server", $"{tokenUri.Host}:{tokenUri.Port}")],
        Actions = [RequestFailureAction.Retry],
    };

    /// <summary>A network failure of the token request: the same wording as a request that failed the same way.</summary>
    /// <param name="certificateFacts">What the handshake showed, when a certificate check failed.</param>
    public static RequestFailure FromNetworkFailure(Exception exception, Uri requestedUri, Uri attemptedUri, CertificateFacts? certificateFacts,
        SslSettingSource sslSource, DateTime now)
    {
        if (NetworkFailureClassifier.Classify(exception) != NetworkFailureCause.SecureConnectionFailed)
            return NetworkFailureDescriber.Describe(exception, requestedUri, attemptedUri);

        var failure = CertificateFailureDescriber.Describe(certificateFacts, RequestExecutor.FindTlsMessage(exception), now, requestedUri.IdnHost);
        // Without a failed certificate check, turning verification off would not help.
        if (certificateFacts is null)
            return failure;

        // A request's own "Turn off verification" would change a request that isn't being sent. The setting that
        // token requests follow is the one to point to.
        return sslSource == SslSettingSource.Request
            ? failure with { Actions = [RequestFailureAction.OpenRequestSettings], ActionCaption = "Token requests follow this request's SSL setting." }
            : failure with { Actions = [RequestFailureAction.OpenAppSettings], ActionCaption = "The app-wide SSL setting applies to token requests." };
    }

    /// <summary>Something Hermes has no case for: the details are for a bug report.</summary>
    public static RequestFailure FromUnexpected(Exception exception) => new()
    {
        Kind = RequestFailureKind.UnexpectedTokenFailure,
        Headline = GenericHeadline,
        Facts = "Something unexpected went wrong. Copy the details if you report this.",
        Details = [new FailureDetail("Error", NetworkFailureClassifier.DescribeChain(exception))],
        OriginalMessage = ExceptionText.Flatten(exception),
    };

    private static bool IsAddressInUse(Exception exception) => exception switch
    {
        SocketException socket => socket.SocketErrorCode == SocketError.AddressAlreadyInUse,
        HttpListenerException listener => listener.ErrorCode is AddressInUseLinux or AddressInUseMac or AlreadyExistsWindows or SharingViolationWindows,
        _ => false,
    };

    private static string Truncate(string text) =>
        text.Length > MaxServerTextLength ? text[..MaxServerTextLength] + "…" : text;
}
