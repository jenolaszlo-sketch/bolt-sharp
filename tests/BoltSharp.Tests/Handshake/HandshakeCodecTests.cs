using System.Buffers;

using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Manifest v1 offer and selection codec: golden bytes, independent reader/writer
/// interoperability, and bounded/malformed parsing.
/// </summary>
public sealed class HandshakeCodecTests
{
    private static byte[] WriteOffer(uint[] rangeValues, ulong capabilities)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var ranges = new List<BoltVersionRange>();
        foreach (var value in rangeValues)
        {
            ranges.Add(BoltVersionRange.FromUInt32(value));
        }

        BoltManifestV1.WriteOffer(buffer, ranges, capabilities);
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WriteSelection(int major, int minor, ulong capabilities)
    {
        var buffer = new ArrayBufferWriter<byte>();
        BoltManifestV1.WriteSelection(buffer, new BoltProtocolVersion(major, minor), capabilities);
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void WriteOffer_MatchesIndependentGoldenBytes()
    {
        var expected = HandshakeTestHelpers.Offer(new uint[] { 0x00020805u, 0x00040404u }, 0x09);

        Assert.Equal(expected, WriteOffer(new uint[] { 0x00020805u, 0x00040404u }, 0x09));
    }

    [Fact]
    public void ReadOffer_ParsesIndependentGoldenBytes()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x00020805u, 0x00040404u }, 0x09);

        var offer = BoltManifestV1.ReadOffer(new MemoryStream(bytes));

        Assert.Equal(
            new[] { new BoltVersionRange(5, 8, 2), new BoltVersionRange(4, 4, 4) },
            offer.Ranges);
        Assert.Equal(0x09UL, offer.Capabilities);
        Assert.True(offer.Contains(new BoltProtocolVersion(5, 6)));
        Assert.False(offer.Contains(new BoltProtocolVersion(5, 5)));
    }

    [Fact]
    public void ReadOffer_EmptyVersionList_IsAccepted()
    {
        var bytes = HandshakeTestHelpers.Offer(Array.Empty<uint>(), 0);

        var offer = BoltManifestV1.ReadOffer(new MemoryStream(bytes));

        Assert.Empty(offer.Ranges);
        Assert.Empty(offer.Versions);
    }

    [Fact]
    public void WriteSelection_MatchesIndependentGoldenBytes()
    {
        var expected = HandshakeTestHelpers.Selection(5, 7, 0x08);

        Assert.Equal(expected, WriteSelection(5, 7, 0x08));
    }

    [Fact]
    public void ReadSelection_ParsesIndependentGoldenBytes()
    {
        var bytes = HandshakeTestHelpers.Selection(5, 7, 0x08);

        var selection = BoltManifestV1.ReadSelection(new MemoryStream(bytes));

        Assert.Equal(new BoltProtocolVersion(5, 7), selection.Version);
        Assert.Equal(0x08UL, selection.Capabilities);
    }

    [Fact]
    public void ReadOffer_WrongMarker_ThrowsFormat()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0);
        bytes[3] = 0xFE;

        Assert.Throws<BoltHandshakeFormatException>(() => BoltManifestV1.ReadOffer(new MemoryStream(bytes)));
    }

    [Fact]
    public void ReadOffer_TruncatedMarker_ThrowsTruncated()
    {
        Assert.Throws<BoltHandshakeTruncatedException>(
            () => BoltManifestV1.ReadOffer(new MemoryStream(new byte[] { 0x00, 0x00, 0x01 })));
    }

    [Fact]
    public void ReadOffer_TooManyVersions_ThrowsFormatWithoutAllocating()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x00000805u, 0x00000705u }, 0);
        var limits = new BoltHandshakeLimits(maxManifestVersions: 1);

        Assert.Throws<BoltHandshakeFormatException>(
            () => BoltManifestV1.ReadOffer(new MemoryStream(bytes), limits));
    }

    [Fact]
    public void ReadOffer_TruncatedRangeEntry_ThrowsTruncated()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0);
        var truncated = bytes[..^3]; // drop the range entry and capabilities

        Assert.Throws<BoltHandshakeTruncatedException>(
            () => BoltManifestV1.ReadOffer(new MemoryStream(truncated)));
    }

    [Fact]
    public void ReadOffer_ReservedByteInRange_ThrowsFormat()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x01000805u }, 0);

        Assert.Throws<BoltHandshakeFormatException>(() => BoltManifestV1.ReadOffer(new MemoryStream(bytes)));
    }

    [Fact]
    public void ReadOffer_RangeSpanningMajor_ThrowsFormat()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { HandshakeTestHelpers.Range(4, 1, 2) }, 0);

        Assert.Throws<BoltHandshakeFormatException>(() => BoltManifestV1.ReadOffer(new MemoryStream(bytes)));
    }

    [Fact]
    public void ReadOffer_ExceedingByteBudget_ThrowsFormat()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0);
        var limits = new BoltHandshakeLimits(maxManifestVersions: 256, maxHandshakeBytes: 5);

        Assert.Throws<BoltHandshakeFormatException>(
            () => BoltManifestV1.ReadOffer(new MemoryStream(bytes), limits));
    }

    [Fact]
    public void ReadSelection_TruncatedCapabilities_ThrowsTruncated()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x08, 0x05, 0x80 };

        Assert.Throws<BoltHandshakeTruncatedException>(
            () => BoltManifestV1.ReadSelection(new MemoryStream(bytes)));
    }

    [Fact]
    public void ReadOffer_Versions_EnumerateInWireOrder()
    {
        var bytes = HandshakeTestHelpers.Offer(new uint[] { 0x00020805u }, 0);

        var offer = BoltManifestV1.ReadOffer(new MemoryStream(bytes));

        Assert.Equal(
            new[]
            {
                new BoltProtocolVersion(5, 8),
                new BoltProtocolVersion(5, 7),
                new BoltProtocolVersion(5, 6),
            },
            offer.Versions);
    }

    [Fact]
    public void ReadSelection_RangeValue_ThrowsFormat()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x07, 0x05, 0x00 };

        Assert.Throws<BoltHandshakeFormatException>(
            () => BoltManifestV1.ReadSelection(new MemoryStream(bytes)));
    }
}
