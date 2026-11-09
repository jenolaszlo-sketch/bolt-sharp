using System.Net;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Handshake;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace BoltSharp.DriverInteropTests.Infrastructure;

/// <summary>
/// A test-only host for the genuine accepted B5-A <see cref="BoltTcpServer"/>: a loopback bind on
/// an ephemeral port, the accepted B3-C reference provider (the transport default), the B3-B
/// default session configuration (only the verifier is injected by the transport), and a
/// deterministic credential verifier. Records the server-side diagnostic events for evidence.
/// </summary>
internal sealed class B5BServerHost : IAsyncDisposable
{
    private readonly List<BoltTransportDiagnosticEvent> _events = new();
    private readonly object _lock = new();

    private B5BServerHost(BoltTcpServer server)
    {
        Server = server;
        Server.Diagnostics.Event += OnEvent;
    }

    internal BoltTcpServer Server { get; }

    internal IPEndPoint Endpoint => Server.BoundEndPoint!;

    internal string HostPort => $"localhost:{Endpoint.Port}";

    /// <summary>Starts a plaintext loopback server (the B5-A default secure configuration).</summary>
    internal static async Task<B5BServerHost> StartPlaintextAsync(
        IBoltCredentialVerifier? verifier,
        bool manifestV1Enabled = true)
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            CredentialVerifier = verifier,
            HandshakeOptions = BoltHandshakeOptions.Create(
                new[] { new BoltProtocolVersion(5, 8) },
                manifestV1Enabled: manifestV1Enabled),
        });
        await server.StartAsync().ConfigureAwait(false);
        return new B5BServerHost(server);
    }

    /// <summary>Starts a TLS loopback server with the supplied server certificate.</summary>
    internal static async Task<B5BServerHost> StartTlsAsync(
        X509Certificate2 serverCertificate,
        IBoltCredentialVerifier? verifier,
        bool manifestV1Enabled = true)
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            Tls = new BoltTlsServerOptions(serverCertificate),
            CredentialVerifier = verifier,
            HandshakeOptions = BoltHandshakeOptions.Create(
                new[] { new BoltProtocolVersion(5, 8) },
                manifestV1Enabled: manifestV1Enabled),
        });
        await server.StartAsync().ConfigureAwait(false);
        return new B5BServerHost(server);
    }

    /// <summary>
    /// Starts a server that advertises no supported version (B2 version honesty): every handshake
    /// proposal is answered with the zero word and the connection closed.
    /// </summary>
    internal static async Task<B5BServerHost> StartRejectAllAsync()
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            CredentialVerifier = TestCredentials.Verifier,
            HandshakeOptions = BoltHandshakeOptions.Default,
        });
        await server.StartAsync().ConfigureAwait(false);
        return new B5BServerHost(server);
    }

    /// <summary>Snapshot of the accepted transport counters.</summary>
    internal ServerCounters Counters() => new(
        Accepted: Server.Diagnostics.ConnectionsAccepted,
        RejectedForCapacity: Server.Diagnostics.ConnectionsRejectedForCapacity,
        Active: Server.Diagnostics.ConnectionsActive,
        Completed: Server.Diagnostics.ConnectionsCompleted,
        Failed: Server.Diagnostics.ConnectionsFailed,
        TlsHandshakeFailures: Server.Diagnostics.TlsHandshakeFailures,
        BoltHandshakeFailures: Server.Diagnostics.BoltHandshakeFailures,
        MessagesProcessed: Server.Diagnostics.MessagesProcessed);

    /// <summary>A stable copy of the recorded diagnostic events.</summary>
    internal IReadOnlyList<BoltTransportDiagnosticEvent> Events()
    {
        lock (_lock)
        {
            return _events.ToArray();
        }
    }

    /// <summary>The negotiated handshake mode reported by the transport, or "none".</summary>
    internal string ObservedHandshakeMode()
    {
        var completed = Events().FirstOrDefault(
            e => e.Kind == BoltTransportEventKind.BoltHandshakeCompleted);
        if (completed?.Detail is null)
        {
            return "none";
        }

        const string marker = "mode=";
        var index = completed.Detail.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? "none" : completed.Detail[(index + marker.Length)..].Trim();
    }

    /// <summary>Polls the counter snapshot until the condition holds or the bound expires.</summary>
    internal async Task<bool> WaitForCountersAsync(Func<ServerCounters, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition(Counters()))
            {
                return true;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return condition(Counters());
    }

    private void OnEvent(BoltTransportDiagnosticEvent diagnosticEvent)
    {
        lock (_lock)
        {
            _events.Add(diagnosticEvent);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Server.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await Server.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal readonly record struct ServerCounters(
    long Accepted,
    long RejectedForCapacity,
    long Active,
    long Completed,
    long Failed,
    long TlsHandshakeFailures,
    long BoltHandshakeFailures,
    long MessagesProcessed);
