// B5-B-F01-R1-Q1 reviewer probe — independent raw Bolt 5.8 client over a real TCP socket.
//
// Handshake, B2 chunk framing, message payloads and response reads are all implemented here from
// the protocol specification. The client never calls BoltSharp framing/codec/session code, so it
// is an independent oracle for what actually crossed the wire.
namespace BoltSharp.B5BF01R1Q1.Probes.Infra;

using System.Net;
using System.Net.Sockets;

public sealed class ReviewerBoltClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;

    public IPEndPoint LocalEndPoint { get; private set; } = new(IPAddress.Loopback, 0);
    public Guid ConnectionId { get; } = Guid.NewGuid();

    public async Task ConnectAsync(IPEndPoint endpoint)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(endpoint);
        _stream = _client.GetStream();
        LocalEndPoint = (IPEndPoint)_client.Client.LocalEndPoint!;
    }

    /// <summary>Writes the Bolt magic and a single legacy proposal for exactly Bolt 5.8, then
    /// reads and returns the 4-byte server reply (hex). Synchronous internals: the read must be
    /// pending in the kernel before the server can process and close, or an RST can discard the
    /// in-flight response (Windows behavior observed by this probe suite).</summary>
    public Task<string> HandshakeAsync()
    {
        var handshake = new byte[]
        {
            0x60, 0x60, 0xB0, 0x17, 0x00, 0x00, 0x08, 0x05, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        _stream!.Write(handshake);
        var reply = ReadExactly(4);
        return Task.FromResult(Convert.ToHexString(reply));
    }

    public Task SendFramedAsync(byte[] payload)
    {
        SendFramed(payload);
        return Task.CompletedTask;
    }

    /// <summary>Synchronous framed send: call sites pair this with a synchronous read so the
    /// read is pending in the kernel before the server can process and close (an RST arriving
    /// before the response is read can otherwise discard the in-flight bytes on Windows).</summary>
    public void SendFramed(byte[] payload) => _stream!.Write(Frame(payload));

    /// <summary>Synchronous raw write — used for pipelining several framed messages in one TCP
    /// write, immediately followed by a synchronous read.</summary>
    public void SendRaw(byte[] bytes) => _stream!.Write(bytes);

    public Task SendRawAsync(byte[] bytes)
    {
        SendRaw(bytes);
        return Task.CompletedTask;
    }

    /// <summary>Write to a socket that may already have been closed by the server (RST): the
    /// write failure is contained — the probe only cares that nothing further is delivered.</summary>
    public async Task SendToPossiblyClosedSocketAsync(byte[] payload)
    {
        try
        {
            await SendFramedAsync(payload);
        }
        catch (IOException)
        {
            // expected: the server already closed the connection
        }
        catch (ObjectDisposedException)
        {
            // expected: ditto
        }
    }

    /// <summary>Reads one complete B2 message (all chunks to the 00 00 terminator). Returns null
    /// when the server closed the connection without delivering a complete message.</summary>
    public Task<byte[]?> ReadMessageAsync(TimeSpan? timeout = null) => Task.FromResult(ReadMessage(timeout));

    /// <summary>Synchronous read of one complete B2 message; see the send-side note on why the
    /// read path is synchronous.</summary>
    public byte[]? ReadMessage(TimeSpan? timeout = null)
    {
        var chunks = new List<byte>();
        try
        {
            while (true)
            {
                var head = ReadExactly(2, timeout);
                var header = (head[0] << 8) | head[1];
                if (header == 0)
                {
                    return chunks.ToArray();
                }

                chunks.AddRange(ReadExactly(header, timeout));
            }
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return null; // closed by server (EOF or abortive RST)
        }
    }

    /// <summary>Attempts one more read after a terminal response; any complete message is a
    /// failure, and both EOF and RST count as "no further message".</summary>
    public async Task<bool> HasAdditionalMessageAsync()
    {
        try
        {
            var message = await ReadMessageAsync(TimeSpan.FromSeconds(5));
            return message is not null;
        }
        catch (TimeoutException)
        {
            return false; // no message arrived within the bound on a still-open connection
        }
    }

    public async Task<bool> WaitForCloseAsync(TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var message = await ReadMessageAsync(TimeSpan.FromSeconds(2));
                if (message is null)
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
                // no data within the read bound; retry until the outer deadline
            }
        }

        return false;
    }

    /// <summary>Reads the 4-byte handshake reply after a client-managed (possibly fragmented)
    /// preamble has already been written.</summary>
    public Task<string> HandshakeReplyAfterFragmentedPreambleAsync()
    {
        var reply = ReadRaw(4);
        return Task.FromResult(Convert.ToHexString(reply));
    }

    /// <summary>Reads exactly <paramref name="count"/> raw bytes (no framing interpretation).</summary>
    public Task<byte[]> ReadRawAsync(int count, TimeSpan? timeout = null) => Task.FromResult(ReadRaw(count, timeout));

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[payload.Length + 4];
        frame[0] = (byte)(payload.Length >> 8);
        frame[1] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 2, payload.Length);
        frame[^2] = 0x00;
        frame[^1] = 0x00;
        return frame;
    }

    private byte[] ReadRaw(int count, TimeSpan? timeout = null)
    {
        var deadline = timeout is null ? DateTime.MaxValue : DateTime.UtcNow + timeout.Value;
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("raw read did not complete in time");
            }

            var n = _stream!.Read(buffer, read, count - read);
            if (n == 0)
            {
                throw new IOException("EOF");
            }

            read += n;
        }

        return buffer;
    }

    private byte[] ReadExactly(int count, TimeSpan? timeout = null) => ReadRaw(count, timeout);
}
