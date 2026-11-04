using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.Handshake;

/// <summary>
/// The Bolt Manifest v1 handshake codec (introduced with Bolt 5.7). The client may substitute
/// the manifest marker <c>00 00 01 FF</c> for one of its four version proposals; the server then
/// answers with the marker, a bounded VarInt count of supported version ranges (Bolt 4.3 style),
/// and a bounded VarInt capability bitmask. The client replies with a concrete Bolt 4.0 style
/// version and a capability subset, and may pipeline its first message immediately afterwards.
/// </summary>
public static class BoltManifestV1
{
    /// <summary>The four manifest marker bytes <c>00 00 01 FF</c> as a big-endian value.</summary>
    public const uint Marker = 0x0000_01FFu;

    /// <summary>The manifest version encoded by <see cref="Marker"/>.</summary>
    public const int Version = 1;

    /// <summary>The four marker bytes in wire order.</summary>
    public static ReadOnlyMemory<byte> MarkerBytes { get; } = new byte[] { 0x00, 0x00, 0x01, 0xFF };

    /// <summary>
    /// Writes a manifest offer: the marker, the range count, the ranges in Bolt 4.3 style, and
    /// the offered capability bitmask.
    /// </summary>
    public static void WriteOffer(
        IBufferWriter<byte> output,
        IReadOnlyList<BoltVersionRange> ranges,
        ulong capabilities)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(ranges);

        WriteMarker(output);
        BoltVarInt.Write(output, (ulong)ranges.Count);
        foreach (var range in ranges)
        {
            var span = output.GetSpan(4);
            range.WriteBytes(span);
            output.Advance(4);
        }

