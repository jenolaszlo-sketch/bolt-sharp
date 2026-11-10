using System.Net;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Handshake;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>Snapshot of the accepted B5-A transport counters (reviewer copy of the public surface).</summary>
internal readonly record struct Q1ServerCounters(
    long Accepted,
    long RejectedForCapacity,
    long Active,
    long Completed,
    long Failed,
    long TlsHandshakeFailures,
    long BoltHandshakeFailures,
    long MessagesProcessed);

/// <summary>
/// Reviewer-owned test host for the genuine accepted B5-A BoltTcpServer: loopback bind, ephemeral
/// port, explicit Bolt 5.8 handshake options, reviewer credential verifier, and (optionally) the
/// reviewer's per-connection provider factory. Everything is injected through the public
/// BoltTcpServerOptions surface; no production source is modified.
/// </summary>
internal sealed class Q1Server : IAsyncDisposable
{
    private readonly List<BoltTransportDiagnosticEvent> _events = new();
    private readonly object _lock = new();

    private Q1Server(BoltTcpServer server)
    {
        Server = server;
        Server.Diagnostics.Event += OnEvent;
    }

    internal BoltTcpServer Server { get; }

    internal IPEndPoint Endpoint => Server.BoundEndPoint!;

    internal string HostPort => $"localhost:{Endpoint.Port}";

    internal Q1ProviderLog? Providers { get; private set; }

    internal static async Task<Q1Server> StartPlaintextAsync(
        IBoltCredentialVerifier? verifier,
        bool manifestV1Enabled = true,
        Q1ProviderLog? providers = null)
    {
        // When no reviewer provider log is supplied, preserve the transport's accepted default
        // provider (the B3-C reference backend); null would fail closed at the session stage.
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            CredentialVerifier = verifier,
            OperationProviderFactory = providers?.Factory ?? (() => new BoltSharp.Backend.ReferenceOperationProvider()),
            HandshakeOptions = BoltHandshakeOptions.Create(
                new[] { new BoltProtocolVersion(5, 8) },
                manifestV1Enabled: manifestV1Enabled),
        });
        await server.StartAsync();
        return new Q1Server(server) { Providers = providers };
    }

    internal static async Task<Q1Server> StartTlsAsync(
        X509Certificate2 serverCertificate,
        IBoltCredentialVerifier? verifier,
        bool manifestV1Enabled = true,
        Q1ProviderLog? providers = null)
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            Tls = new BoltTlsServerOptions(serverCertificate),
            CredentialVerifier = verifier,
            OperationProviderFactory = providers?.Factory ?? (() => new BoltSharp.Backend.ReferenceOperationProvider()),
            HandshakeOptions = BoltHandshakeOptions.Create(
                new[] { new BoltProtocolVersion(5, 8) },
                manifestV1Enabled: manifestV1Enabled),
        });
        await server.StartAsync();
        return new Q1Server(server) { Providers = providers };
    }

    /// <summary>A server advertising no supported revision (answers the zero word).</summary>
    internal static async Task<Q1Server> StartRejectAllAsync()
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            CredentialVerifier = Q1Credentials.Verifier,
            HandshakeOptions = BoltHandshakeOptions.Default,
        });
        await server.StartAsync();
        return new Q1Server(server);
    }

    internal Q1ServerCounters Counters() => new(
        Accepted: Server.Diagnostics.ConnectionsAccepted,
        RejectedForCapacity: Server.Diagnostics.ConnectionsRejectedForCapacity,
        Active: Server.Diagnostics.ConnectionsActive,
        Completed: Server.Diagnostics.ConnectionsCompleted,
        Failed: Server.Diagnostics.ConnectionsFailed,
        TlsHandshakeFailures: Server.Diagnostics.TlsHandshakeFailures,
        BoltHandshakeFailures: Server.Diagnostics.BoltHandshakeFailures,
        MessagesProcessed: Server.Diagnostics.MessagesProcessed);

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

    /// <summary>The number of messages processed with a given request signature (hex form, e.g. "0x10").</summary>
    internal int CountProcessedSignatures(string signatureHex)
    {
        return Events().Count(e =>
            e.Kind == BoltTransportEventKind.MessageProcessed &&
            (e.Detail ?? string.Empty).Contains($"sig={signatureHex}", StringComparison.Ordinal));
    }

    internal async Task<bool> WaitForCountersAsync(Func<Q1ServerCounters, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition(Counters()))
            {
                return true;
            }

            await Task.Delay(20);
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
            await Server.StopAsync();
        }
        finally
        {
            await Server.DisposeAsync();
        }
    }
}
