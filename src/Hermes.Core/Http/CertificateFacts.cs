using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Hermes.Core.Http;

/// <summary>
/// What the TLS handshake showed about a server certificate that failed validation, copied out of the objects .NET
/// hands to the validation callback (it disposes them afterwards).
/// </summary>
public sealed record CertificateFacts
{
    /// <summary>The host the connection was made to.</summary>
    public required string Host { get; init; }

    /// <summary>The certificate's common name, or its full subject when it has none.</summary>
    public required string Subject { get; init; }

    /// <summary>The DNS names and IP addresses in the subject alternative name extension.</summary>
    public required IReadOnlyList<string> AlternativeNames { get; init; }

    /// <summary>The issuer's common name, or its full name when it has none.</summary>
    public required string Issuer { get; init; }

    public required DateTime NotBefore { get; init; }
    public required DateTime NotAfter { get; init; }

    /// <summary>True when the certificate is its own issuer and the only element in its chain.</summary>
    public required bool IsSelfSigned { get; init; }

    public required SslPolicyErrors PolicyErrors { get; init; }
    public required X509ChainStatusFlags ChainStatus { get; init; }

    /// <summary>Copies the facts out of a validation callback's arguments; null when the server sent no certificate.</summary>
    public static CertificateFacts? Capture(string host, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors policyErrors)
    {
        if (certificate is null)
            return null;

        using var certificate2 = new X509Certificate2(certificate);
        var chainStatus = X509ChainStatusFlags.NoError;
        foreach (var status in chain?.ChainStatus ?? [])
            chainStatus |= status.Status;

        return new CertificateFacts
        {
            Host = host,
            Subject = certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
            AlternativeNames = ReadDnsNames(certificate2),
            Issuer = certificate2.GetNameInfo(X509NameType.SimpleName, forIssuer: true),
            NotBefore = certificate2.NotBefore,
            NotAfter = certificate2.NotAfter,
            IsSelfSigned = certificate2.SubjectName.RawData.AsSpan().SequenceEqual(certificate2.IssuerName.RawData)
                && chain is { ChainElements.Count: 1 },
            PolicyErrors = policyErrors,
            ChainStatus = chainStatus,
        };
    }

    private static List<string> ReadDnsNames(X509Certificate2 certificate)
    {
        var names = new List<string>();
        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension alternativeNames)
            {
                names.AddRange(alternativeNames.EnumerateDnsNames());
                names.AddRange(alternativeNames.EnumerateIPAddresses().Select(address => address.ToString()));
            }
        }
        return names;
    }
}
