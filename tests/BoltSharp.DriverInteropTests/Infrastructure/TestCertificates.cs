using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BoltSharp.DriverInteropTests.Infrastructure;

/// <summary>
/// In-memory, per-test TLS certificates for the B5-B TLS interoperability scenarios. Every
/// certificate is generated at runtime and never written to disk, never committed and never
/// installed into a machine trust store. The PFX round-trip is required so the certificate owns
/// a private key usable by SslStream on Windows.
/// </summary>
internal static class TestCertificates
{
    /// <summary>A self-signed localhost certificate (CN=localhost, SAN localhost/127.0.0.1).</summary>
    internal static X509Certificate2 CreateSelfSignedLocalhost()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddServerExtensions(request);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    /// <summary>A test-only CA certificate (basic constraint CA=true, keyCertSign).</summary>
    internal static X509Certificate2 CreateTestCa(string subject = "CN=BoltSharp-B5B-Test-CA")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        // The CA window must strictly contain the leaf window (leaf: now+7d).
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(8));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    /// <summary>A server leaf certificate signed by the test CA.</summary>
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
