namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Hand-built handshake fixtures. These encoders are deliberately independent of the production
/// writer so that the reader and writer can be falsified separately.
/// </summary>
internal static class HandshakeTestHelpers
{
    internal static readonly byte[] Magic = { 0x60, 0x60, 0xB0, 0x17 };

    /// <summary>The Bolt version 4.0 style value for a concrete version (minor then major).</summary>
    internal static uint Version(int major, int minor) => (uint)((minor << 8) | major);

    /// <summary>The Bolt version 4.3 style value for a minor-version range.</summary>
    internal static uint Range(int major, int minor, int range) =>
        (uint)((range << 16) | (minor << 8) | major);

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
    internal static byte[] ClientBytes(params uint[] proposals)
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

    /// <summary>An independent VarInt encoder used to build fixtures.</summary>
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

    /// <summary>An independent Manifest v1 offer encoder (marker, count, ranges, capabilities).</summary>
    internal static byte[] Offer(uint[] rangeValues, ulong capabilities)
    {
        var bytes = new List<byte> { 0x00, 0x00, 0x01, 0xFF };
        bytes.AddRange(VarInt((ulong)rangeValues.Length));
        foreach (var value in rangeValues)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        bytes.AddRange(VarInt(capabilities));
        return bytes.ToArray();
    }

    /// <summary>An independent Manifest v1 selection encoder (concrete version, capabilities).</summary>
    internal static byte[] Selection(int major, int minor, ulong capabilities) =>
        Concat(new[] { (byte)0x00, (byte)0x00, (byte)minor, (byte)major }, VarInt(capabilities));

    /// <summary>A deterministic, position-dependent pattern of the requested length.</summary>
    internal static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 37) + 11);
        }

        return data;
    }
}
