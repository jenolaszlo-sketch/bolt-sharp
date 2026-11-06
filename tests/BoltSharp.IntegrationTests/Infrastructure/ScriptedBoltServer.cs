using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using BoltSharp.Framing;
using BoltSharp.PackStream;

namespace BoltSharp.IntegrationTests.Infrastructure;

internal enum HandshakeMode
{
    /// <summary>Answer the legacy handshake with a concrete Bolt 5.8 selection (00 00 08 05).</summary>
    Legacy58,

    /// <summary>Offer Bolt 5.8 through Manifest v1 (00 00 01 FF ...).</summary>
    Manifest58,

    /// <summary>Reject every proposal with 00 00 00 00 and close.</summary>
    RejectAll,
}

internal sealed class BoltServerOptions
{
    public BoltConnectionScript Script { get; init; } = new();

    public HandshakeMode Handshake { get; init; } = HandshakeMode.Legacy58;

    public WriteSplitMode HandshakeSplit { get; init; } = WriteSplitMode.Whole;

    public int HandshakeWriteChunkSize { get; init; } = 7;

    /// <summary>Maximum client messages to process before stopping.</summary>
    public int MaxMessages { get; init; } = 32;

    /// <summary>Bounded deadline for the whole connection.</summary>
    public TimeSpan Bound { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>When true, an unscripted request is answered with an empty SUCCESS.</summary>
    public bool ReplyToUnscripted { get; init; }

    /// <summary>Maximum concurrent accepted connections.</summary>
    public int MaxConnections { get; init; } = 1;
}

/// <summary>
/// A minimal, in-process, loopback-only fake Bolt server for controlled driver experiments. It
/// never binds a public interface, never requires an external database, uses an OS-assigned port
/// and has deterministic bounded lifecycle. It delegates nothing to BoltSharp's protocol core for
/// its <b>responses</b> (those come from <see cref="ProtocolFixtures"/>); it only uses
/// <see cref="BoltMessageReader"/> / <see cref="PackStreamReader"/> to decode client requests.
/// </summary>
internal sealed class ScriptedBoltServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly BoltServerOptions _options;
    private readonly WireCapture _capture = new();
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _loop;
    private int _disposed;

    private ScriptedBoltServer(BoltServerOptions options)
    {
        _options = options;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(options.MaxConnections);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = AcceptLoopAsync();
    }

    public int Port { get; }

    public Uri Uri => new($"bolt://127.0.0.1:{Port}");

    public WireCapture Capture => _capture;

    /// <summary>Completes when the accepted connection has been fully handled.</summary>
    public Task Completion => _completion.Task;

    public static ScriptedBoltServer Start(BoltServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ScriptedBoltServer(options);
    }

    public async Task WaitForCompletionAsync(TimeSpan timeout) =>
        await _completion.Task.WaitAsync(timeout).ConfigureAwait(false);

