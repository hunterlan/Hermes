using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Hermes.TestSupport;

/// <summary>Certificates for the local HTTPS server, made to fail validation in one specific way each.</summary>
public static class TestCertificates
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    /// <summary>A self-signed certificate for 127.0.0.1 whose validity ended long ago.</summary>
    public static X509Certificate2 Expired() =>
        CreateSelfSigned("Expired Test Server", "localhost", DateTimeOffset.UtcNow.AddYears(-3), DateTimeOffset.UtcNow.AddYears(-2));

    /// <summary>A self-signed certificate for 127.0.0.1 that is valid now.</summary>
    public static X509Certificate2 SelfSigned() =>
        CreateSelfSigned("Self-Signed Test Server", "localhost", DateTimeOffset.UtcNow - OneDay, DateTimeOffset.UtcNow.AddYears(1));

    /// <summary>A self-signed certificate that is valid now but names other hosts than 127.0.0.1.</summary>
    public static X509Certificate2 ForOtherHost(string dnsName) =>
        CreateSelfSigned("Other Host Test Server", dnsName, DateTimeOffset.UtcNow - OneDay, DateTimeOffset.UtcNow.AddYears(1), includeLoopback: false);

    /// <summary>A valid certificate for 127.0.0.1, issued by a certificate authority that no computer trusts.</summary>
    public static X509Certificate2 FromUntrustedAuthority(string authorityName)
    {
        var notBefore = DateTimeOffset.UtcNow - OneDay;
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);

        using var authorityKey = RSA.Create(2048);
        var authorityRequest = new CertificateRequest($"CN={authorityName}", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, false, 0, critical: true));
        using var authority = authorityRequest.CreateSelfSigned(notBefore, notAfter);

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=Authority-Signed Test Server", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(CreateAlternativeNames("localhost", includeLoopback: true));
        using var leaf = leafRequest.Create(authority, notBefore, notAfter, RandomNumberGenerator.GetBytes(8));
        return WithPrivateKey(leaf.CopyWithPrivateKey(leafKey));
    }

    private static X509Certificate2 CreateSelfSigned(string commonName, string dnsName, DateTimeOffset notBefore, DateTimeOffset notAfter,
        bool includeLoopback = true)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(CreateAlternativeNames(dnsName, includeLoopback));
        using var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return WithPrivateKey(certificate);
    }

    private static X509Extension CreateAlternativeNames(string dnsName, bool includeLoopback)
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        if (includeLoopback)
            names.AddIpAddress(IPAddress.Loopback);
        return names.Build();
    }

    /// <summary>Round-trips through PKCS#12: SslStream can't use an ephemeral key on every platform.</summary>
    private static X509Certificate2 WithPrivateKey(X509Certificate2 certificate) =>
        X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
}
