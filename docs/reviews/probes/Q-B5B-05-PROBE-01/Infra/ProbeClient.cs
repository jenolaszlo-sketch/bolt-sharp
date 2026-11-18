// Q-B5B-05-PROBE-01 producer probe — independent raw Bolt 5.8 client over a real TCP socket.
//
// Handshake, B2 chunk framing, message payloads and response reads are implemented here from the
// protocol specification. The client never calls BoltSharp framing/codec/session code, so it is an
// independent oracle for what actually crossed the wire.
//
// The client exposes the two client-side physical-connection facts used by the same-connection
// oracle: the socket's local endpoint (an ephemeral port that necessarily changes on reconnect)
// and a probe-local connection key (never sent on the wire).
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace BoltSharp.Q5B05.Probe.Infra;

public sealed class ProbeClient : IDisposable
{
    private TcpClient? _client;
    private Stream? _stream;

    /// <summary>The socket's local endpoint; a reconnect necessarily yields a different port.</summary>
    public IPEndPoint LocalEndPoint { get; private set; } = new(IPAddress.Loopback, 0);

    /// <summary>Probe-local identity of this physical connection. Never transmitted.</summary>
    public Guid ConnectionKey { get; } = Guid.NewGuid();

    /// <summary>True from a successful connect until the socket is closed or a read observes EOF.</summary>
    public bool IsOpen { get; private set; }

    public async Task ConnectAsync(IPEndPoint endpoint)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(endpoint);
        _stream = _client.GetStream();
        LocalEndPoint = (IPEndPoint)_client.Client.LocalEndPoint!;
        IsOpen = true;
    }

    /// <summary>
    /// Writes the Bolt magic and a single legacy proposal for exactly Bolt 5.8, then reads the
    /// 4-byte server reply (hex). Synchronous internals: the read is pending in the kernel before
    /// the server can process and close, so an RST cannot discard the in-flight reply on Windows.
    /// </summary>
    public Task<string> HandshakeAsync()
    {
        var handshake = new byte[]
        {
            0x60, 0x60, 0xB0, 0x17, 0x00, 0x00, 0x08, 0x05, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };
        _stream!.Write(handshake);
        return Task.FromResult(Convert.ToHexString(ReadExactly(4)));
    }

    /// <summary>Synchronous framed send; call sites pair it with a synchronous read.</summary>
    public void SendFramed(byte[] payload) => _stream!.Write(Frame(payload));

    public Task SendFramedAsync(byte[] payload)
    {
        SendFramed(payload);
        return Task.CompletedTask;
    }

    /// <summary>Synchronous raw write, for pipelining several framed messages in one TCP write.</summary>
    public void SendRaw(byte[] bytes) => _stream!.Write(bytes);

    /// <summary>Write to a socket the server may already have closed; the failure is contained.</summary>
    public void SendToPossiblyClosedSocket(byte[] payload)
    {
        try
        {
            SendFramed(payload);
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

    public Task<byte[]?> ReadMessageAsync(TimeSpan? timeout = null) =>
        Task.FromResult(ReadMessage(timeout));

    /// <summary>
    /// Synchronous read of one complete B2 message (all chunks to the 00 00 terminator). Returns
    /// null when the server closed the connection without delivering a complete message.
    /// </summary>
    public byte[]? ReadMessage(TimeSpan? timeout = null)
    {
        var payload = new List<byte>();
        try
        {
            while (true)
            {
                var head = ReadExactly(2, timeout);
                var length = (head[0] << 8) | head[1];
                if (length == 0)
                {
                    return payload.ToArray();
                }

                payload.AddRange(ReadExactly(length, timeout));
            }
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            IsOpen = false;
            return null; // closed by the server (EOF or abortive reset)
        }
    }

    /// <summary>True when no further message arrives within the bound on a still-open connection.</summary>
    public bool HasNoFurtherMessage(TimeSpan within)
    {
        try
        {
            return ReadMessage(within) is null;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public byte[] ReadRaw(int count, TimeSpan? timeout = null) => ReadExactly(count, timeout);

    /// <summary>
    /// Upgrades the connected plaintext socket to TLS as a client, accepting the loopback
    /// server's self-signed certificate (the accepted isolated loopback arrangement; no trust
    /// store is touched). After the upgrade, every read and write is encrypted while the same
    /// physical socket continues to carry the session.
    /// </summary>
    public async Task TlsUpgradeAsync(string targetHost)
    {
        ObjectDisposedException.ThrowIf(_stream is null, this);
        var ssl = new SslStream(_stream, leaveInnerStreamOpen: true);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
        });
        _stream = ssl;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _client?.Dispose();
        IsOpen = false;
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

    private byte[] ReadExactly(int count, TimeSpan? timeout = null)
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
                IsOpen = false;
                throw new IOException("EOF");
            }

            read += n;
        }

        return buffer;
    }
}