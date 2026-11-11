using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Hermes.Core.Models;

namespace Hermes.Core.Http;

/// <summary>Turns a failed certificate validation into a <see cref="RequestFailure"/>, without reading .NET's message wording.</summary>
public static class CertificateFailureDescriber
{
    private const string RiskCaption = "Only for servers you trust, such as a local or test server. Applies to this request only.";
    private const string DateFormat = "d MMM yyyy";
    private const int MaxListedNames = 5;

    /// <summary>
    /// .NET's own text (the <c>net_ssl_io_frame</c> resource) for the <see cref="System.Security.Authentication.AuthenticationException"/>
    /// raised when the peer's first bytes aren't a TLS record, as when an HTTPS URL points at a plain HTTP port.
    /// English only: .NET doesn't localise runtime messages. An integration test sends a real request to a plain HTTP
    /// listener, so a .NET upgrade that rewords this fails that test instead of silently giving the general wording.
    /// </summary>
    internal const string PlainHttpAnsweredMessage = "Cannot determine the frame size or a corrupted frame was received.";

    /// <param name="facts">What the handshake showed; null when no certificate check failed, which gives a failure in plain words: plain HTTP answered on an HTTPS URL, or else that both sides couldn't agree on how to secure the connection.</param>
    /// <param name="originalMessage">The exception's message. Kept for the Details only when no certificate facts exist, and never shown as the facts; with facts it is left out, since the facts say more.</param>
    /// <param name="now">The current time, to tell an expired certificate from one that isn't valid yet.</param>
    /// <param name="requestedHost">The host of the request. When the facts are for another host, a redirect led there.</param>
    public static RequestFailure Describe(CertificateFacts? facts, string originalMessage, DateTime now, string requestedHost)
    {
        // Without a failed certificate check (a protocol or cipher mismatch, a dropped handshake) turning
        // verification off would not help, so no next steps are offered.
        if (facts is null)
        {
            var plainHttpAnswered = originalMessage == PlainHttpAnsweredMessage;
            return new RequestFailure
            {
                Kind = plainHttpAnswered ? RequestFailureKind.PlainHttpAnswered : RequestFailureKind.SecureConnectionFailed,
                Headline = plainHttpAnswered ? "Server didn't answer over HTTPS" : "Secure connection failed",
                Facts = plainHttpAnswered
                    ? $"{requestedHost} answered, but not over a secure connection. If it only serves plain HTTP, change the URL to http://."
                    : $"Hermes and {requestedHost} couldn't agree on how to secure the connection. The server may only support old TLS versions or ciphers this computer no longer allows.",
                NothingWasSent = true,
                OriginalMessage = originalMessage,
            };
        }

        var classification = Classify(facts, now);
        var redirected = !string.Equals(facts.Host, requestedHost, StringComparison.OrdinalIgnoreCase);
        return new RequestFailure
        {
            Kind = classification.Kind,
            Headline = classification.Headline,
            Facts = redirected ? $"The request was redirected to {facts.Host}. {classification.Facts}" : classification.Facts,
            // After a redirect the first request went out.
            NothingWasSent = !redirected,
            Details = CreateDetails(facts),
            Actions = [RequestFailureAction.TurnOffSslVerification, RequestFailureAction.OpenRequestSettings],
            ActionCaption = RiskCaption,
        };
    }

    private static CertificateClassification Classify(CertificateFacts facts, DateTime now)
    {
        if (facts.ChainStatus.HasFlag(X509ChainStatusFlags.NotTimeValid))
        {
            // The flag covers every certificate in the chain; the dates here are the leaf's, so they decide the wording.
            if (facts.NotBefore > now)
                return new CertificateClassification(RequestFailureKind.CertificateExpired, "Certificate isn't valid yet",
                    $"The certificate for {facts.Host} isn't valid until {FormatDate(facts.NotBefore)}.");
            if (facts.NotAfter < now)
                return new CertificateClassification(RequestFailureKind.CertificateExpired, "Certificate expired",
                    $"The certificate for {facts.Host} expired on {FormatDate(facts.NotAfter)}.");
            return new CertificateClassification(RequestFailureKind.CertificateExpired, "A certificate in the chain has expired",
                $"The certificate for {facts.Host} is valid, but one of the certificates it depends on has expired or isn't valid yet.");
        }

        // The name is checked before the authority: the address is the cheaper thing to fix, and it is the first
        // thing that is wrong when both are.
        if (facts.PolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return new CertificateClassification(RequestFailureKind.CertificateNameMismatch, "Certificate is for a different address",
                $"Issued for {ListNames(facts)}, not {facts.Host}. Check the URL.");
        }

        if (facts.ChainStatus.HasFlag(X509ChainStatusFlags.UntrustedRoot) || facts.ChainStatus.HasFlag(X509ChainStatusFlags.PartialChain))
        {
            return facts.IsSelfSigned
                ? new CertificateClassification(RequestFailureKind.CertificateSelfSigned, "Certificate is self-signed",
                    "Signed by the server itself, not by a trusted authority. Common for local and test servers.")
                : new CertificateClassification(RequestFailureKind.CertificateUntrustedAuthority, "Certificate isn't from a trusted authority",
                    $"Issued by {facts.Issuer}, which this computer doesn't trust.");
        }

        return new CertificateClassification(RequestFailureKind.SecureConnectionFailed, "Secure connection failed",
            $"The certificate for {facts.Host} didn't pass a security check. Details say which one.");
    }

    private sealed record CertificateClassification(RequestFailureKind Kind, string Headline, string Facts);

    private static List<FailureDetail> CreateDetails(CertificateFacts facts) =>
    [
        new("Issued to", facts.Subject),
        new("Issued by", facts.Issuer),
        new("Valid", $"{FormatDate(facts.NotBefore)} – {FormatDate(facts.NotAfter)}"),
        new("Check failed", DescribeFailedChecks(facts)),
    ];

    /// <summary>The names of the checks that failed, as .NET names them: that is what a bug report needs.</summary>
    private static string DescribeFailedChecks(CertificateFacts facts)
    {
        var checks = new List<string>();
        foreach (var flag in Enum.GetValues<X509ChainStatusFlags>())
        {
            if (flag != X509ChainStatusFlags.NoError && facts.ChainStatus.HasFlag(flag))
                checks.Add(flag.ToString());
        }

        // The chain flags say more than "there were chain errors", so that one is only listed when nothing else is.
        var policyErrors = facts.PolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors;
        foreach (var error in new[] { SslPolicyErrors.RemoteCertificateNameMismatch, SslPolicyErrors.RemoteCertificateNotAvailable })
        {
            if (policyErrors.HasFlag(error))
                checks.Add(error.ToString());
        }

        if (checks.Count == 0 && facts.PolicyErrors != SslPolicyErrors.None)
            checks.Add(facts.PolicyErrors.ToString());
        return string.Join(", ", checks);
    }

    private static string ListNames(CertificateFacts facts)
    {
        IReadOnlyList<string> names = facts.AlternativeNames.Count > 0 ? facts.AlternativeNames : [facts.Subject];
        var listed = string.Join(", ", names.Take(MaxListedNames));
        return names.Count > MaxListedNames ? $"{listed} and {names.Count - MaxListedNames} more" : listed;
    }

    private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
