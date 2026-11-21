// Q-B5B-05-PROBE-01 producer probe — server host, per-connection provider identity, TLS
// certificates, diagnostics trace and evidence writer.
//
// The only hooks into production are the public BoltTcpServer/BoltTcpServerOptions surfaces and
// the public Server.Diagnostics event sink. No production source is modified.
//
// Evidence is credential-free by construction: LOGON payloads are never written (the proxy
// redacts them) and no verifier state contains secrets.
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using BoltSharp.Backend;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace BoltSharp.Q5B05.Probe.Infra;

/// <summary>A deterministic accepting credential verifier (loopback-only test values).</summary>
public sealed class ProbeVerifier : IBoltCredentialVerifier
{
    public bool Verify(LogonRequest request) => true;
}

/// <summary>
/// Recording wrapper around the accepted B3-C reference backend. One instance is created per
/// negotiated connection (production invokes the factory exactly once per connection at
/// BoltTcpConnection), so instance identity is a per-physical-connection oracle: a reused physical
/// connection produces exactly one provider instance.
/// </summary>
public sealed class CountingReferenceProvider : IBoltOperationProvider
{
    private readonly ReferenceOperationProvider _inner = new();

    public int PrepareCount;

    public int SuccessCount;

    public int RefusalCount;

    public int DisposeAttempts;

    /// <summary>When true the next Prepare throws, producing the recoverable 50N00 FAILURE.</summary>
    public bool ThrowOnNextPrepare { get; set; }

    public IBoltOperation? Prepare(RunRequest run)
    {
        Interlocked.Increment(ref PrepareCount);
        if (ThrowOnNextPrepare)
        {
            ThrowOnNextPrepare = false;
            throw new InvalidOperationException("probe fault: provider Prepare failure");
        }

        var operation = _inner.Prepare(run);
        if (operation is null)
        {
            Interlocked.Increment(ref RefusalCount);
            return null;
        }

        Interlocked.Increment(ref SuccessCount);
        return new CountingOperation(operation, this);
    }

    private sealed class CountingOperation : IBoltOperation
    {
        private readonly IBoltOperation _inner;
        private readonly CountingReferenceProvider _owner;

        public CountingOperation(IBoltOperation inner, CountingReferenceProvider owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public IReadOnlyList<string> Fields => _inner.Fields;

        public IReadOnlyList<PackStreamValue?>? ReadRow() => _inner.ReadRow();

        public void Dispose()
        {
            Interlocked.Increment(ref _owner.DisposeAttempts);
            _inner.Dispose();
        }
    }
}

/// <summary>
/// A per-connection provider log. Production calls the factory once per negotiated connection, so
/// the number of provider instances is the number of distinct physical connections, and each
/// instance's counters describe what ran on that connection.
/// </summary>
public sealed class ProbeProviderLog
{
    private readonly object _gate = new();
    private readonly List<CountingReferenceProvider> _providers = new();

    public Func<IBoltOperationProvider> Factory { get; }

    public ProbeProviderLog()
    {
        Func<CountingReferenceProvider> create = () =>
        {
            var provider = new CountingReferenceProvider();
            lock (_gate)
            {
                _providers.Add(provider);
            }

            return provider;
        };

        Factory = () => create();
    }

    public IReadOnlyList<CountingReferenceProvider> Providers
    {
        get
        {
            lock (_gate)
            {
                return _providers.ToArray();
            }
        }
    }

    /// <summary>The single provider instance, when exactly one physical connection was created.</summary>
    public CountingReferenceProvider? Single => Providers.Count == 1 ? Providers[0] : null;

    public int TotalPrepareCount => Providers.Sum(p => p.PrepareCount);

    public int TotalSuccessCount => Providers.Sum(p => p.SuccessCount);

    public int TotalRefusalCount => Providers.Sum(p => p.RefusalCount);

    /// <summary>Arms the first provider instance (the first negotiated connection) to fault.</summary>
    public void ArmFirstPrepareFault()
    {
        var first = Providers.Count > 0 ? Providers[0] : null;
        if (first is null)
        {
            throw new InvalidOperationException("no provider instance exists yet");
        }

        first.ThrowOnNextPrepare = true;
    }

    /// <summary>Arms every provider instance to fault on its next Prepare.</summary>
    public void ArmAllPrepareFaults()
    {
        foreach (var provider in Providers)
        {
            provider.ThrowOnNextPrepare = true;
        }
    }
}

/// <summary>
/// Diagnostics trace over the public <see cref="BoltTransportDiagnostics.Event"/> sink, recording
/// every event with its connection id so per-connection identity can be asserted.
/// </summary>
public sealed class ProbeTrace
{
    private readonly object _gate = new();
    private readonly List<BoltTransportDiagnosticEvent> _events = new();

    public ProbeTrace(BoltTcpServer server) => server.Diagnostics.Event += OnEvent;

    private void OnEvent(BoltTransportDiagnosticEvent e)
    {
        lock (_gate)
        {
            _events.Add(e);
        }
    }

