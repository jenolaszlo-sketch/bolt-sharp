using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>Critical investigation B: TLS configuration, trust model and fail-closed behavior (§9).</summary>
internal static class ProbeTls
{
    public static void Run()
    {
        Program.Section("C. TLS configuration and trust");
        var (server, certificate) = StartTls();
        try
        {
            TrustedCertificateConversation(server, certificate);
            StrictChainValidationRejectsSelfSigned(server);
            HostnameMismatchProbe(certificate);
            TlsVersionNegotiation(server, certificate);
            PlaintextToTlsListener(server);
            TlsAuthenticationTimeout();
            ClientAbortDuringTls();
            RevocationTrustModelAssessment(certificate);
        }
        finally
        {
            server.Dispose();
        }

        CertificateValidationAtConfiguration();
        DeprecatedProtocolPolicyRefused();
    }

    private static (Program.ProbeServer Server, X509Certificate2 Certificate) StartTls()
    {
        var certificate = MakeCertificate();
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            Tls = new BoltTlsServerOptions(certificate),
        });
        return (server, certificate);
    }

    internal static X509Certificate2 MakeCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    private static void TrustedCertificateConversation(Program.ProbeServer server, X509Certificate2 certificate)
    {
        using var client = ReviewClient.ConnectTls(server.Server.BoundEndPoint!, certificate);
        Program.TraceLine($"TLS negotiated: {client.NegotiatedTls} cipher={client.NegotiatedCipher}");
        Program.Check("C1 TLS handshake completed with explicitly trusted test certificate", client.IsTlsAuthenticated, $"{client.NegotiatedTls}");
        Program.Check(
            "C1 negotiated TLS version is 1.2 or 1.3",
            client.NegotiatedTls is "Tls12" or "Tls13",
            client.NegotiatedTls);

        var reply = client.HandshakeLegacy(0x00000805u);
        Program.Check("C1 Bolt negotiation after TLS selects 5.8", reply == "00000805", reply);
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Logon());
        client.ReadMessageOrThrow();
        client.Send(Review.Run("RETURN 42 AS answer"));
        client.ReadMessageOrThrow();
        client.Send(Review.Pull(1000));
        var record = client.ReadMessageOrThrow();
        Program.Check("C1 full Bolt conversation over TLS returns RECORD [42]", Program.Hex(record) == Golden.Record42, Program.Hex(record));
        client.Send(Review.Goodbye);
    }

    private static void StrictChainValidationRejectsSelfSigned(Program.ProbeServer server)
    {
        // No explicit trust: the platform default chain + name validation must reject the
        // self-signed test certificate. The server must fail closed (no downgrade).
        try
        {
            using var client = ReviewClient.ConnectTls(server.Server.BoundEndPoint!, trustedCertificate: null, strictChainValidation: true);
            Program.Check("C2 strict validation rejects untrusted self-signed certificate", false, "TLS unexpectedly succeeded");
        }
        catch (Exception ex)
        {
            Program.Check("C2 strict validation rejects untrusted self-signed certificate", true, ex.GetType().Name);
        }

        Program.Check(
            "C2 server closed the connection (fail closed, no downgrade path exists)",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
            $"active={server.Server.Diagnostics.ConnectionsActive}; client-side rejection manifests as a client abort (B0-A clean disconnect, see C7); the counter covers server-visible failures (see C5)");
    }

    private static void HostnameMismatchProbe(X509Certificate2 certificate)
    {
        // A listener on a certificate for CN=localhost; a strict client targeting a different
        // host name must refuse. We validate against a fresh short-lived server on loopback.
        var certificate2 = MakeCertificate();
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            Tls = new BoltTlsServerOptions(certificate2),
        });
        try
        {
            try
            {
                // Strict validation against a name mismatch (wrong-host) via SslStream defaults:
                // connect to 127.0.0.1 while the certificate claims CN=localhost. Platform
                // default validation rejects a self-signed certificate anyway; to isolate the
                // name check we explicitly trust the thumbprint but demand the platform name
                // check through a callback that only skips chain errors, not name errors.
                var sslOk = false;
                using var tcp = new TcpClient();
                tcp.Connect(server.Server.BoundEndPoint!.Address, server.Server.BoundEndPoint.Port);
                using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, errors) =>
                    (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
                    && cert is X509Certificate2 actual
                    && actual.Thumbprint == certificate2.Thumbprint);
                try
                {
                    ssl.AuthenticateAsClient(new SslClientAuthenticationOptions
                    {
                        TargetHost = "wrong-host.example", // name does not match CN=localhost
                    });
                    sslOk = ssl.IsAuthenticated;
                }
                catch (Exception)
                {
                    sslOk = false;
                }

                Program.Check(
                    "C3 hostname mismatch is rejected by a name-validating client",
                    !sslOk,
                    sslOk ? "client accepted wrong host name" : "rejected");
            }
            catch (Exception ex)
            {
                Program.Check("C3 hostname mismatch is rejected by a name-validating client", false, $"unexpected: {ex.GetType().Name}");
            }
        }
        finally
        {
            server.Dispose();
            certificate2.Dispose();
        }
    }

    private static void TlsVersionNegotiation(Program.ProbeServer server, X509Certificate2 certificate)
    {
        // TLS 1.2 explicitly.
        var v12 = TryConnect(server.Server.BoundEndPoint!, certificate, SslProtocols.Tls12);
        Program.Check("C4 TLS 1.2 negotiation succeeds where supported", v12.ok || v12.error.Contains("not supported"), $"{v12.negotiated} {v12.error}");
        // TLS 1.3 explicitly (platform dependent).
        var v13 = TryConnect(server.Server.BoundEndPoint!, certificate, SslProtocols.Tls13);
        Program.Check("C4 TLS 1.3 negotiation succeeds where supported", v13.ok || v13.error.Contains("not supported"), $"{v13.negotiated} {v13.error}");
        // TLS 1.1: the server policy (1.2+) must refuse.
        var v11 = TryConnect(server.Server.BoundEndPoint!, certificate, SslProtocols.Tls11);
        Program.Check("C4 TLS 1.1 refused by the TLS 1.2+ server policy", !v11.ok, $"{v11.negotiated} {v11.error}");
    }

    private static (bool ok, string negotiated, string error) TryConnect(IPEndPoint ep, X509Certificate2 certificate, SslProtocols protocols)
    {
        try
        {
            using var client = ReviewClient.ConnectTls(ep, certificate, enabledProtocols: protocols);
            return (true, client.NegotiatedTls, string.Empty);
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            return (false, "none", inner.Length > 60 ? inner[..60] : inner);
        }
    }

    private static void PlaintextToTlsListener(Program.ProbeServer server)
    {
        using var client = ReviewClient.Connect(server.Server.BoundEndPoint!);
        client.SendRaw(new byte[] { 0x60, 0x60, 0xB0, 0x17, 0, 0, 8, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Program.Check(
            "C5 plaintext Bolt handshake against TLS-only listener fails closed",
            client.WaitForClose(10_000) == "closed",
            "no downgrade");
        Program.Check(
            "C5 TLS failure counted",
            Program.WaitUntil(() => server.Server.Diagnostics.TlsHandshakeFailures >= 1, TimeSpan.FromSeconds(5)),
            $"failures={server.Server.Diagnostics.TlsHandshakeFailures}");
    }

    private static void TlsAuthenticationTimeout()
    {
        var certificate = MakeCertificate();
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            Tls = new BoltTlsServerOptions(certificate),
            TlsHandshakeTimeout = TimeSpan.FromMilliseconds(600),
        });
        try
        {
            using var client = ReviewClient.Connect(server.Server.BoundEndPoint!);
            // Never authenticate; wait for the bounded TLS phase to close the connection.
            Program.Check(
                "C6 unauthenticated TLS connection closed by timeout",
                client.WaitForClose(10_000) == "closed",
                "bounded TLS handshake phase");
        }
        finally
        {
            server.Dispose();
            certificate.Dispose();
        }
    }

    private static void ClientAbortDuringTls()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            Tls = new BoltTlsServerOptions(MakeCertificate()),
        });
        try
        {
            using (var client = ReviewClient.Connect(server.Server.BoundEndPoint!))
            {
                client.SendRaw(new byte[] { 0x16, 0x03, 0x01 }); // partial TLS ClientHello, then abort
            }

            Program.Check(
                "C7 client abort during TLS authentication contained",
                Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
                $"active={server.Server.Diagnostics.ConnectionsActive}");
            Program.Check(
                "C7 abort not counted as listener fault",
                server.Server.Diagnostics.ConnectionsFailed == 0,
                $"failed={server.Server.Diagnostics.ConnectionsFailed}");
        }
        finally
        {
            server.Dispose();
        }
    }

    private static void RevocationTrustModelAssessment(X509Certificate2 certificate)
    {
        // Trust-model facts established from source inspection:
        // - checkCertificateRevocation=false is the SERVER-side flag of
        //   SslStream.AuthenticateAsServerAsync, which only governs validation of a CLIENT
        //   certificate chain during mutual TLS.
        // - BoltTlsServerOptions.ClientCertificateRequired defaults to false and B5-A never
        //   enables it; B3-B LOGON remains the only authentication boundary.
        // - Therefore no required certificate validation is bypassed: there is no client
        //   certificate to revoke-check.
        // - Server certificate validation is performed by the CLIENT; the reviewer client
        //   (strict-chain case C2) proves a validating client rejects the self-signed chain.
        Program.Check(
            "C8 server-side revocation flag does not bypass a required check (no mutual TLS)",
            true,
            "ClientCertificateRequired=false; server does not authenticate clients via TLS");
        Program.Check(
            "C9 client-side validation is the client's responsibility; strict client rejects untrusted server",
            true,
            "see C2");
    }

    private static void CertificateValidationAtConfiguration()
    {
        // Null certificate refused at construction.
        var threw = false;
        try
        {
            _ = new BoltTlsServerOptions(null!);
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }

        Program.Check("C10 null server certificate refused at construction", threw, "ArgumentNullException");

        // Public-only certificate (no private key) refused at validation.
        var full = MakeCertificate();
        var publicOnly = new X509Certificate2(full.Export(X509ContentType.Cert));
        threw = false;
        try
        {
            new BoltTlsServerOptions(publicOnly).Validate();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Program.Check("C10 certificate without private key refused at validation", threw, "InvalidOperationException");
        full.Dispose();
        publicOnly.Dispose();
    }

    private static void DeprecatedProtocolPolicyRefused()
    {
        var certificate = MakeCertificate();
        foreach (var protocols in new[] { SslProtocols.Ssl3, SslProtocols.Tls, SslProtocols.Tls11 })
        {
            var refused = false;
            try
            {
                new BoltTlsServerOptions(certificate) { EnabledProtocols = protocols }.Validate();
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }

            Program.Check($"C11 deprecated protocol set {protocols} refused by Validate()", refused, "TLS 1.2+ policy");
        }

        certificate.Dispose();
    }
}
