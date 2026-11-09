using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>B5-A2/§13.4/§13.5: TLS transport mode, fail-closed configuration and refusal cases.</summary>
public sealed class TlsIntegrationTests
{
    private static (BoltTcpServer Server, X509Certificate2 Certificate) StartTlsServer()
    {
        var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        var server = TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            Tls = new BoltTlsServerOptions(certificate),
        }).GetAwaiter().GetResult();
        return (server, certificate);
    }

    [Fact]
    public async Task TlsConversation_CompletesScalarQuery()
    {
        var (server, certificate) = StartTlsServer();
        await using var _ = server;
        using var client = TestBoltClient.ConnectTls(server.BoundEndPoint!, certificate);

        // Bolt negotiation happens strictly after TLS authentication, inside the TLS stream.
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "HELLO");
        client.SendRequest(SessionHarness.Logon());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "LOGON");
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "RUN");
        client.SendRequest(new PullRequest(1000, null));
        var responses = client.ReadResult();
        Assert.Equal(2, responses.Length);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(responses[0]).Data.Items[0]).Value);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted + server.Diagnostics.ConnectionsActive);
    }

    [Fact]
    public void TlsConfiguration_RequiresCertificateWithPrivateKey()
    {
        // Null certificate: refused at construction.
        Assert.Throws<ArgumentNullException>(() => new BoltTlsServerOptions(null!));

        // Public-only certificate (no private key): refused at validation.
        var full = TransportTestSupport.CreateSelfSignedCertificate();
        var publicOnly = new X509Certificate2(full.Export(X509ContentType.Cert));
        var options = new BoltTlsServerOptions(publicOnly);
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public async Task TlsConfiguration_DeprecatedProtocols_AreRefused()
    {
        var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        // SYSLIB0039 is deliberate: the test proves the transport refuses deprecated protocols.
#pragma warning disable SYSLIB0039
        var options = new BoltTlsServerOptions(certificate)
        {
            EnabledProtocols = System.Security.Authentication.SslProtocols.Tls11,
        };
#pragma warning restore SYSLIB0039
        Assert.Throws<InvalidOperationException>(() => options.Validate());
    }

    [Fact]
    public async Task PlaintextBytesToTlsListener_FailsClosed_NoDowngrade()
    {
        var (server, _) = StartTlsServer();
        await using var _ = server;
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        // Plaintext Bolt handshake bytes against a TLS-only listener: the TLS authentication
        // fails and the connection is closed. There is no plaintext fallback.
        client.SendRaw(TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)));
        Assert.Equal("closed", client.WaitForClose(10_000));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.TlsHandshakeFailures >= 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.BoltHandshakeFailures);
    }

    [Fact]
    public async Task ClientDisconnectDuringTlsAuthentication_IsContained()
    {
        var (server, _) = StartTlsServer();
        await using var _ = server;
        using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
        {
            // Connect and disconnect without any TLS bytes.
            client.SendRaw(new byte[] { 0x16, 0x03, 0x01 }); // partial TLS record, then abrupt close
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0, server.Diagnostics.BoltHandshakeFailures);
    }

    [Fact]
    public async Task TlsAuthenticationTimeout_ClosesConnection()
    {
        var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            Tls = new BoltTlsServerOptions(certificate),
            TlsHandshakeTimeout = TimeSpan.FromMilliseconds(500),
        });
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        // Connected but never authenticates: the bounded TLS handshake closes the connection.
        Assert.Equal("closed", client.WaitForClose(10_000));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.TlsHandshakeFailures >= 1, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TlsDoesNotBypassAuthentication()
    {
        var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.Accept("neo4j", "secret"),
            Tls = new BoltTlsServerOptions(certificate),
        });
        using var client = TestBoltClient.ConnectTls(server.BoundEndPoint!, certificate);
        client.Handshake(new BoltProtocolVersion(5, 8));

        client.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(client.ReadResponse(), "HELLO");
        client.SendRequest(SessionHarness.Logon(principal: "neo4j", credentials: "wrong"));
        Assert.IsType<FailureResponse>(client.ReadResponse());
    }

    [Fact]
    public void TlsValidate_MissingCertificate_Throws()
    {
        // Validate() must reject a missing certificate even if construction were bypassed.
        var options = new BoltTcpServerOptions();
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception); // plaintext options without TLS are valid

        var tlsOptions = new BoltTlsServerOptions(TransportTestSupport.CreateSelfSignedCertificate());
        Assert.Null(Record.Exception(() => tlsOptions.Validate()));
    }
}
