using System.Buffers;

namespace BoltSharp.Handshake;

/// <summary>
/// Encodes and decodes the Bolt Manifest v1 VarInt: an unsigned integer transmitted in 7-bit
/// groups, least significant group first, where the high bit of each byte is a continuation
/// flag. Values are bounded to an unsigned 64-bit integer and to
/// <see cref="MaxEncodedLength"/> bytes.
/// <para>
/// Decoding rejects overflow beyond 64 bits and an encoding longer than
/// <see cref="MaxEncodedLength"/>; a non-minimal (zero-padded) encoding is accepted because the
/// specification does not require the shortest form. It performs no unbounded allocation.
/// </para>
/// </summary>
public static class BoltVarInt
{
    /// <summary>
    /// The maximum number of bytes needed to encode an unsigned 64-bit integer in 7-bit
    /// groups (ceil(64 / 7) = 10).
    /// </summary>
    public const int MaxEncodedLength = 10;

    /// <summary>The number of bytes <paramref name="value"/> occupies when encoded.</summary>
    public static int GetEncodedLength(ulong value)
    {
        var length = 1;
        while ((value >>= 7) != 0)
        {
            length++;
        }

        return length;
    }

    /// <summary>Writes the VarInt encoding of <paramref name="value"/>.</summary>
    public static void Write(IBufferWriter<byte> output, ulong value)
    {
        ArgumentNullException.ThrowIfNull(output);

        var span = output.GetSpan(MaxEncodedLength);
        var count = 0;
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            span[count++] = b;
        }
        while (value != 0);

        output.Advance(count);
    }

    /// <summary>
    /// Attempts to decode a VarInt from the start of <paramref name="source"/>. Returns
    /// <see langword="false"/> if the source does not begin with a complete, valid bounded
    /// VarInt; on success <paramref name="bytesConsumed"/> is the encoded length.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out ulong value, out int bytesConsumed)
    {
        value = 0;
        bytesConsumed = 0;
        var shift = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (i >= MaxEncodedLength)
            {
                value = 0;
                bytesConsumed = 0;
                return false;
            }

            var b = source[i];
            var payload = (ulong)(b & 0x7F);
            if (shift == 63 && (payload & ~1UL) != 0)
            {
                value = 0;
                bytesConsumed = 0;
                return false;
            }

            value |= payload << shift;
            bytesConsumed = i + 1;

            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        bytesConsumed = 0;
        return false;
    }

    /// <summary>
    /// Reads a bounded VarInt from <paramref name="stream"/>.
    /// </summary>
    /// <exception cref="BoltHandshakeTruncatedException">The stream ended mid-VarInt.</exception>
    /// <exception cref="BoltHandshakeFormatException">The VarInt overflows 64 bits or exceeds 10 bytes.</exception>
    public static ulong Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new HandshakeByteReader(stream, int.MaxValue);
        return reader.ReadVarInt();
    }

    /// <summary>Asynchronously reads a bounded VarInt from <paramref name="stream"/>.</summary>
    public static async ValueTask<ulong> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new HandshakeByteReader(stream, int.MaxValue);
        return await reader.ReadVarIntAsync(cancellationToken).ConfigureAwait(false);
    }
}
