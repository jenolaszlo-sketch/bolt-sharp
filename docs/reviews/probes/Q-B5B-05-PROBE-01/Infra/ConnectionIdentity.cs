// Q-B5B-05-PROBE-01 producer probe — the same-physical-connection oracle.
//
// Q-B5B-05's principal acceptance gate is physical connection identity. This oracle never relies
// on driver-instance reuse, logical driver sessions, query success, connection-pool membership or
// driver retry success. It combines five independent lines of evidence:
//
//   1. Server connection identity  — the monotonic per-accepted-socket connection id that the
//      production listener mints and carries on every diagnostics event. Exactly one accepted id
//      must exist, and every MessageProcessed event of the sequence must carry that same id.
//   2. Server connection counters  — accepted, active, completed, failed, messages processed.
//      A recoverable FAILURE is not a transport fault, so failed must stay 0 and the connection
//      must stay active (neither completed nor a second accepted).
//   3. Client socket identity      — the client's local ephemeral port, which necessarily changes
//      if the physical socket is replaced, plus the fact that no EOF or reset was ever observed.
//   4. Per-connection provider     — production creates one provider instance per negotiated
//      connection, so exactly one provider instance means exactly one physical connection.
//   5. Proxy transcript identity   — for plaintext flows, one proxied connection carrying every
//      message of the sequence in order.
//
// Any one of these lines could be misleading on its own; a replacement connection, an extra
// handshake or a driver retry over a fresh socket breaks at least one line each time.
using System.Net;
using System.Net.Sockets;

using BoltSharp.Transport;

namespace BoltSharp.Q5B05.Probe.Infra;

public sealed class SameConnectionVerdict
{
    public required IPEndPoint ClientLocalEndPoint { get; init; }

    public required string AcceptedConnectionId { get; init; }

    public required IReadOnlyList<string> MessageProcessedConnectionIds { get; init; }

    public required long ConnectionsAccepted { get; init; }

    public required long ConnectionsActive { get; init; }

    public required long ConnectionsCompleted { get; init; }

    public required long ConnectionsFailed { get; init; }

    public required long MessagesProcessed { get; init; }

    public required long ProviderInstanceCount { get; init; }

    public required bool ClientSocketStillOpen { get; init; }

    public required bool ClientLocalPortUnchanged { get; init; }

    /// <summary>Where in the connection lifecycle this verdict was captured.</summary>
    public required string VerdictPhase { get; init; }

    /// <summary>True when the connection had not yet completed at capture time.</summary>
    public required bool CapturedBeforeOrderlyCompletion { get; init; }

    public Guid? ProxyConnectionId { get; init; }

    public required long ProxyMessageCount { get; init; }

    public bool ExactlyOneAcceptedConnection => ConnectionsAccepted == 1;

    public bool EveryMessageOnThatConnection =>
        MessageProcessedConnectionIds.Count == 1
        && MessageProcessedConnectionIds[0] == AcceptedConnectionId;

    /// <summary>A recoverable FAILURE is not a transport fault.</summary>
    public bool ConnectionNeverFailed => ConnectionsFailed == 0;

    /// <summary>At most the one orderly completion produced by GOODBYE.</summary>
    public bool ConnectionAtMostOneOrderlyCompletion => ConnectionsCompleted <= 1;

    /// <summary>The connection was still admitted and active when this verdict was captured.</summary>
    public bool ConnectionStillActive => CapturedBeforeOrderlyCompletion && ConnectionsActive == 1;

    public bool ExactlyOneProviderInstance => ProviderInstanceCount == 1;

    /// <summary>
    /// The client socket identity held: the local ephemeral port is unchanged, and (when the
    /// verdict was captured before an orderly GOODBYE) the socket was still open. A replacement
    /// physical connection necessarily changes the port.
    /// </summary>
    public bool ClientSocketIdentityHeld =>
        ClientLocalPortUnchanged
        && (!CapturedBeforeOrderlyCompletion || ClientSocketStillOpen);

    public bool SamePhysicalConnection =>
        ExactlyOneAcceptedConnection
        && EveryMessageOnThatConnection
        && ConnectionNeverFailed
        && ConnectionAtMostOneOrderlyCompletion
        && ExactlyOneProviderInstance
        && ClientSocketIdentityHeld;

