using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte tests for the PackStream 1.0 scalar writer.
/// Expected bytes are taken from the PackStream specification
/// (https://neo4j.com/docs/bolt/current/packstream/) and its
/// optimal-representation table, not from the writer under test.
/// </summary>
public sealed class ScalarWriterGoldenTests
{
    [Fact]
    public void WriteNull_WritesC0() =>
        Assert.Equal(new byte[] { 0xC0 }, PackStreamTestHelpers.Encode(w => w.WriteNull()));

    [Theory]
    [InlineData(true, new byte[] { 0xC3 })]
    [InlineData(false, new byte[] { 0xC2 })]
    public void WriteBoolean_WritesC3OrC2(bool value, byte[] expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteBoolean(value)));

    [Theory]
    // TINY_INT: -16..127 encoded in the marker byte.
    [InlineData(0L, new byte[] { 0x00 })]
    [InlineData(1L, new byte[] { 0x01 })]
    [InlineData(127L, new byte[] { 0x7F })]
    [InlineData(-1L, new byte[] { 0xFF })]
    [InlineData(-16L, new byte[] { 0xF0 })]
    // INT_8: -128..-17.
    [InlineData(-17L, new byte[] { 0xC8, 0xEF })]
    [InlineData(-128L, new byte[] { 0xC8, 0x80 })]
    // INT_16: -32768..-129 and 128..32767.
    [InlineData(-129L, new byte[] { 0xC9, 0xFF, 0x7F })]
    [InlineData(-32768L, new byte[] { 0xC9, 0x80, 0x00 })]
    [InlineData(128L, new byte[] { 0xC9, 0x00, 0x80 })]
    [InlineData(255L, new byte[] { 0xC9, 0x00, 0xFF })]
    [InlineData(256L, new byte[] { 0xC9, 0x01, 0x00 })]
    [InlineData(32767L, new byte[] { 0xC9, 0x7F, 0xFF })]
    // INT_32: -2147483648..-32769 and 32768..2147483647.
    [InlineData(-32769L, new byte[] { 0xCA, 0xFF, 0xFF, 0x7F, 0xFF })]
    [InlineData(32768L, new byte[] { 0xCA, 0x00, 0x00, 0x80, 0x00 })]
    [InlineData(-2147483648L, new byte[] { 0xCA, 0x80, 0x00, 0x00, 0x00 })]
    [InlineData(2147483647L, new byte[] { 0xCA, 0x7F, 0xFF, 0xFF, 0xFF })]
    // INT_64: everything else.
    [InlineData(-2147483649L, new byte[] { 0xCB, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0xFF, 0xFF, 0xFF })]
    [InlineData(2147483648L, new byte[] { 0xCB, 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00 })]
    [InlineData(long.MinValue, new byte[] { 0xCB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(long.MaxValue, new byte[] { 0xCB, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void WriteInteger_UsesCompactRepresentation(long value, byte[] expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteInteger(value)));

    [Fact]
    public void WriteFloat_PositiveZero_WritesCanonicalBits() =>
        Assert.Equal(
            new byte[] { 0xC1, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            PackStreamTestHelpers.Encode(w => w.WriteFloat(0.0)));

    [Fact]
    public void WriteFloat_NegativeZero_IsDistinctFromPositiveZero()
    {
        var positive = PackStreamTestHelpers.Encode(w => w.WriteFloat(0.0));
        var negative = PackStreamTestHelpers.Encode(w => w.WriteFloat(-0.0));

        Assert.Equal(
            new byte[] { 0xC1, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            negative);
        Assert.NotEqual(positive, negative);
    }

    [Fact]
    public void WriteFloat_OnePointTwoThree_MatchesOfficialExample() =>
        Assert.Equal(
            new byte[] { 0xC1, 0x3F, 0xF3, 0xAE, 0x14, 0x7A, 0xE1, 0x47, 0xAE },
            PackStreamTestHelpers.Encode(w => w.WriteFloat(1.23)));

    [Fact]
    public void WriteFloat_PositiveInfinity() =>
        Assert.Equal(
            new byte[] { 0xC1, 0x7F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            PackStreamTestHelpers.Encode(w => w.WriteFloat(double.PositiveInfinity)));

    [Fact]
    public void WriteFloat_NegativeInfinity() =>
        Assert.Equal(
            new byte[] { 0xC1, 0xFF, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            PackStreamTestHelpers.Encode(w => w.WriteFloat(double.NegativeInfinity)));

    [Fact]
    public void WriteFloat_NaN_PreservesPayloadBits()
    {
        // A non-canonical NaN payload; the writer must not normalise it away.
        const long payload = unchecked((long)0x7FF8000000000001UL);
        var value = BitConverter.Int64BitsToDouble(payload);

        Assert.Equal(
            new byte[] { 0xC1, 0x7F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
            PackStreamTestHelpers.Encode(w => w.WriteFloat(value)));
    }

    [Fact]
    public void MultipleWrites_AppendInOrder()
    {
        var actual = PackStreamTestHelpers.Encode(w =>
        {
            w.WriteNull();
            w.WriteBoolean(true);
            w.WriteInteger(-17);
            w.WriteFloat(1.23);
        });

        Assert.Equal(
            new byte[]
            {
                0xC0,
                0xC3,
                0xC8, 0xEF,
                0xC1, 0x3F, 0xF3, 0xAE, 0x14, 0x7A, 0xE1, 0x47, 0xAE,
            },
            actual);
    }
}
