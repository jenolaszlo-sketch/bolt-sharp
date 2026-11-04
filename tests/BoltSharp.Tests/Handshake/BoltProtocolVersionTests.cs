using System.Buffers.Binary;

using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Golden wire encodings for <see cref="BoltProtocolVersion"/> and rejection of reserved bits.
/// </summary>
public sealed class BoltProtocolVersionTests
{
    [Theory]
    [InlineData(1, 0, 0x00, 0x00, 0x00, 0x01)]
    [InlineData(4, 1, 0x00, 0x00, 0x01, 0x04)]
    [InlineData(5, 7, 0x00, 0x00, 0x07, 0x05)]
    [InlineData(5, 8, 0x00, 0x00, 0x08, 0x05)]
    [InlineData(6, 0, 0x00, 0x00, 0x00, 0x06)]
    [InlineData(255, 255, 0x00, 0x00, 0xFF, 0xFF)]
    public void WriteBytes_MatchesSpecificationEncoding(int major, int minor, int b0, int b1, int b2, int b3)
    {
        var version = new BoltProtocolVersion(major, minor);
        Span<byte> span = stackalloc byte[4];

        version.WriteBytes(span);

        Assert.Equal(new[] { (byte)b0, (byte)b1, (byte)b2, (byte)b3 }, span.ToArray());
        Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(span), version.ToUInt32());
    }

    [Fact]
    public void ToUInt32_MajorIsLeastSignificantByte()
    {
        // "00 00 01 04" is Bolt 4.1: minor = 0x01, major = 0x04.
        Assert.Equal(0x00000104u, new BoltProtocolVersion(4, 1).ToUInt32());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 8)]
    [InlineData(255, 255)]
    public void FromUInt32_RoundTrips(int major, int minor)
    {
        var version = new BoltProtocolVersion(major, minor);

        Assert.Equal(version, BoltProtocolVersion.FromUInt32(version.ToUInt32()));
    }

    [Theory]
    [InlineData(0x00010000u)] // reserved high 16 bits set
    [InlineData(0x01000000u)]
    [InlineData(0x00020000u)]
    [InlineData(0xFFFF0000u)]
    public void FromUInt32_RejectsReservedBits(uint value)
    {
        Assert.Throws<BoltHandshakeFormatException>(() => BoltProtocolVersion.FromUInt32(value));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(256, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 256)]
    public void Constructor_RejectsOutOfRange(int major, int minor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltProtocolVersion(major, minor));
    }

    [Fact]
    public void ToString_UsesMajorDotMinor()
    {
        Assert.Equal("5.8", new BoltProtocolVersion(5, 8).ToString());
    }

    [Fact]
    public void Comparison_OrdersByMajorThenMinor()
    {
        var v51 = new BoltProtocolVersion(5, 1);
        var v58 = new BoltProtocolVersion(5, 8);
        var v60 = new BoltProtocolVersion(6, 0);

        Assert.True(v51 < v58);
        Assert.True(v58 < v60);
        Assert.True(v60 > v51);
        Assert.Equal(0, v58.CompareTo(new BoltProtocolVersion(5, 8)));
    }
}