    private async Task AcceptLoopAsync()
    {
        var handlers = new List<Task>();
        try
        {
            while (!_shutdown.IsCancellationRequested && handlers.Count < _options.MaxConnections)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                handlers.Add(HandleConnectionAsync(client));
            }

            if (handlers.Count > 0)
            {
                try
                {
                    await Task.WhenAll(handlers).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _capture.Note($"handler fault: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        finally
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        bound.CancelAfter(_options.Bound);
        var token = bound.Token;

        try
        {
            using var stream = client.GetStream();
            client.NoDelay = true;

            var preamble = await ReadExactAsync(stream, 20, token).ConfigureAwait(false);
            _capture.ClientHandshake = preamble;

            var magicOk = preamble.Length == 20
                && preamble[0] == 0x60 && preamble[1] == 0x60 && preamble[2] == 0xB0 && preamble[3] == 0x17;
            if (!magicOk)
            {
                _capture.Note("handshake magic invalid");
                _capture.NegotiatedVersion = "INVALID_MAGIC";
                return;
            }

            await DoHandshakeAsync(stream, token).ConfigureAwait(false);
            if (_options.Handshake == HandshakeMode.RejectAll)
            {
                _capture.NegotiatedVersion = "rejected";
                return;
            }

            await MessageLoopAsync(stream, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _capture.Note("connection bound elapsed");
        }
        catch (Exception ex)
        {
            _capture.Note($"connection error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                client.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            _completion.TrySetResult();
        }
    }

    private async Task DoHandshakeAsync(Stream stream, CancellationToken token)
    {
        switch (_options.Handshake)
        {
            case HandshakeMode.RejectAll:
            {
                var zero = new byte[] { 0, 0, 0, 0 };
                _capture.ServerHandshake = zero;
                await WriteWithSplitAsync(stream, zero, _options.HandshakeSplit, _options.HandshakeWriteChunkSize, token)
                    .ConfigureAwait(false);
                return;
            }

            case HandshakeMode.Manifest58:
            {
                // 00 00 01 FF | VarInt N=1 | range(5.8, range 0) | VarInt capabilities=0
                var offer = new byte[]
                {
                    0x00, 0x00, 0x01, 0xFF,
                    0x01,
                    0x00, 0x00, 0x08, 0x05,
                    0x00,
                };
                _capture.ServerHandshake = offer;
                await WriteWithSplitAsync(stream, offer, _options.HandshakeSplit, _options.HandshakeWriteChunkSize, token)
                    .ConfigureAwait(false);

                // Read the client's concrete selection (4 bytes) + VarInt capabilities.
                var selection = await ReadExactAsync(stream, 4, token).ConfigureAwait(false);
                var version = BinaryPrimitives.ReadUInt32BigEndian(selection);
                var capabilities = await ReadVarIntAsync(stream, token).ConfigureAwait(false);
                _capture.Note($"manifest selection=0x{version:X8} capabilities={capabilities}");
                _capture.NegotiatedVersion = $"{version & 0xFF}.{(version >> 8) & 0xFF}";
                return;
            }

            default:
            {
                var response = new byte[] { 0x00, 0x00, 0x08, 0x05 };
                _capture.ServerHandshake = response;
                await WriteWithSplitAsync(stream, response, _options.HandshakeSplit, _options.HandshakeWriteChunkSize, token)
                    .ConfigureAwait(false);
                _capture.NegotiatedVersion = "5.8";
                return;
            }
        }
    }

    private async Task MessageLoopAsync(Stream stream, CancellationToken token)
    {
        var reader = new BoltMessageReader(stream);
        var count = 0;

        while (count < _options.MaxMessages)
        {
            byte[]? message;
            try
            {
                message = await reader.ReadMessageAsync(token).ConfigureAwait(false);
            }
            catch (BoltFramingException ex)
            {
                _capture.Note($"framing error from client: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (message is null)
            {
                _capture.Note("client closed the connection");
                return;
            }

            count++;
            var request = DecodeRequest(message, count);
            _capture.RecordRequest(request);
            _capture.RecordFrame(
                WireDirection.ClientToServer,
                request.Name,
                message,
                request.SanitizedDescription,
                request.Sensitive);

            var reply = _options.Script.Next(request.Name);
            if (reply is null)
            {
                request = MarkUnscripted(request);
                _capture.Note($"unscripted request: {request.Name}");
                if (_options.ReplyToUnscripted && request.Name != "GOODBYE")
                {
                    await WriteReplyAsync(
                        stream,
                        new Reply { Messages = { ProtocolFixtures.SuccessEmpty() } },
                        token).ConfigureAwait(false);
                }

                continue;
            }

            await WriteReplyAsync(stream, reply, token).ConfigureAwait(false);

            if (reply.CloseAfter)
            {
                return;
            }
        }

        _capture.Note($"message bound reached ({_options.MaxMessages})");
    }

    private async Task WriteReplyAsync(Stream stream, Reply reply, CancellationToken token)
    {
        if (reply.Coalesce && reply.Messages.Count > 0)
        {
            var all = ProtocolFixtures.Concat(reply.Messages.Select(ProtocolFixtures.Frame));
            _capture.RecordFrame(
                WireDirection.ServerToClient,
                "COALESCED",
                all,
                $"{reply.Messages.Count} message(s) in one write",
                sensitive: false);
            await stream.WriteAsync(all, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            return;
        }

        foreach (var message in reply.Messages)
        {
            var framed = ProtocolFixtures.Frame(message);
            var name = DecodeResponseName(message);
            _capture.RecordFrame(
                WireDirection.ServerToClient,
                name,
                message,
                $"{name} ({message.Length} bytes)",
                sensitive: false);
            await WriteWithSplitAsync(stream, framed, reply.SplitMode, reply.WriteChunkSize, token)
                .ConfigureAwait(false);
        }

        foreach (var raw in reply.RawWrites)
        {
            _capture.RecordFrame(
                WireDirection.ServerToClient,
                "RAW",
                raw,
                $"raw {raw.Length} byte(s)",
                sensitive: false);
            await stream.WriteAsync(raw, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
    }

    private static BoltRequest MarkUnscripted(BoltRequest request) => new()
    {
        Sequence = request.Sequence,
        Signature = request.Signature,
        Name = request.Name,
        RawMessage = request.RawMessage,
        Structure = request.Structure,
        Sensitive = request.Sensitive,
        SanitizedDescription = request.SanitizedDescription,
        Unscripted = true,
    };

    private static BoltRequest DecodeRequest(byte[] message, long sequence)
    {
        byte signature = 0;
        PackStreamStructure? structure = null;
        try
        {
            if (new PackStreamReader(message).ReadValue() is PackStreamStructure s)
            {
                structure = s;
                signature = s.Tag;
            }
        }
        catch (PackStreamException ex)
        {
            // Leave signature 0 and record a diagnostic via the structure being null.
            _ = ex;
        }

        var name = signature == 0 ? "UNDECODABLE" : ProtocolFixtures.MsgName(signature);
        var sensitive = signature is ProtocolFixtures.MsgHello or ProtocolFixtures.MsgLogon
            or ProtocolFixtures.MsgRoute;
        return new BoltRequest
        {
            Sequence = sequence,
            Signature = signature,
            Name = name,
            RawMessage = message,
            Structure = structure ?? new PackStreamStructure(0, Array.Empty<PackStreamValue?>()),
            Sensitive = sensitive,
            SanitizedDescription = Describe(structure, sensitive),
            Unscripted = false,
        };
    }

    private static string Describe(PackStreamStructure? structure, bool sensitive)
    {
        if (structure is null)
        {
            return "undecodable structure";
        }

        var name = ProtocolFixtures.MsgName(structure.Tag);
        var maps = structure.Fields.OfType<PackStreamMap>().ToList();
        if (sensitive)
        {
            // Never persist principal/credentials/user-agent payloads: record only the key names.
            var keys = maps.SelectMany(m => m.Entries.Select(e => e.Key)).Distinct().ToArray();
            return $"{name} map keys [{string.Join(", ", keys)}] <values redacted>";
        }

        var parts = new List<string>();
        foreach (var field in structure.Fields)
        {
            parts.Add(field switch
            {
                null => "null",
                PackStreamString s => $"\"{s.Value}\"",
                PackStreamInteger i => i.Value.ToString(),
                PackStreamMap map => $"{{{string.Join(",", map.Entries.Select(e => e.Key))}}}",
                PackStreamList list => $"[{list.Items.Count}]",
                _ => field.GetType().Name,
            });
        }

        return $"{name}({string.Join(", ", parts)})";
    }

    private static string DecodeResponseName(byte[] message)
    {
        try
        {
            if (new PackStreamReader(message).ReadValue() is PackStreamStructure s)
            {
                return s.Tag switch
                {
                    ProtocolFixtures.MsgSuccess => "SUCCESS",
                    ProtocolFixtures.MsgRecord => "RECORD",
                    ProtocolFixtures.MsgFailure => "FAILURE",
                    ProtocolFixtures.MsgIgnored => "IGNORED",
                    _ => $"RESPONSE_0x{s.Tag:X2}",
                };
            }
        }
        catch (PackStreamException)
        {
        }

        return "RESPONSE";
    }

    private static async Task WriteWithSplitAsync(
        Stream stream,
        byte[] data,
        WriteSplitMode mode,
        int chunkSize,
        CancellationToken token)
    {
        switch (mode)
        {
            case WriteSplitMode.PerByte:
                for (var i = 0; i < data.Length; i++)
                {
                    await stream.WriteAsync(data.AsMemory(i, 1), token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }

                break;

            case WriteSplitMode.FixedChunk:
            {
                var size = Math.Max(1, chunkSize);
                for (var i = 0; i < data.Length; i += size)
                {
                    var length = Math.Min(size, data.Length - i);
                    await stream.WriteAsync(data.AsMemory(i, length), token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }

                break;
            }

            default:
                await stream.WriteAsync(data, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                break;
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), token).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        return read == count ? buffer : buffer.AsSpan(0, read).ToArray();
    }

    private static async Task<ulong> ReadVarIntAsync(Stream stream, CancellationToken token)
    {
        ulong value = 0;
        var shift = 0;
        while (shift < 70)
        {
            var one = new byte[1];
            var n = await stream.ReadAsync(one, token).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            var b = one[0];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return value;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }
}
