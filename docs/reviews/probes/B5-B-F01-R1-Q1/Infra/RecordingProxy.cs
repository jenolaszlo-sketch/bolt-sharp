// B5-B-F01-R1-Q1 reviewer probe — a recording TCP proxy between official-driver clients and the
// BoltTcpServer under test. Each physical connection gets an identity (GUID); both directions are
// recorded at the raw-chunk level with credential redaction, and also at the message level after
// independent deframing. LOGON message payloads are redacted (never stored).
namespace BoltSharp.B5BF01R1Q1.Probes.Infra;

using System.Net;
using System.Net.Sockets;

public sealed record ProxyRecordedMessage(bool ClientToServer, string Name, byte Tag, string Hex, bool Redacted);

public sealed record ProxyTranscript(Guid ConnectionId, IReadOnlyList<ProxyRecordedMessage> Messages)
{
    public int CountClient(string name) => Messages.Count(m => m.ClientToServer && m.Name == name);

    public int CountServer(string name) => Messages.Count(m => !m.ClientToServer && m.Name == name);
}

public sealed class RecordingProxy : IAsyncDisposable
{
    private readonly IPEndPoint _target;
    private readonly List<ProxyTranscript> _transcripts = new();
    private readonly object _lock = new();
    private int _connectionCounter;

    private RecordingProxy(IPEndPoint target, IPEndPoint bound)
    {
        _target = target;
        BoundEndPoint = bound;
    }

    public IPEndPoint BoundEndPoint { get; }

