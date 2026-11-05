namespace BoltSharp.Tests.Harness;

/// <summary>
/// Independent literal wire fixtures for the harness tests. These encoders are deliberately
/// hand-written from the specification (magic, four proposals, Bolt 4.0/4.3 version encodings,
/// VarInt, chunk framing) so that decoder tests do not depend on the production writers.
/// </summary>
internal static class HarnessWire
{
    internal static readonly byte[] Magic = { 0x60, 0x60, 0xB0, 0x17 };

    /// <summary>Bolt 4.0 style concrete version (<c>00 00 minor major</c>).</summary>
    internal static uint Version(int major, int minor) => (uint)((minor << 8) | major);

    /// <summary>Bolt 4.3 style range (<c>00 range minor major</c>).</summary>
    internal static uint Range(int major, int minor, int range) => (uint)((range << 16) | (minor << 8) | major);

    /// <summary>The Manifest v1 marker <c>00 00 01 FF</c>.</summary>
    internal const uint ManifestMarker = 0x0000_01FFu;

    internal static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>Magic plus exactly four big-endian 32-bit proposals, zero-padded.</summary>
    internal static byte[] Preamble(params uint[] proposals)
    {
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

    /// <summary>A Manifest v1 selection (concrete version + VarInt capabilities).</summary>
    internal static byte[] Selection(int major, int minor, ulong capabilities) =>
        Concat(new byte[] { 0x00, 0x00, (byte)minor, (byte)major }, VarInt(capabilities));

    /// <summary>An independent least-significant-7-bit-group VarInt encoder.</summary>
    internal static byte[] VarInt(ulong value)
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

    /// <summary>
    /// An independent chunked-message framing encoder: 2-byte big-endian chunk lengths, chunk
    /// payloads of at most 65,535 bytes, then the <c>00 00</c> terminator. Empty payloads are
    /// rejected (they are indistinguishable from a NOOP).
    /// </summary>
    internal static byte[] Frame(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new ArgumentException("An empty payload cannot be framed distinctly from a NOOP.", nameof(payload));
        }

        var bytes = new List<byte>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var length = Math.Min(65_535, payload.Length - offset);
            bytes.Add((byte)(length >> 8));
            bytes.Add((byte)length);
            for (var i = 0; i < length; i++)
            {
                bytes.Add(payload[offset + i]);
            }

            offset += length;
        }

        bytes.Add(0x00);
        bytes.Add(0x00);
        return bytes.ToArray();
    }

    /// <summary>A deterministic payload of the requested length.</summary>
    internal static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 31) + 7);
        }

        return data;
    }
}
