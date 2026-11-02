using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte and validation tests for generic PackStream 1.0 structures.
/// Expected bytes follow the PackStream specification
/// (https://neo4j.com/docs/bolt/current/packstream/#data-type-structure), independently of
/// the codec under test. Tags are not interpreted.
/// </summary>
public sealed class StructureGoldenTests
{
    [Fact]
    public void WriteStructure_ZeroFields() =>
        Assert.Equal(
            new byte[] { 0xB0, 0x00 },
            PackStreamTestHelpers.Encode(w => w.WriteStructure(0, Array.Empty<PackStreamValue?>())));

    [Fact]
    public void WriteStructure_OneField() =>
        Assert.Equal(
            new byte[] { 0xB1, 0x00, 0x2A },
            PackStreamTestHelpers.Encode(w => w.WriteStructure(0, new PackStreamValue?[] { new PackStreamInteger(42) })));

    [Fact]
    public void WriteStructure_FifteenFields()
    {
        var expected = new byte[2 + 15];
        expected[0] = 0xBF;
        expected[1] = 0x7F;
        expected.AsSpan(2).Fill(0x01);

        var fields = new PackStreamValue?[15];
        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = new PackStreamInteger(1);
        }

        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteStructure(0x7F, fields)));
    }

    [Fact]
    public void WriteStructure_SixteenFields_ThrowsFormat()
    {
        var fields = new PackStreamValue?[16];

        Assert.Throws<PackStreamFormatException>(
            () => PackStreamTestHelpers.Encode(w => w.WriteStructure(0, fields)));
    }

    [Fact]
    public void ConstructStructure_SixteenFields_ThrowsArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackStreamStructure(0, new PackStreamValue?[16]));

    [Fact]
    public void WriteStructure_Tag128_ThrowsFormat() =>
        Assert.Throws<PackStreamFormatException>(
            () => PackStreamTestHelpers.Encode(w => w.WriteStructure(128, Array.Empty<PackStreamValue?>())));

    [Fact]
    public void ConstructStructure_Tag128_ThrowsArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackStreamStructure(128, Array.Empty<PackStreamValue?>()));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0x4E)]
    [InlineData(0x7F)]
    public void WriteStructure_BoundaryTags_RoundTrip(byte tag)
    {
        var value = new PackStreamStructure(tag, Array.Empty<PackStreamValue?>());
        var bytes = PackStreamTestHelpers.Encode(w => w.WriteValue(value));

        var decoded = Assert.IsType<PackStreamStructure>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Equal(tag, decoded.Tag);
    }

    [Fact]
    public void ReadStructure_TwoFields_PreservesTagAndFieldOrder()
    {
        var bytes = new byte[] { 0xB2, 0x05, 0x01, 0xC3 };

        var value = Assert.IsType<PackStreamStructure>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Equal(5, value.Tag);
        Assert.Equal(2, value.Fields.Count);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(value.Fields[0]).Value);
        Assert.True(Assert.IsType<PackStreamBoolean>(value.Fields[1]).Value);
    }

    [Fact]
    public void ReadStructure_NestedStructure()
    {
        // <tag 1>[ <tag 2>[] ]
        var bytes = new byte[] { 0xB1, 0x01, 0xB0, 0x02 };

        var value = Assert.IsType<PackStreamStructure>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Equal(1, value.Tag);
        var inner = Assert.IsType<PackStreamStructure>(value.Fields[0]);
        Assert.Equal(2, inner.Tag);
        Assert.Empty(inner.Fields);
    }

    [Fact]
    public void ReadStructure_InvalidTag_ThrowsFormat()
    {
        // Tag byte 0x80 is outside 0-127.
        var bytes = new byte[] { 0xB0, 0x80 };

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Theory]
    [InlineData(new byte[] { 0xB1 })]             // missing tag byte
    [InlineData(new byte[] { 0xB1, 0x01 })]       // missing field
    [InlineData(new byte[] { 0xBF, 0x01, 0x01 })] // declares 15 fields, 1 present
    public void ReadStructure_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadValue());

    [Theory]
    [InlineData(new byte[] { 0xC4 })] // reserved
    [InlineData(new byte[] { 0xC7 })] // reserved
    [InlineData(new byte[] { 0xCF })] // reserved
    [InlineData(new byte[] { 0xD3 })] // reserved
    [InlineData(new byte[] { 0xD7 })] // reserved
    [InlineData(new byte[] { 0xE0 })] // UUID marker is PackStream 1.1 only
    public void ReadValue_UnsupportedMarkers_ThrowFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
}