    public string Summary => string.Join("; ", new[]
    {
        $"acceptedConnectionId={AcceptedConnectionId}",
        $"distinctAcceptedIds={ConnectionsAccepted}",
        $"messageProcessedIds=[{string.Join(",", MessageProcessedConnectionIds)}]",
        $"messagesProcessed={MessagesProcessed}",
        $"active={ConnectionsActive}",
        $"completed={ConnectionsCompleted}",
        $"failed={ConnectionsFailed}",
        $"providerInstances={ProviderInstanceCount}",
        $"clientLocalPort={ClientLocalEndPoint.Port}",
        $"clientSocketStillOpen={ClientSocketStillOpen}",
        $"clientLocalPortUnchanged={ClientLocalPortUnchanged}",
        $"proxyConnectionId={ProxyConnectionId?.ToString() ?? "<not-observed>"}",
        $"proxyMessageCount={ProxyMessageCount}",
        $"SAME_PHYSICAL_CONNECTION={SamePhysicalConnection}",
    });
}

/// <summary>
/// Recording proxy between a client and the server under test. Each physical connection it accepts
/// receives an identity; every direction is relayed byte-for-byte and recorded at the message
/// level with a timestamp. LOGON payloads are redacted and never stored.
/// </summary>
public sealed class RecordingProxy : IAsyncDisposable
{
    private readonly IPEndPoint _target;
    private readonly List<ProxiedConnection> _connections = new();
    private readonly object _gate = new();

    private RecordingProxy(IPEndPoint target, IPEndPoint bound)
    {
        _target = target;
        BoundEndPoint = bound;
    }

    public IPEndPoint BoundEndPoint { get; }

    public string HostPort => BoundEndPoint.ToString();

    public static RecordingProxy Start(IPEndPoint target)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var proxy = new RecordingProxy(target, (IPEndPoint)listener.LocalEndpoint);
        _ = Task.Run(() => proxy.AcceptLoopAsync(listener));
        return proxy;
    }

    /// <summary>Snapshot of every proxied connection, including in-flight ones.</summary>
    public IReadOnlyList<ProxiedConnection> Connections()
    {
        lock (_gate)
        {
            return _connections.ToArray();
        }
    }

    public int ConnectionCount => Connections().Count;

    public async Task WaitForConnectionCountAsync(int count, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            if (ConnectionCount >= count)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"expected {count} proxied connection(s), observed {ConnectionCount}");
    }

    public async ValueTask DisposeAsync() => await Task.CompletedTask;

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch
            {
                return;
            }

            var connection = new ProxiedConnection(Guid.NewGuid(), Interlocked.Increment(ref _counter));
            lock (_gate)
            {
                _connections.Add(connection);
            }

            var relay = this;
            _ = Task.Run(() => relay.RelayAsync(client, connection));
        }
    }

    private static int _counter;

    private async Task RelayAsync(TcpClient client, ProxiedConnection connection)
    {
        try
        {
            using var clientSocket = client;
            using var serverSocket = new TcpClient();
            await serverSocket.ConnectAsync(_target.Address, _target.Port);
            await using var clientStream = clientSocket.GetStream();
            await using var serverStream = serverSocket.GetStream();

            var pumpInbound = PumpAsync(clientStream, serverStream, connection, clientToServer: true);
            var pumpOutbound = PumpAsync(serverStream, clientStream, connection, clientToServer: false);
            await Task.WhenAny(pumpInbound, pumpOutbound);
        }
        catch
        {
            // A proxy fault ends the relay; whatever was recorded so far is retained as evidence.
        }
    }

    private static async Task PumpAsync(
        Stream source,
        Stream sink,
        ProxiedConnection connection,
        bool clientToServer)
    {
        var buffer = new byte[64 * 1024];
        var skipper = new BoltHandshakeSkipper(clientToServer);
        var deframer = new ChunkDeframer();
        var handshakeDone = false;

        while (true)
        {
            int read;
            try
            {
                read = await source.ReadAsync(buffer);
            }
            catch
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            var remaining = buffer.AsSpan(0, read).ToArray();
            if (!handshakeDone)
            {
                remaining = skipper.Feed(remaining, out var done);
                handshakeDone = done;
            }

            if (remaining.Length > 0)
            {
                foreach (var message in deframer.Feed(remaining))
                {
                    connection.Record(clientToServer, message);
                }
            }

            try
            {
                await sink.WriteAsync(buffer.AsMemory(0, read));
            }
            catch
            {
                break;
            }
        }
    }

    private sealed class BoltHandshakeSkipper
    {
        private readonly bool _clientToServer;
        private readonly List<byte> _pending = new();
        private int _phase;

        public BoltHandshakeSkipper(bool clientToServer) => _clientToServer = clientToServer;

        public byte[] Feed(byte[] bytes, out bool done)
        {
            _pending.AddRange(bytes);
            if (_phase == 0)
            {
                // Only the client sends the magic preamble; the server never does.
                if (_clientToServer)
                {
                    if (_pending.Count < 20)
                    {
                        done = false;
                        return [];
                    }

                    _pending.RemoveRange(0, 20);
                }

                _phase = 1;
            }

            if (_phase == 1)
            {
                if (_clientToServer)
                {
                    // A manifest selection would begin with the selected version 00 00 08 05
                    // followed by a capability varint; a legacy client sends no reply at all. This
                    // probe offers exactly one concrete proposal, so no selection bytes exist.
                    if (_pending.Count < 4)
                    {
                        done = false;
                        return [];
                    }

                    if (_pending[0] == 0x00 && _pending[1] == 0x00
                        && _pending[2] == 0x08 && _pending[3] == 0x05)
                    {
                        if (_pending.Count < 5)
                        {
                            done = false;
                            return [];
                        }

                        _pending.RemoveRange(0, 5);
                    }
                }
                else
                {
                    // The server answers either with the 4-byte legacy version word or with a
                    // Manifest v1 offer (marker, range-count varint, 4-byte ranges, capability
                    // varint). A manifest offer never begins with the legacy word.
                    if (_pending.Count < 4)
                    {
                        done = false;
                        return [];
                    }

                    if (_pending[0] == 0x00 && _pending[1] == 0x00
                        && _pending[2] == 0x01 && _pending[3] == 0xFF)
                    {
                        if (_pending.Count < 5)
                        {
                            done = false;
                            return [];
                        }

                        var ranges = _pending[4];
                        if (_pending.Count < 5 + (4 * ranges) + 1)
                        {
                            done = false;
                            return [];
                        }

                        _pending.RemoveRange(0, 5 + (4 * ranges) + 1);
                    }
                    else
                    {
                        _pending.RemoveRange(0, 4);
                    }
                }

                _phase = 2;
            }

            done = true;
            var rest = _pending.ToArray();
            _pending.Clear();
            return rest;
        }
    }

    private sealed class ChunkDeframer
    {
        private readonly List<byte> _pending = new();
        private readonly List<byte> _message = new();

        public IEnumerable<byte[]> Feed(byte[] bytes)
        {
            _pending.AddRange(bytes);
            while (true)
            {
                if (_pending.Count < 2)
                {
                    yield break;
                }

                var length = (_pending[0] << 8) | _pending[1];
                if (length == 0)
                {
                    _pending.RemoveRange(0, 2);
                    if (_message.Count > 0)
                    {
                        var complete = _message.ToArray();
                        _message.Clear();
                        yield return complete;
                    }

                    continue;
                }

                if (_pending.Count < 2 + length)
                {
                    yield break;
                }

                _message.AddRange(_pending.Skip(2).Take(length));
                _pending.RemoveRange(0, 2 + length);
            }
        }
    }
}