        BoltVarInt.Write(output, capabilities);
    }

    /// <summary>Writes a manifest selection: a concrete Bolt 4.0 style version and a capability subset.</summary>
    public static void WriteSelection(IBufferWriter<byte> output, BoltProtocolVersion version, ulong capabilities)
    {
        ArgumentNullException.ThrowIfNull(output);

        var span = output.GetSpan(4);
        version.WriteBytes(span);
        output.Advance(4);
        BoltVarInt.Write(output, capabilities);
    }

    /// <summary>Reads and validates a manifest offer from <paramref name="stream"/>.</summary>
    public static BoltManifestOffer ReadOffer(Stream stream, BoltHandshakeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var effective = limits ?? BoltHandshakeLimits.Default;
        var reader = new HandshakeByteReader(stream, effective.MaxHandshakeBytes);
        return ReadOffer(reader, effective);
    }

    /// <summary>Reads and validates a manifest selection from <paramref name="stream"/>.</summary>
    public static BoltManifestSelection ReadSelection(Stream stream, BoltHandshakeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var effective = limits ?? BoltHandshakeLimits.Default;
        var reader = new HandshakeByteReader(stream, effective.MaxHandshakeBytes);
        return ReadSelection(reader);
    }

    internal static BoltManifestOffer ReadOffer(HandshakeByteReader reader, BoltHandshakeLimits limits)
    {
        Span<byte> marker = stackalloc byte[4];
        reader.ReadExactly(marker);
        if (BinaryPrimitives.ReadUInt32BigEndian(marker) != Marker)
        {
            throw new BoltHandshakeFormatException(
                "The manifest offer did not begin with the manifest v1 marker (00 00 01 FF).");
        }

        var count = reader.ReadVarInt();
        if (count > (ulong)limits.MaxManifestVersions)
        {
            throw new BoltHandshakeFormatException(
                $"The manifest offer declares {count} version ranges, exceeding the configured " +
                $"maximum of {limits.MaxManifestVersions}.");
        }

        var ranges = new BoltVersionRange[(int)count];
        Span<byte> entry = stackalloc byte[4];
        for (var i = 0; i < ranges.Length; i++)
        {
            reader.ReadExactly(entry);
            ranges[i] = BoltVersionRange.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(entry));
        }

        var capabilities = reader.ReadVarInt();
        return new BoltManifestOffer(ranges, capabilities);
    }

    internal static async ValueTask<BoltManifestOffer> ReadOfferAsync(
        HandshakeByteReader reader,
        BoltHandshakeLimits limits,
        CancellationToken cancellationToken)
    {
        var marker = new byte[4];
        await reader.ReadExactlyAsync(marker, cancellationToken).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32BigEndian(marker) != Marker)
        {
            throw new BoltHandshakeFormatException(
                "The manifest offer did not begin with the manifest v1 marker (00 00 01 FF).");
        }

        var count = await reader.ReadVarIntAsync(cancellationToken).ConfigureAwait(false);
        if (count > (ulong)limits.MaxManifestVersions)
        {
            throw new BoltHandshakeFormatException(
                $"The manifest offer declares {count} version ranges, exceeding the configured " +
                $"maximum of {limits.MaxManifestVersions}.");
        }

        var ranges = new BoltVersionRange[(int)count];
        var entry = new byte[4];
        for (var i = 0; i < ranges.Length; i++)
        {
            await reader.ReadExactlyAsync(entry, cancellationToken).ConfigureAwait(false);
            ranges[i] = BoltVersionRange.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(entry));
        }

        var capabilities = await reader.ReadVarIntAsync(cancellationToken).ConfigureAwait(false);
        return new BoltManifestOffer(ranges, capabilities);
    }

    internal static BoltManifestSelection ReadSelection(HandshakeByteReader reader)
    {
        Span<byte> versionBytes = stackalloc byte[4];
        reader.ReadExactly(versionBytes);
        var version = BoltProtocolVersion.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(versionBytes));
        var capabilities = reader.ReadVarInt();
        return new BoltManifestSelection(version, capabilities);
    }

    internal static async ValueTask<BoltManifestSelection> ReadSelectionAsync(
        HandshakeByteReader reader,
        CancellationToken cancellationToken)
    {
        var versionBytes = new byte[4];
        await reader.ReadExactlyAsync(versionBytes, cancellationToken).ConfigureAwait(false);
        var version = BoltProtocolVersion.FromUInt32(BinaryPrimitives.ReadUInt32BigEndian(versionBytes));
        var capabilities = await reader.ReadVarIntAsync(cancellationToken).ConfigureAwait(false);
        return new BoltManifestSelection(version, capabilities);
    }

    private static void WriteMarker(IBufferWriter<byte> output)
    {
        var span = output.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, Marker);
        output.Advance(4);
    }
}

/// <summary>A parsed and structurally validated Manifest v1 offer.</summary>
public sealed class BoltManifestOffer
{
    internal BoltManifestOffer(IReadOnlyList<BoltVersionRange> ranges, ulong capabilities)
    {
        Ranges = ranges.ToArray();
        Capabilities = capabilities;
    }

    /// <summary>The offered version ranges, in the order they appeared on the wire.</summary>
    public IReadOnlyList<BoltVersionRange> Ranges { get; }

    /// <summary>The offered capability bitmask.</summary>
    public ulong Capabilities { get; }

    /// <summary>Every concrete version covered by the offer.</summary>
    public IReadOnlyList<BoltProtocolVersion> Versions
    {
        get
        {
            var versions = new List<BoltProtocolVersion>();
            foreach (var range in Ranges)
            {
                versions.AddRange(range.Expand());
            }

            return versions;
        }
    }

    /// <summary>Whether the offer covers the given concrete version.</summary>
    public bool Contains(BoltProtocolVersion version)
    {
        foreach (var range in Ranges)
        {
            if (range.Contains(version))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A parsed and structurally validated Manifest v1 client selection.</summary>
public sealed class BoltManifestSelection
{
    internal BoltManifestSelection(BoltProtocolVersion version, ulong capabilities)
    {
        Version = version;
        Capabilities = capabilities;
    }

    /// <summary>The concrete version the client selected.</summary>
    public BoltProtocolVersion Version { get; }

    /// <summary>The capability bitmask the client selected.</summary>
    public ulong Capabilities { get; }
}
