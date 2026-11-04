using System.Buffers;

using BoltSharp.Handshake;

using BoltSharp.Tests.Framing;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Golden, boundary, overflow and truncation tests for the Manifest v1 VarInt codec.
/// </summary>
public sealed class BoltVarIntTests
{
    private static byte[] Write(ulong value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        BoltVarInt.Write(buffer, value);
        return buffer.WrittenSpan.ToArray();
    }

    private static ulong Read(byte[] bytes) => BoltVarInt.Read(new MemoryStream(bytes));

    [Theory]
    [InlineData(0UL, new byte[] { 0x00 })]
    [InlineData(1UL, new byte[] { 0x01 })]
    [InlineData(127UL, new byte[] { 0x7F })]
    [InlineData(128UL, new byte[] { 0x80, 0x01 })]
    [InlineData(255UL, new byte[] { 0xFF, 0x01 })]
    [InlineData(300UL, new byte[] { 0xAC, 0x02 })]
    [InlineData(16_383UL, new byte[] { 0xFF, 0x7F })]
    [InlineData(16_384UL, new byte[] { 0x80, 0x80, 0x01 })]
    [InlineData(1_851_775UL, new byte[] { 0xFF, 0x82, 0x71 })]
    public void Write_MatchesSpecificationGoldenVectors(ulong value, byte[] expected)
    {
        Assert.Equal(expected, Write(value));
        Assert.Equal(expected, HandshakeTestHelpers.VarInt(value));
        Assert.Equal(value, Read(expected));
    }

    [Theory]
    [InlineData(4_294_967_296UL, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 })]
    [InlineData(9_223_372_036_854_775_808UL, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })]
    [InlineData(18_446_744_073_709_551_615UL, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
    public void Write_MatchesBoundaryGoldenVectors(ulong value, byte[] expected)
    {
        Assert.Equal(expected, Write(value));
        Assert.Equal(value, Read(expected));
        Assert.Equal(10, BoltVarInt.MaxEncodedLength);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(127UL)]
    [InlineData(128UL)]
    [InlineData(16_383UL)]
    [InlineData(16_384UL)]
    [InlineData(1_851_775UL)]
    [InlineData(4_294_967_296UL)]
    [InlineData(9_223_372_036_854_775_808UL)]
    [InlineData(18_446_744_073_709_551_615UL)]
    public void RoundTrip_ThroughStream(ulong value)
    {
        Assert.Equal(value, Read(Write(value)));
    }

    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(127UL, 1)]
    [InlineData(128UL, 2)]
    [InlineData(1_851_775UL, 3)]
    [InlineData(18_446_744_073_709_551_615UL, 10)]
    public void GetEncodedLength_MatchesWrittenLength(ulong value, int expected)
    {
        Assert.Equal(expected, BoltVarInt.GetEncodedLength(value));
        Assert.Equal(expected, Write(value).Length);
    }

    [Fact]
    public void Write_AppendsToExistingDestination()
    {
        var buffer = new ArrayBufferWriter<byte>();
        buffer.Write(new byte[] { 0xAA });
        BoltVarInt.Write(buffer, 1UL);

        Assert.Equal(new byte[] { 0xAA, 0x01 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Read_TruncatedContinuation_ThrowsTruncated()
    {
        Assert.Throws<BoltHandshakeTruncatedException>(() => Read(new byte[] { 0x80 }));
    }

    [Fact]
    public void Read_TruncatedOverMultipleBytes_ThrowsTruncated()
    {
        Assert.Throws<BoltHandshakeTruncatedException>(() => Read(new byte[] { 0xFF, 0x82 }));
    }

    [Fact]
    public void Read_EmptyStream_ThrowsTruncated()
    {
        Assert.Throws<BoltHandshakeTruncatedException>(() => Read(Array.Empty<byte>()));
    }

    [Fact]
    public void Read_TenthByteOverflow_ThrowsFormat()
    {
        // Ten bytes, the last contributing bits above bit 63.
        var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };
        Assert.Throws<BoltHandshakeFormatException>(() => Read(bytes));
    }

    [Fact]
    public void Read_EleventhByteContinuation_ThrowsFormat()
    {
        var bytes = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        Assert.Throws<BoltHandshakeFormatException>(() => Read(bytes));
    }

    [Fact]
    public void Read_AllowsNonMinimalEncodingButBoundedByTenBytes()
    {
        // 0x80 0x00 is a non-minimal encoding of zero; the specification does not require the
        // shortest form, so it is accepted.
        Assert.Equal(0UL, Read(new byte[] { 0x80, 0x00 }));
    }

    [Fact]
    public void TryRead_DecodesGoldenVector()
    {
        Assert.True(BoltVarInt.TryRead(new byte[] { 0xFF, 0x82, 0x71 }, out var value, out var consumed));
        Assert.Equal(1_851_775UL, value);
        Assert.Equal(3, consumed);
    }

    [Fact]
    public void TryRead_Incomplete_ReturnsFalse()
    {
        Assert.False(BoltVarInt.TryRead(new byte[] { 0x80 }, out _, out _));
    }

    [Fact]
    public void TryRead_Overflow_ReturnsFalse()
    {
        Assert.False(BoltVarInt.TryRead(
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 },
            out _,
            out _));
    }

    [Fact]
    public async Task ReadAsync_MatchesSync()
    {
        var bytes = Write(18_446_744_073_709_551_615UL);
        Assert.Equal(18_446_744_073_709_551_615UL, await BoltVarInt.ReadAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_FragmentedOneByteAtATime_ReconstructsValue()
    {
        var bytes = Write(1_851_775UL);
        using var stream = new FragmentingStream(bytes, maxReadSize: 1);

        Assert.Equal(1_851_775UL, BoltVarInt.Read(stream));
    }

    [Fact]
    public async Task ReadAsync_FragmentedOneByteAtATime_ReconstructsValue()
    {
        var bytes = Write(18_446_744_073_709_551_615UL);
        using var stream = new FragmentingStream(bytes, maxReadSize: 1);

        Assert.Equal(18_446_744_073_709_551_615UL, await BoltVarInt.ReadAsync(stream));
    }
}
