using System.Net.Sockets;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// A minimal test-controlled Bolt handshake peer. It is used only for negative and boundary
/// probes; its behaviour is never attributed to the official driver.
/// </summary>
internal sealed class SyntheticHandshakeClient : IAsyncDisposable
{
    internal static readonly byte[] Magic = { 0x60, 0x60, 0xB0, 0x17 };

    internal static readonly byte[] ManifestMarker = { 0x00, 0x00, 0x01, 0xFF };

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    /// <summary>Bolt 4.0 style concrete version value (minor then major).</summary>
    internal static uint Version(int major, int minor) => (uint)((minor << 8) | major);

    /// <summary>Bolt 4.3 style minor range value.</summary>
    internal static uint Range(int major, int minor, int range) =>
        (uint)((range << 16) | (minor << 8) | major);

    /// <summary>Magic plus exactly four big-endian proposals, zero-padded.</summary>
    internal static byte[] Preamble(params uint[] proposals)
    {
        if (proposals.Length > 4)
        {
            throw new ArgumentException("At most four proposals are allowed.", nameof(proposals));
        }

        var bytes = new List<byte>(Magic);
        for (var i = 0; i < 4; i++)
        {
            var value = i < proposals.Length ? proposals[i] : 0u;
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        return bytes.ToArray();
    }

    /// <summary>Magic plus the Manifest v1 marker in the first proposal slot.</summary>
    internal static byte[] ManifestPreamble() => Preamble(0x0000_01FFu);

    internal static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private SyntheticHandshakeClient(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    internal static async Task<SyntheticHandshakeClient> ConnectAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();
        client.NoDelay = true;
        try
        {
            using var connectTimeout = CreateTimeout(timeout, cancellationToken);
            await client.ConnectAsync("127.0.0.1", port, connectTimeout.Token).ConfigureAwait(false);
            return new SyntheticHandshakeClient(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal async Task SendAsync(
        ReadOnlyMemory<byte> bytes,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var writeTimeout = CreateTimeout(timeout, cancellationToken);
        await _stream.WriteAsync(bytes, writeTimeout.Token).ConfigureAwait(false);
        await _stream.FlushAsync(writeTimeout.Token).ConfigureAwait(false);
    }

    internal async Task<byte[]> ReadExactlyAsync(
        int count,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[count];
        var offset = 0;
        using var readTimeout = CreateTimeout(timeout, cancellationToken);
        while (offset < count)
        {
            var read = await _stream.ReadAsync(
                buffer.AsMemory(offset, count - offset),
                readTimeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    $"The listener closed after {offset} of {count} expected byte(s).");
            }

            offset += read;
        }

        return buffer;
    }

    /// <summary>Reads until the listener closes; returns the number of bytes received.</summary>
    internal async Task<int> ReadUntilClosedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var total = 0;
        var buffer = new byte[256];
        using var readTimeout = CreateTimeout(timeout, cancellationToken);
        while (true)
        {
            var read = await _stream.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return total;
            }

            total += read;
        }
    }

    /// <summary>
    /// Returns true when the listener closed the connection without delivering any response bytes.
    /// A TCP reset (IOException/SocketException) also counts: the server closing its socket while
    /// client-sent bytes are still unread produces RST on Windows, and no byte was delivered to
    /// this client by that read. Server-side capture (<c>trace.ServerResponseBytes</c>) remains the
    /// authoritative check that nothing was written.
    /// </summary>
    internal async Task<bool> WaitForCloseWithoutDataAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1];
        using var readTimeout = CreateTimeout(timeout, cancellationToken);
        try
        {
            var read = await _stream.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false);
            return read == 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    internal async Task<ulong> ReadVarIntAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ulong value = 0;
        var shift = 0;
        for (var i = 0; i < 10; i++)
        {
            var one = await ReadExactlyAsync(1, timeout, cancellationToken).ConfigureAwait(false);
            var b = one[0];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        throw new InvalidOperationException("The synthetic client received an over-long VarInt.");
    }

    internal async Task<byte[]> ReadManifestOfferAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var marker = await ReadExactlyAsync(4, timeout, cancellationToken).ConfigureAwait(false);
        var count = await ReadVarIntAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (count > 64)
        {
            throw new InvalidOperationException("The synthetic client refused an implausible version count.");
        }

        var ranges = await ReadExactlyAsync((int)count * 4, timeout, cancellationToken).ConfigureAwait(false);
        var capabilities = await ReadVarIntAsync(timeout, cancellationToken).ConfigureAwait(false);
        var capabilitiesBytes = EncodeVarInt(capabilities);

        var result = new byte[marker.Length + VarIntLength(count) + ranges.Length + capabilitiesBytes.Length];
        marker.CopyTo(result, 0);
        EncodeVarInt(count).CopyTo(result, marker.Length);
        ranges.CopyTo(result, marker.Length + VarIntLength(count));
        capabilitiesBytes.CopyTo(result, marker.Length + VarIntLength(count) + ranges.Length);
        return result;
    }

    internal static byte[] EncodeVarInt(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            bytes.Add(b);
        }
        while (value != 0);

        return bytes.ToArray();
    }

    internal static int VarIntLength(ulong value)
    {
        var length = 1;
        while ((value >>= 7) != 0)
        {
            length++;
        }

        return length;
    }

    private static CancellationTokenSource CreateTimeout(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
