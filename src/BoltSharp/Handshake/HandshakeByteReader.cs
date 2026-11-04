namespace BoltSharp.Handshake;

/// <summary>
/// A small bounded reader over an ordered byte stream used by the handshake layer. It reads
/// exactly the requested number of bytes (handling arbitrary partial reads), counts every byte
/// consumed, and fails deterministically when the stream ends early or the configured byte
/// budget is exceeded. It performs no buffering beyond a single reusable byte, so it never
/// consumes bytes belonging to a pipelined message that follows the handshake.
/// </summary>
internal sealed class HandshakeByteReader
{
    private readonly Stream _stream;
    private readonly int _maxBytes;
    private readonly byte[] _single = new byte[1];
    private int _consumed;

    internal HandshakeByteReader(Stream stream, int maxBytes)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maxBytes = maxBytes;
    }

    /// <summary>Total number of bytes consumed from the stream so far.</summary>
    internal int Consumed => _consumed;

    /// <summary>
    /// Reads one byte, returning -1 at end of stream. Raises
    /// <see cref="BoltHandshakeFormatException"/> if the byte budget would be exceeded.
    /// </summary>
    internal int ReadByte()
    {
        EnsureWithinBudget(1);
        var count = _stream.Read(_single, 0, 1);
        if (count <= 0)
        {
            return -1;
        }

        _consumed += count;
        return _single[0];
    }

    internal async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        EnsureWithinBudget(1);
        var count = await _stream.ReadAsync(_single.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (count <= 0)
        {
            return -1;
        }

        _consumed += count;
        return _single[0];
    }

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes or raises truncation.</summary>
    internal void ReadExactly(Span<byte> destination)
    {
        EnsureWithinBudget(destination.Length);

        var read = 0;
        while (read < destination.Length)
        {
            var count = _stream.Read(destination.Slice(read));
            if (count <= 0)
            {
                throw new BoltHandshakeTruncatedException(
                    $"The stream ended {destination.Length - read} byte(s) before a " +
                    $"{destination.Length}-byte handshake field was complete.");
            }

            read += count;
        }

        _consumed += read;
    }

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes or raises truncation.</summary>
    internal async ValueTask ReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        EnsureWithinBudget(destination.Length);

        var read = 0;
        while (read < destination.Length)
        {
            var count = await _stream.ReadAsync(destination.Slice(read), cancellationToken).ConfigureAwait(false);
            if (count <= 0)
            {
                throw new BoltHandshakeTruncatedException(
                    $"The stream ended {destination.Length - read} byte(s) before a " +
                    $"{destination.Length}-byte handshake field was complete.");
            }

            read += count;
        }

        _consumed += read;
    }

    /// <summary>
    /// Reads a bounded unsigned 64-bit VarInt (7 payload bits per byte, least significant group
    /// first, high bit as the continuation flag). Raises <see cref="BoltHandshakeTruncatedException"/>
    /// on end of stream and <see cref="BoltHandshakeFormatException"/> on overflow or an
    /// over-long encoding.
    /// </summary>
    internal ulong ReadVarInt()
    {
        var value = 0UL;
        var shift = 0;

        for (var i = 0; i < BoltVarInt.MaxEncodedLength; i++)
        {
            var b = ReadByte();
            if (b < 0)
            {
                throw new BoltHandshakeTruncatedException(
                    "The stream ended in the middle of a VarInt.");
            }

            var payload = (ulong)(b & 0x7F);
            if (shift == 63 && (payload & ~1UL) != 0)
            {
                throw new BoltHandshakeFormatException(
                    "The VarInt value overflows an unsigned 64-bit integer.");
            }

            value |= payload << shift;

            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        throw new BoltHandshakeFormatException(
            $"A VarInt may not exceed {BoltVarInt.MaxEncodedLength} bytes.");
    }

    internal async ValueTask<ulong> ReadVarIntAsync(CancellationToken cancellationToken)
    {
        var value = 0UL;
        var shift = 0;

        for (var i = 0; i < BoltVarInt.MaxEncodedLength; i++)
        {
            var b = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (b < 0)
            {
                throw new BoltHandshakeTruncatedException(
                    "The stream ended in the middle of a VarInt.");
            }

            var payload = (ulong)(b & 0x7F);
            if (shift == 63 && (payload & ~1UL) != 0)
            {
                throw new BoltHandshakeFormatException(
                    "The VarInt value overflows an unsigned 64-bit integer.");
            }

            value |= payload << shift;

            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        throw new BoltHandshakeFormatException(
            $"A VarInt may not exceed {BoltVarInt.MaxEncodedLength} bytes.");
    }

    private void EnsureWithinBudget(int additional)
    {
        if (additional > _maxBytes - _consumed)
        {
            throw new BoltHandshakeFormatException(
                $"The handshake exceeded the configured maximum of {_maxBytes} byte(s).");
        }
    }
}