    public IReadOnlyList<BoltTransportDiagnosticEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public IReadOnlyList<string> AcceptedConnectionIds =>
        Events.Where(e => e.Kind == BoltTransportEventKind.ConnectionAccepted)
            .Select(e => e.ConnectionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Counts MessageProcessed events whose detail carries the given signature.</summary>
    public int CountProcessedSignatures(string signature) =>
        Events.Count(e => e.Kind == BoltTransportEventKind.MessageProcessed
                          && e.Detail is not null
                          && e.Detail.Contains("sig=" + signature, StringComparison.Ordinal));

    public IReadOnlyList<string> MessageProcessedConnectionIds =>
        Events.Where(e => e.Kind == BoltTransportEventKind.MessageProcessed)
            .Select(e => e.ConnectionId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Every connection id recorded by the trace, in first-seen order.</summary>
    public IReadOnlyList<string> ConnectionIds()
    {
        var ids = new List<string>();
        foreach (var e in Events)
        {
            if (!ids.Contains(e.ConnectionId, StringComparer.Ordinal))
            {
                ids.Add(e.ConnectionId);
            }
        }

        return ids;
    }

    /// <summary>
    /// The connection id of the accepted connection whose remote endpoint port matches. The
    /// production listener records the accepted connection's remote endpoint, which is the peer
    /// client's local ephemeral port, so this maps a client socket to its server-side identity.
    /// </summary>
    public string? ConnectionIdForClientPort(int port)
    {
        lock (_gate)
        {
            foreach (var e in _events)
            {
                if (e.Kind != BoltTransportEventKind.ConnectionAccepted)
                {
                    continue;
                }

                if (e.Detail is not null && e.Detail.EndsWith(":" + port, StringComparison.Ordinal))
                {
                    return e.ConnectionId;
                }
            }
        }

        return null;
    }

    /// <summary>Counts MessageProcessed events for one connection carrying the given signature.</summary>
    public int CountProcessedSignatures(string connectionId, string signature) =>
        Events.Count(e => e.Kind == BoltTransportEventKind.MessageProcessed
                          && string.Equals(e.ConnectionId, connectionId, StringComparison.Ordinal)
                          && e.Detail is not null
                          && e.Detail.Contains("sig=" + signature, StringComparison.Ordinal));

    public string Render()
    {
        var builder = new StringBuilder();
        foreach (var e in Events)
        {
            builder.AppendLine($"{e.TimestampUtc:O}  {e.Kind}  {e.ConnectionId}  {e.Detail}");
        }

        return builder.ToString();
    }
}

/// <summary>A bounded polling helper for asynchronous, counter-based evidence.</summary>
public static class Poll
{
    public static async Task WaitAsync(Func<bool> condition, string description, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"condition not observed in time: {description}");
    }
}

public static class ProbeHost
{
    public const string Loopback = "127.0.0.1";

    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BoltSharp.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("repository root not found");
        }

        return dir.FullName;
    }

    public static string EvidenceDirectory() =>
        Path.Combine(RepositoryRoot(), "docs", "reviews", "evidence", "Q-B5B-05-PROBE-01");

    public static string ProbeDirectory() =>
        Path.Combine(RepositoryRoot(), "docs", "reviews", "probes", "Q-B5B-05-PROBE-01");

    /// <summary>Starts the real BoltSharp listener (plaintext or TLS) with the given provider log.</summary>
    public static async Task<(BoltTcpServer Server, ProbeTrace Trace)> StartServerAsync(
        ProbeProviderLog providers, bool tls = false, int maxConnections = 100)
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            CredentialVerifier = new ProbeVerifier(),
            OperationProviderFactory = providers.Factory,
            HandshakeOptions = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }),
            MaxConnections = maxConnections,
            Tls = tls ? new BoltTlsServerOptions(CreateSelfSignedLocalhost()) : null,
        });
        await server.StartAsync();
        return (server, new ProbeTrace(server));
    }

    /// <summary>Opens a raw connection, performs the legacy 5.8 handshake, HELLO and LOGON.</summary>
    public static async Task<ProbeClient> ConnectAndAuthorizeAsync(IPEndPoint endpoint, string user, string secret)
    {
        var client = new ProbeClient();
        await client.ConnectAsync(endpoint);
        var reply = await client.HandshakeAsync();
        if (reply != "00000805")
        {
            throw new InvalidOperationException($"handshake reply {reply} did not select 5.8");
        }

        client.SendFramed(ProbeMessages.Hello("Q-B5B-05-PROBE-01/1.0", "BoltSharp-Probe/1.0"));
        var hello = client.ReadMessage();
        if (hello is null || hello.Length < 2 || hello[1] != ProbeMessages.SuccessTag)
        {
            throw new InvalidOperationException("HELLO was not answered with SUCCESS");
        }

        client.SendFramed(ProbeMessages.Logon("basic", user, secret));
        var logon = client.ReadMessage();
        if (logon is null || logon.Length < 2 || logon[1] != ProbeMessages.SuccessTag)
        {
            throw new InvalidOperationException("LOGON was not answered with SUCCESS");
        }

        return client;
    }

    public static string Hex(byte[]? bytes) => bytes is null ? "<null>" : Convert.ToHexString(bytes);

    public static void WriteEvidence(string fileName, string content) =>
        WriteEvidence(fileName, content, "create");

    public static void AppendEvidence(string fileName, string content) =>
        WriteEvidence(fileName, content, "append");

    private static void WriteEvidence(string fileName, string content, string mode)
    {
        var directory = EvidenceDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (mode == "append" && File.Exists(path))
        {
            File.AppendAllText(path, content, new UTF8Encoding(false));
            return;
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    /// <summary>In-memory self-signed localhost certificate; never written to disk or a trust store.</summary>
    public static X509Certificate2 CreateSelfSignedLocalhost()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    public static string Summary(BoltTcpServer server)
    {
        var d = server.Diagnostics;
        return $"accepted={d.ConnectionsAccepted} active={d.ConnectionsActive} " +
               $"completed={d.ConnectionsCompleted} failed={d.ConnectionsFailed} " +
               $"rejectedCapacity={d.ConnectionsRejectedForCapacity} " +
               $"tlsFailed={d.TlsHandshakeFailures} boltHandshakeFailed={d.BoltHandshakeFailures} " +
               $"messagesProcessed={d.MessagesProcessed}";
    }
}