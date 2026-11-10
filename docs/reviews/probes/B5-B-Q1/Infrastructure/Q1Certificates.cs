using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>
/// In-memory test certificates for the reviewer TLS probes. Nothing is written to disk, nothing is
/// installed into any machine trust store, and no private key or PFX blob is ever committed.
/// </summary>
internal static class Q1Certificates
{
    internal static X509Certificate2 CreateSelfSignedLocalhost()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddServerExtensions(request);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    internal static X509Certificate2 CreateTestCa()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=BoltSharp-B5BQ1-Reviewer-CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(8));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    internal static X509Certificate2 CreateCaSignedLocalhost(X509Certificate2 ca)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddServerExtensions(request);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        var certificate = request.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7), serial);
        return new X509Certificate2(certificate.CopyWithPrivateKey(rsa).Export(X509ContentType.Pfx));
    }

    private static void AddServerExtensions(CertificateRequest request)
    {
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
    }
}