    public static async Task<RecordingProxy> StartAsync(IPEndPoint target)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var proxy = new RecordingProxy(target, (IPEndPoint)listener.LocalEndpoint);
        _ = Task.Run(() => proxy.AcceptLoopAsync(listener));
        return proxy;
    }

    public IReadOnlyList<ProxyTranscript> Transcripts()
    {
        lock (_lock)
        {
            return _transcripts.ToArray();
        }
    }

    public ProxyTranscript FindConnection(Func<ProxyTranscript, bool> predicate)
    {
        lock (_lock)
        {
            return _transcripts.Single(predicate);
        }
    }

    /// <summary>Waits until a completed transcript matches the predicate (relays finish slightly
    /// after the driver disposes its connections).</summary>
    public async Task<ProxyTranscript> WaitForTranscriptAsync(Func<ProxyTranscript, bool> predicate, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                var match = _transcripts.SingleOrDefault(predicate);
                if (match is not null)
                {
                    return match;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("no matching transcript completed in time");
    }

    public async Task WaitForConnectionsAsync(int count, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                if (_transcripts.Count >= count)
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"expected {count} proxy connections within {within}");
    }

    public ValueTask DisposeAsync()
    {
        // The accept loop ends when its listener faults; nothing else to release here.
        return ValueTask.CompletedTask;
    }

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
                return; // listener stopped
            }

            var connectionId = Guid.NewGuid();
            var transcriptId = Interlocked.Increment(ref _connectionCounter);
            _ = RelayAsync(client, connectionId, transcriptId);
        }
    }

    private async Task RelayAsync(TcpClient client, Guid connectionId, int transcriptId)
    {
        var messages = new List<ProxyRecordedMessage>();
        try
        {
            using var clientSocket = client;
            using var serverSocket = new TcpClient();
            await serverSocket.ConnectAsync(_target);
            await using var clientStream = clientSocket.GetStream();
            await using var serverStream = serverSocket.GetStream();

            var c2s = PumpAsync(clientStream, serverStream, clientToServer: true, messages, transcriptId);
            var s2c = PumpAsync(serverStream, clientStream, clientToServer: false, messages, transcriptId);
            await Task.WhenAny(c2s, s2c);
            try
            {
                clientSocket.Client.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                // best effort
            }
        }
        catch
        {
            // proxy faults end the relay; transcripts recorded so far are kept
        }
        finally
        {
            lock (_lock)
            {
                _transcripts.Add(new ProxyTranscript(connectionId, messages));
            }
        }
    }

    private static async Task PumpAsync(
        Stream source,
        Stream sink,
        bool clientToServer,
        List<ProxyRecordedMessage> messages,
        int transcriptId)
    {
        var buffer = new byte[64 * 1024];
        var deframer = new ChunkDeframer();

        // Bolt handshake handling. The client sends magic (4) + four proposals (16) = 20 bytes;
        // in Manifest v1 mode it then sends a 5-byte selection (4-byte version + capability
        // varint). The server replies either with a 4-byte legacy version or with a manifest
        // offer (marker + range-count varint + 4x ranges + capability varint). The skipper below
        // consumes exactly the negotiated handshake bytes per direction before deframing.
        var skipper = new HandshakeSkipper(clientToServer);
        var handshakeDone = false;

        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
            {
                return;
            }

            var payload = buffer.AsSpan(0, read).ToArray();
            if (!handshakeDone)
            {
                payload = skipper.Feed(payload, out var done);
                handshakeDone = done;
                if (payload.Length == 0)
                {
                    await sink.WriteAsync(buffer.AsMemory(0, read));
                    continue;
                }
            }

            foreach (var message in deframer.Feed(payload))
            {
                Record(clientToServer, message, messages);
            }

            await sink.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    /// <summary>Consumes exactly the Bolt handshake bytes for one direction, then passes the
    /// remaining bytes through. (Probe-only implementation; supports the 5.8-only surfaces the
    /// probes exercise.)</summary>
    private sealed class HandshakeSkipper
    {
        private readonly bool _clientToServer;
        private readonly List<byte> _pending = new();
        private int _phase; // 0 = collecting preamble, 1 = deciding selection/offer, 2 = done

        public HandshakeSkipper(bool clientToServer) => _clientToServer = clientToServer;

        public byte[] Feed(byte[] bytes, out bool done)
        {
            _pending.AddRange(bytes);
            if (_phase == 0)
            {
                // Phase 0 exists only for the client direction (magic + 16 proposal bytes). The
                // server never sends a magic preamble.
                if (!_clientToServer)
                {
                    _phase = 1;
                }
                else if (_pending.Count < 20)
                {
                    done = false;
                    return Array.Empty<byte>();
                }
                else
                {
                    _pending.RemoveRange(0, 20);
                    _phase = 1;
                }
            }

            if (_phase == 1)
            {
                if (_clientToServer)
                {
                    // A manifest selection begins with the selected version 00 00 08 05 followed
                    // by a capability varint; a legacy client's first chunk header cannot be
                    // 00 00 08 05 (that would be a zero-length chunk followed by garbage).
                    if (_pending.Count < 4)
                    {
                        done = false;
                        return Array.Empty<byte>();
                    }

                    if (_pending[0] == 0x00 && _pending[1] == 0x00 && _pending[2] == 0x08 && _pending[3] == 0x05)
                    {
                        if (_pending.Count < 5)
                        {
                            done = false;
                            return Array.Empty<byte>();
                        }

                        _pending.RemoveRange(0, 5);
                    }
                }
                else
                {
                    if (_pending.Count < 4)
                    {
                        done = false;
                        return Array.Empty<byte>();
                    }

                    if (_pending[0] == 0x00 && _pending[1] == 0x00 && _pending[2] == 0x01 && _pending[3] == 0xFF)
                    {
                        // Manifest offer: marker + range-count varint + 4x count ranges + capability varint.
                        if (_pending.Count < 5)
                        {
                            done = false;
                            return Array.Empty<byte>();
                        }

                        var ranges = _pending[4];
                        if (_pending.Count < 5 + (4 * ranges) + 1)
                        {
                            done = false;
                            return Array.Empty<byte>();
                        }

                        _pending.RemoveRange(0, 5 + (4 * ranges) + 1);
                    }
                    else
                    {
                        _pending.RemoveRange(0, 4); // legacy 4-byte version reply
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

    /// <summary>Stateful B2 deframer: complete messages (all chunks to the 00 00 terminator),
    /// preserving partial data across reads.</summary>
    private sealed class ChunkDeframer
    {
        private readonly List<byte> _pending = new();

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
                    // message terminator
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
                    yield break; // partial chunk; wait for more bytes
                }

                _message.AddRange(_pending.Skip(2).Take(length));
                _pending.RemoveRange(0, 2 + length);
            }
        }

        private readonly List<byte> _message = new();
    }

    private static void Record(bool clientToServer, byte[] body, List<ProxyRecordedMessage> messages)
    {
        var tag = body.Length >= 2 ? body[1] : (byte)0;
        var name = ProbeMessages.Name(tag);
        var isLogon = clientToServer && tag == ProbeMessages.LogonTag;
        messages.Add(new ProxyRecordedMessage(
            clientToServer,
            name,
            tag,
            isLogon ? "REDACTED(LOGON)" : Convert.ToHexString(body),
            Redacted: isLogon));
    }
}
