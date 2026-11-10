using System.Net;
using System.Net.Sockets;

namespace BoltSharp.B5BQ1.Probes.Infrastructure;

/// <summary>Direction of one recorded raw chunk.</summary>
internal enum Q1TapDirection
{
    ClientToServer,
    ServerToClient,
}

/// <summary>One passively recorded raw read. Never rewritten by the tap.</summary>
internal sealed record Q1TapChunk(Q1TapDirection Direction, byte[] Bytes, DateTimeOffset Timestamp, string CloseKind)
{
    internal string Hex => Q1WireTranscript.Hex(Bytes);
}

/// <summary>
/// A passive, test-only loopback wire tap. The tap listens on its own ephemeral port and forwards
/// every byte between the official driver and the real B5-A server unchanged and immediately.
/// It records a copy of each read for evidence only; it never rewrites, delays, drops or injects
/// bytes, never strips request options and never alters TLS trust behavior.
/// </summary>
internal sealed class Q1WireTap : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IPEndPoint _target;
    private readonly List<Q1TapChunk> _chunks = new();
    private readonly List<string> _clientCloses = new();
    private readonly List<string> _serverCloses = new();
    private readonly object _lock = new();
    private int _connectionsAccepted;
    private readonly List<Task> _relayTasks = new();

    private Q1WireTap(IPEndPoint target)
    {
        _target = target;
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    internal IPEndPoint Endpoint => (IPEndPoint)_listener.LocalEndpoint;

    internal string HostPort => $"localhost:{Endpoint.Port}";

    internal int ConnectionsAccepted => _connectionsAccepted;

    internal static async Task<Q1WireTap> StartAsync(IPEndPoint target)
    {
        var tap = new Q1WireTap(target);
        tap._listener.Start();
        _ = tap.AcceptLoopAsync();
        await Task.CompletedTask;
        return tap;
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            Interlocked.Increment(ref _connectionsAccepted);
            var task = Task.Run(() => RelayAsync(client));
            lock (_lock)
            {
                _relayTasks.Add(task);
            }
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using var clientOwnership = client;
        try
        {
            using var server = new TcpClient();
            await server.ConnectAsync(_target.Address, _target.Port);
            using var serverOwnership = server;
            var clientStream = client.GetStream();
            var serverStream = server.GetStream();

            var c2s = CopyLoop(clientStream, serverStream, Q1TapDirection.ClientToServer, _clientCloses);
            var s2c = CopyLoop(serverStream, clientStream, Q1TapDirection.ServerToClient, _serverCloses);
            await Task.WhenAny(c2s, s2c);
        }
        catch
        {
            // Relay faults close the relayed pair; the tap itself never fabricates protocol data.
        }
    }

    private async Task CopyLoop(NetworkStream source, NetworkStream destination, Q1TapDirection direction, List<string> closeLog)
    {
        var buffer = new byte[65536];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer);
                if (read == 0)
                {
                    lock (_lock)
                    {
                        closeLog.Add("eof");
                    }

                    break;
                }

                var copy = new byte[read];
                Array.Copy(buffer, copy, read);
                lock (_lock)
                {
                    _chunks.Add(new Q1TapChunk(direction, copy, DateTimeOffset.UtcNow, "data"));
                }

                await destination.WriteAsync(copy);
                await destination.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                closeLog.Add(ex.GetType().Name);
            }
        }
    }

    /// <summary>A stable copy of the recorded chunks.</summary>
    public IReadOnlyList<Q1TapChunk> Chunks
    {
        get
        {
            lock (_lock)
            {
                return _chunks.ToArray();
            }
        }
    }

    public IReadOnlyList<string> ClientCloses
    {
        get
        {
            lock (_lock)
            {
                return _clientCloses.ToArray();
            }
        }
    }

    public IReadOnlyList<string> ServerCloses
    {
        get
        {
            lock (_lock)
            {
                return _serverCloses.ToArray();
            }
        }
    }

    /// <summary>Waits until at least <paramref name="count"/> relay legs have closed on each side.</summary>
    public async Task<bool> WaitForClosedConnectionsAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ClientCloses.Count >= count && ServerCloses.Count >= count)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return ClientCloses.Count >= count && ServerCloses.Count >= count;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        Task[] tasks;
        lock (_lock)
        {
            tasks = _relayTasks.ToArray();
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
        }
    }
}