/// <summary>One proxied physical connection and its ordered message record.</summary>
public sealed class ProxiedConnection
{
    private readonly object _gate = new();
    private readonly List<ProxiedMessage> _messages = new();

    public ProxiedConnection(Guid connectionId, int index)
    {
        ConnectionId = connectionId;
        Index = index;
    }

    public Guid ConnectionId { get; }

    public int Index { get; }

    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    public void Record(bool clientToServer, byte[] payload)
    {
        var tag = payload.Length >= 2 ? payload[1] : (byte)0;
        var isLogon = clientToServer && tag == ProbeMessages.LogonTag;
        lock (_gate)
        {
            _messages.Add(new ProxiedMessage(
                DateTimeOffset.UtcNow,
                clientToServer,
                ProbeMessages.Name(tag, clientToServer),
                tag,
                isLogon ? "REDACTED(LOGON)" : Convert.ToHexString(payload),
                isLogon));
        }
    }

    public IReadOnlyList<ProxiedMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    public int CountClient(string name) =>
        Messages.Count(m => m.ClientToServer && m.Name == name);

    public int CountServer(string name) =>
        Messages.Count(m => !m.ClientToServer && m.Name == name);

    public string Sequence() =>
        string.Join(" | ", Messages.Select(m =>
            $"{(m.ClientToServer ? "C2S" : "S2C")} {m.Name}{(m.Redacted ? "*" : "")} {m.Hex}"));

    public string SequenceLong() =>
        string.Join(" | ", Messages.Select(m =>
            $"{(m.ClientToServer ? "C2S" : "S2C")} {m.Name}{(m.Redacted ? "*" : "")} {m.Hex}"));
}

public sealed record ProxiedMessage(
    DateTimeOffset AtUtc,
    bool ClientToServer,
    string Name,
    byte Signature,
    string Hex,
    bool Redacted);