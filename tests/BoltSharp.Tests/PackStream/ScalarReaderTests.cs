using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte tests for the PackStream 1.0 scalar reader. The reader must accept
/// every valid representation, including wider ones than the writer would choose.
/// </summary>
public sealed class ScalarReaderTests
{
    [Fact]
    public void ReadNull_ConsumesExactlyOneByte()
    {
        var reader = new PackStreamReader(new byte[] { 0xC0, 0x2A });

        reader.ReadNull();

        Assert.Equal(1, reader.Position);
        Assert.True(reader.HasMore);
        Assert.Equal(42L, reader.ReadInteger());
    }

    [Theory]
    [InlineData(new byte[] { 0xC3 }, true)]
    [InlineData(new byte[] { 0xC2 }, false)]
    public void ReadBoolean_DecodesMarker(byte[] bytes, bool expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Decode(bytes, r => r.ReadBoolean()));

    [Theory]
    // The specification's worked example: the value 42 in every representation.
    [InlineData(new byte[] { 0x2A }, 42L)]
    [InlineData(new byte[] { 0xC8, 0x2A }, 42L)]
    [InlineData(new byte[] { 0xC9, 0x00, 0x2A }, 42L)]
    [InlineData(new byte[] { 0xCA, 0x00, 0x00, 0x00, 0x2A }, 42L)]
    [InlineData(new byte[] { 0xCB, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A }, 42L)]
    // Tiny integer boundaries.
    [InlineData(new byte[] { 0x00 }, 0L)]
    [InlineData(new byte[] { 0x7F }, 127L)]
    [InlineData(new byte[] { 0xF0 }, -16L)]
    [InlineData(new byte[] { 0xFF }, -1L)]
    // Signed 8/16/32/64-bit representations and their boundaries.
    [InlineData(new byte[] { 0xC8, 0xEF }, -17L)]
    [InlineData(new byte[] { 0xC8, 0x80 }, -128L)]
    [InlineData(new byte[] { 0xC9, 0xFF, 0x7F }, -129L)]
    [InlineData(new byte[] { 0xC9, 0x80, 0x00 }, -32768L)]
    [InlineData(new byte[] { 0xCA, 0xFF, 0xFF, 0x7F, 0xFF }, -32769L)]
    [InlineData(new byte[] { 0xCA, 0x80, 0x00, 0x00, 0x00 }, -2147483648L)]
    [InlineData(new byte[] { 0xCA, 0x7F, 0xFF, 0xFF, 0xFF }, 2147483647L)]
    [InlineData(new byte[] { 0xCB, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF }, -2147483649L)]
    [InlineData(new byte[] { 0xCB, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00 }, 2147483648L)]
    [InlineData(new byte[] { 0xCB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, long.MinValue)]
    [InlineData(new byte[] { 0xCB, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, long.MaxValue)]
    // Wider-than-necessary encodings of small values must be accepted.
    [InlineData(new byte[] { 0xCB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, -1L)]
    [InlineData(new byte[] { 0xCA, 0x00, 0x00, 0x00, 0x00 }, 0L)]
    [InlineData(new byte[] { 0xC9, 0x00, 0x01 }, 1L)]
    public void ReadInteger_AcceptsEveryValidRepresentation(byte[] bytes, long expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Decode(bytes, r => r.ReadInteger()));

    [Fact]
    public void ReadInteger_LeavesTrailingBytesUntouched()
    {
        var reader = new PackStreamReader(new byte[] { 0xC9, 0x00, 0x2A, 0x7F });

        Assert.Equal(42L, reader.ReadInteger());
        Assert.Equal(3, reader.Position);
        Assert.True(reader.HasMore);
        Assert.Equal(127L, reader.ReadInteger());
        Assert.Equal(4, reader.Position);
        Assert.False(reader.HasMore);
    }

    [Fact]
    public void ReadFloat_OnePointTwoThree_MatchesOfficialExample()
    {
        var bytes = new byte[] { 0xC1, 0x3F, 0xF3, 0xAE, 0x14, 0x7A, 0xE1, 0x47, 0xAE };

        Assert.Equal(1.23, PackStreamTestHelpers.Decode(bytes, r => r.ReadFloat()));
    }

    [Fact]
    public void ReadFloat_PositiveAndNegativeZero_AreDistinct()
    {
        var positive = PackStreamTestHelpers.Decode(
            new byte[] { 0xC1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            r => r.ReadFloat());
        var negative = PackStreamTestHelpers.Decode(
            new byte[] { 0xC1, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            r => r.ReadFloat());

        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(positive));
        Assert.Equal(unchecked((long)0x8000000000000000UL), BitConverter.DoubleToInt64Bits(negative));
        Assert.Equal(0.0, positive);
        Assert.Equal(-0.0, negative);
        Assert.NotEqual(BitConverter.DoubleToInt64Bits(positive), BitConverter.DoubleToInt64Bits(negative));
    }

    [Fact]
    public void ReadFloat_Infinity()
    {
        Assert.True(double.IsPositiveInfinity(PackStreamTestHelpers.Decode(
            new byte[] { 0xC1, 0x7F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            r => r.ReadFloat())));
        Assert.True(double.IsNegativeInfinity(PackStreamTestHelpers.Decode(
            new byte[] { 0xC1, 0xFF, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            r => r.ReadFloat())));
    }

    [Fact]
    public void ReadFloat_NaN_PreservesPayloadBits()
    {
        const long payload = unchecked((long)0x7FF8000000000001UL);
        var bytes = new byte[] { 0xC1, 0x7F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };

        var value = PackStreamTestHelpers.Decode(bytes, r => r.ReadFloat());

        Assert.True(double.IsNaN(value));
        Assert.Equal(payload, BitConverter.DoubleToInt64Bits(value));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(-17L)]
    [InlineData(-128L)]
    [InlineData(-129L)]
    [InlineData(32767L)]
    [InlineData(32768L)]
    [InlineData(-32768L)]
    [InlineData(-32769L)]
    [InlineData(-2147483648L)]
    [InlineData(2147483647L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Integer_RoundTripsThroughGoldenEncoding(long value)
    {
        var bytes = PackStreamTestHelpers.Encode(w => w.WriteInteger(value));

        Assert.Equal(value, PackStreamTestHelpers.Decode(bytes, r => r.ReadInteger()));
    }
}