/// <summary>
/// One parsed Bolt message from a tapped direction. LOGON payloads are redacted at the raw-payload
/// level before anything is stored: the credential-bearing bytes are never retained.
/// </summary>
internal sealed record Q1WireMessage(string Name, byte Marker, byte Signature, string Hex, string Description, bool Redacted);

/// <summary>Independent transcript parsing over tap bytes (handshake, chunk framing, signatures).</summary>
internal static class Q1WireTranscript
{
    internal static string Hex(ReadOnlySpan<byte> bytes)
    {
        var builder = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("X2"));
        }

        return builder.ToString();
    }

    /// <summary>Parses the client Bolt handshake (magic + four 4-byte proposals) from the tapped stream.</summary>
    internal static (string MagicHex, string[] ProposalHex) ParseClientHandshake(Q1WireTap tap)
    {
        var stream = ConcatDirection(tap, Q1TapDirection.ClientToServer);
        if (stream.Length < 20)
        {
            return (Hex(stream.AsSpan(0, Math.Min(stream.Length, 20))), Array.Empty<string>());
        }

        var magic = Hex(stream.AsSpan(0, 4));
        var proposals = new string[4];
        for (var i = 0; i < 4; i++)
        {
            proposals[i] = Hex(stream.AsSpan(4 + (i * 4), 4));
        }

        return (magic, proposals);
    }

    /// <summary>Parses the server's handshake reply (legacy 4-byte or manifest v1 offer).</summary>
    internal static string ParseServerHandshake(Q1WireTap tap)
    {
        var stream = ConcatDirection(tap, Q1TapDirection.ServerToClient);
        var handshakeLength = ServerHandshakeLength(stream);
        return Hex(stream.AsSpan(0, handshakeLength));
    }

    private static int ServerHandshakeLength(ReadOnlySpan<byte> stream)
    {
        if (stream.Length >= 10 && Hex(stream.Slice(0, 4)) == "000001FF")
        {
            return 10;
        }

        return Math.Min(4, stream.Length);
    }

    /// <summary>
    /// The byte offset where the client's first chunked Bolt message begins: the 20-byte handshake
    /// plus the 5-byte manifest selection when the server answered with a manifest offer (the
    /// legacy path has no client-side selection bytes).
    /// </summary>
    private static int ClientMessagesOffset(Q1WireTap tap)
    {
        var serverStream = ConcatDirection(tap, Q1TapDirection.ServerToClient);
        var serverLength = ServerHandshakeLength(serverStream);
        var manifestMode = serverLength == 10;
        return manifestMode ? 25 : 20;
    }

    private static byte[] ConcatDirection(Q1WireTap tap, Q1TapDirection direction)
    {
        var chunks = tap.Chunks.Where(c => c.Direction == direction).ToArray();
        var length = chunks.Sum(c => c.Bytes.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk.Bytes, 0, result, offset, chunk.Bytes.Length);
            offset += chunk.Bytes.Length;
        }

        return result;
    }

    /// <summary>
    /// Parses all chunked Bolt messages flowing in one direction across all tapped connections.
    /// The stream is segmented at each handshake boundary; within a segment, the client LOGON
    /// message is redacted at the raw-payload level (hex replaced) before storage.
    /// </summary>
    internal static (IReadOnlyList<Q1WireMessage> Messages, string ParseError) ParseMessages(
        Q1WireTap tap, Q1TapDirection direction)
    {
        var stream = ConcatDirection(tap, direction);
        var messages = new List<Q1WireMessage>();
        var error = string.Empty;
        var position = 0;

        while (position < stream.Length)
        {
            // Locate the next handshake segment start for this direction.
            int start;
            int headerLength;
            if (direction == Q1TapDirection.ClientToServer)
            {
                start = IndexOfPattern(stream, position, "6060B017");
                headerLength = 20;
                if (start < 0)
                {
                    error = "no-client-handshake-marker";
                    break;
                }
            }
            else
            {
                if (position == 0 && stream.Length >= 4 && Hex(stream.AsSpan(0, 4)) == "00000805")
                {
                    // First connection answered with the legacy 4-byte reply.
                    start = 0;
                    headerLength = 4;
                }
                else
                {
                    start = IndexOfPattern(stream, position, "000001FF");
                    headerLength = 10;
                    if (start < 0)
                    {
                        error = "no-server-handshake-marker";
                        break;
                    }
                }
            }

            // The client's manifest selection (5 bytes) sits between the handshake and the first
            // framed message; the legacy path adds none.
            var payloadStart = start + headerLength;
            if (direction == Q1TapDirection.ClientToServer &&
                payloadStart + 5 <= stream.Length &&
                Hex(stream.AsSpan(payloadStart, 5)) == "0000080500")
            {
                payloadStart += 5;
            }

            // Messages run until the next handshake marker or end of stream.
            var end = stream.Length;
            var nextMarker = direction == Q1TapDirection.ClientToServer
                ? IndexOfPattern(stream, payloadStart, "6060B017")
                : IndexOfPattern(stream, payloadStart, "000001FF");
            if (nextMarker >= 0)
            {
                end = nextMarker;
            }

            var parseResult = ParseFrames(stream, payloadStart, end, messages);
            if (parseResult.Length > 0)
            {
                error = parseResult;
                break;
            }

            position = end;
        }

        return (messages, error);
    }

    private static string ParseFrames(byte[] stream, int from, int to, List<Q1WireMessage> messages)
    {
        var position = from;
        var payload = new List<byte>();
        while (position < to)
        {
            if (position + 2 > to)
            {
                return "truncated-chunk-header";
            }

            var chunkLength = (stream[position] << 8) | stream[position + 1];
            position += 2;
            if (chunkLength == 0)
            {
                if (payload.Count == 0)
                {
                    return "empty-message";
                }

                var bytes = payload.ToArray();
                payload.Clear();
                messages.Add(BuildMessage(bytes));
                continue;
            }

            if (position + chunkLength > to)
            {
                return "truncated-chunk-body";
            }

            payload.AddRange(stream.AsSpan(position, chunkLength).ToArray());
            position += chunkLength;
        }

        return string.Empty;
    }

    private static int IndexOfPattern(byte[] stream, int from, string hexPattern)
    {
        var pattern = Convert.FromHexString(hexPattern);
        for (var i = from; i + pattern.Length <= stream.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (stream[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static Q1WireMessage BuildMessage(byte[] payload)
    {
        if (payload.Length < 2)
        {
            return new Q1WireMessage("UNKNOWN", payload.Length > 0 ? payload[0] : (byte)0, (byte)0, Hex(payload), "too-short", false);
        }

        var marker = payload[0];
        var signature = payload[1];
        var name = signature switch
        {
            0x01 => "HELLO",
            0x02 => "GOODBYE",
            0x0F => "RESET",
            0x10 => "RUN",
            0x11 => "BEGIN",
            0x12 => "COMMIT",
            0x13 => "ROLLBACK",
            0x2F => "DISCARD",
            0x3F => "PULL",
            0x54 => "TELEMETRY",
            0x66 => "ROUTE",
            0x6A => "LOGON",
            0x6B => "LOGOFF",
            0x70 => "SUCCESS",
            0x71 => "RECORD",
            0x7E => "IGNORED",
            0x7F => "FAILURE",
            _ => $"SIG_0x{signature:x2}",
        };

        // Credential-bearing requests are redacted at the raw-payload level before storage.
        if (signature == 0x6A)
        {
            return new Q1WireMessage(name, marker, signature, "<redacted>", $"<redacted LOGON payload: {payload.Length} bytes>", true);
        }

        var description = name + " " + Q1PackStreamProbe.Describe(payload);
        return new Q1WireMessage(name, marker, signature, Hex(payload), description, false);
    }
}

/// <summary>
/// A minimal, reviewer-owned PackStream structural probe used only to describe captured wire
/// payloads for evidence (top-level map keys, small scalars). Independent of the production codec.
/// </summary>
internal static class Q1PackStreamProbe
{
    /// <summary>
    /// Describes a full message payload: after the B0–B3 struct header, all struct fields are
    /// described in order (space-separated). Empty struct (B0) yields an empty description.
    /// </summary>
    internal static string Describe(byte[] payload)
    {
        try
        {
            if (payload.Length >= 2 && payload[0] is >= 0xB0 and <= 0xBF)
            {
                var size = payload[0] & 0x0F;
                var reader = new Reader(payload, 2);
                var parts = new string[size];
                for (var i = 0; i < size; i++)
                {
                    parts[i] = DescribeValue(reader);
                }

                return string.Join(" ", parts);
            }

            return "<unexpected-outer-marker>";
        }
        catch
        {
            return "<undecodable>";
        }
    }

    private static string DescribeValue(Reader reader)
    {
        var marker = reader.Next();
        return marker switch
        {
            >= 0x80 and <= 0x8F => reader.ReadString(marker & 0x0F),
            0xD0 => reader.ReadString(reader.ReadByte()),
            0xD1 => reader.ReadString(reader.ReadUInt16()),
            >= 0x90 and <= 0x9F => DescribeList(reader, marker & 0x0F),
            0xD4 => DescribeList(reader, reader.ReadByte()),
            0xD5 => DescribeList(reader, reader.ReadUInt16()),
            >= 0xA0 and <= 0xAF => DescribeMap(reader, marker & 0x0F),
            0xD8 => DescribeMap(reader, reader.ReadByte()),
            0xD9 => DescribeMap(reader, reader.ReadUInt16()),
            0xC0 => "null",
            0xC2 => "false",
            0xC3 => "true",
            >= 0x00 and <= 0x7F => marker.ToString(),
            >= 0xF0 and <= 0xFF => unchecked((sbyte)marker).ToString(),
            0xC8 => ((sbyte)reader.ReadByte()).ToString(),
            0xC9 => ((short)reader.ReadUInt16()).ToString(),
            0xCA => reader.ReadInt32().ToString(),
            0xCB => reader.ReadInt64().ToString(),
            0xC1 => reader.ReadDouble().ToString("R"),
            >= 0xB0 and <= 0xBF => "<struct:" + (marker & 0x0F) + ">",
            _ => "<marker:" + marker.ToString("X2") + ">",
        };
    }

    private static string DescribeList(Reader reader, int size)
    {
        var parts = new string[size];
        for (var i = 0; i < size; i++)
        {
            parts[i] = DescribeValue(reader);
        }

        return "[" + string.Join(",", parts) + "]";
    }

    private static string DescribeMap(Reader reader, int size)
    {
        var parts = new string[size];
        for (var i = 0; i < size; i++)
        {
            var keyMarker = reader.Next();
            var key = keyMarker switch
            {
                >= 0x80 and <= 0x8F => reader.ReadString(keyMarker & 0x0F),
                0xD0 => reader.ReadString(reader.ReadByte()),
                0xD1 => reader.ReadString(reader.ReadUInt16()),
                _ => "<key:" + keyMarker.ToString("x2") + ">",
            };
            parts[i] = key + "=" + DescribeValue(reader);
        }

        return "{" + string.Join(",", parts) + "}";
    }

    private sealed class Reader
    {
        private readonly byte[] _data;
        private int _position;

        public Reader(byte[] data, int position)
        {
            _data = data;
            _position = position;
        }

        public byte Next()
        {
            if (_position >= _data.Length)
            {
                throw new EndOfStreamException();
            }

            return _data[_position++];
        }

        public byte ReadByte() => Next();

        public ushort ReadUInt16()
        {
            var high = Next();
            var low = Next();
            return (ushort)((high << 8) | low);
        }

        public uint ReadUInt32()
        {
            uint value = 0;
            for (var i = 0; i < 4; i++)
            {
                value = (value << 8) | Next();
            }

            return value;
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public long ReadInt64()
        {
            long value = 0;
            for (var i = 0; i < 8; i++)
            {
                value = (value << 8) | Next();
            }

            return value;
        }

        public double ReadDouble()
        {
            var raw = ReadInt64();
            return BitConverter.Int64BitsToDouble(raw);
        }

        public string ReadString(int length)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                bytes[i] = Next();
            }

            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
