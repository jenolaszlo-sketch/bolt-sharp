using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte and validation tests for PackStream 1.0 strings.
/// Expected bytes follow the PackStream specification
/// (https://neo4j.com/docs/bolt/current/packstream/#data-type-string), independently of
/// the codec under test.
/// </summary>
public sealed class StringGoldenTests
{
    [Theory]
    [InlineData("", new byte[] { 0x80 })]
    [InlineData("A", new byte[] { 0x81, 0x41 })]
    [InlineData("ABC", new byte[] { 0x83, 0x41, 0x42, 0x43 })]
    public void WriteString_SmallStrings(string value, byte[] expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteString(value)));

    [Fact]
    public void WriteString_OfficialUtf8Example()
    {
        var expected = new byte[]
        {
            0xD0, 0x12,
            0x47, 0x72, 0xC3, 0xB6, 0xC3, 0x9F, 0x65, 0x6E, 0x6D, 0x61,
            0xC3, 0x9F, 0x73, 0x74, 0xC3, 0xA4, 0x62, 0x65,
        };

        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteString("Gr\u00F6\u00DFenma\u00DFst\u00E4be")));
    }

    [Fact]
    public void WriteString_MultibyteCrossingTinyBoundary_UsesString8()
    {
        // Four supplementary characters = 16 UTF-8 bytes, so TINY_STRING (max 15) is not used.
        var expected = new byte[]
        {
            0xD0, 0x10,
            0xF0, 0x9F, 0x98, 0x80,
            0xF0, 0x9F, 0x98, 0x80,
            0xF0, 0x9F, 0x98, 0x80,
            0xF0, 0x9F, 0x98, 0x80,
        };

        Assert.Equal(
            expected,
            PackStreamTestHelpers.Encode(w => w.WriteString("\U0001F600\U0001F600\U0001F600\U0001F600")));
    }

    [Fact]
    public void WriteString_FifteenMultibyteBytes_UsesTinyString()
    {
        // Seven 2-byte characters plus one ASCII byte = 15 UTF-8 bytes.
        var expected = new byte[]
        {
            0x8F,
            0xC3, 0xA9, 0xC3, 0xA9, 0xC3, 0xA9, 0xC3, 0xA9,
            0xC3, 0xA9, 0xC3, 0xA9, 0xC3, 0xA9,
            0x61,
        };

        Assert.Equal(
            expected,
            PackStreamTestHelpers.Encode(w => w.WriteString("\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9\u00E9a")));
    }

    [Theory]
    [InlineData(15, new byte[] { 0x8F })]
    [InlineData(16, new byte[] { 0xD0, 0x10 })]
    [InlineData(255, new byte[] { 0xD0, 0xFF })]
    [InlineData(256, new byte[] { 0xD1, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xD1, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xD2, 0x00, 0x01, 0x00, 0x00 })]
    public void WriteString_AsciiLength_UsesExpectedPrefix(int length, byte[] prefix)
    {
        var actual = PackStreamTestHelpers.Encode(w => w.WriteString(new string('A', length)));

        var expected = new byte[prefix.Length + length];
        prefix.CopyTo(expected, 0);
        expected.AsSpan(prefix.Length).Fill(0x41);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(new byte[] { 0x80 }, "")]
    [InlineData(new byte[] { 0x81, 0x41 }, "A")]
    [InlineData(new byte[] { 0x83, 0x41, 0x42, 0x43 }, "ABC")]
    [InlineData(new byte[] { 0x84, 0xF0, 0x9F, 0x98, 0x80 }, "\U0001F600")]
    // Wider-than-necessary representations of "A" must be accepted.
    [InlineData(new byte[] { 0xD0, 0x01, 0x41 }, "A")]
    [InlineData(new byte[] { 0xD1, 0x00, 0x01, 0x41 }, "A")]
    [InlineData(new byte[] { 0xD2, 0x00, 0x00, 0x00, 0x01, 0x41 }, "A")]
    public void ReadString_Valid(byte[] bytes, string expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Decode(bytes, r => r.ReadString()));

    [Fact]
    public void ReadString_OfficialUtf8Example()
    {
        var bytes = new byte[]
        {
            0xD0, 0x12,
            0x47, 0x72, 0xC3, 0xB6, 0xC3, 0x9F, 0x65, 0x6E, 0x6D, 0x61,
            0xC3, 0x9F, 0x73, 0x74, 0xC3, 0xA4, 0x62, 0x65,
        };

        Assert.Equal("Gr\u00F6\u00DFenma\u00DFst\u00E4be", PackStreamTestHelpers.Decode(bytes, r => r.ReadString()));
    }

    [Theory]
    [InlineData(new byte[] { 0x81, 0x80 })]                  // lone continuation byte
    [InlineData(new byte[] { 0x81, 0xFF })]                  // invalid start byte
    [InlineData(new byte[] { 0x82, 0xC3, 0x28 })]            // invalid continuation byte
    [InlineData(new byte[] { 0x82, 0xC0, 0xAF })]            // overlong encoding of '/'
    [InlineData(new byte[] { 0x83, 0xED, 0xA0, 0x80 })]      // UTF-8-encoded surrogate
    public void ReadString_MalformedUtf8_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadString());

    [Theory]
    [InlineData(new byte[] { 0x81 })]                    // declares 1 byte, none present
    [InlineData(new byte[] { 0x83, 0x41, 0x42 })]        // declares 3 bytes, 2 present
    [InlineData(new byte[] { 0xD0 })]                    // missing length byte
    [InlineData(new byte[] { 0xD0, 0x01 })]              // missing payload byte
    [InlineData(new byte[] { 0xD1, 0x00 })]              // missing length bytes
    [InlineData(new byte[] { 0xD2, 0x00, 0x00, 0x00 })]  // missing length bytes
    public void ReadString_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadString());

    [Theory]
    [InlineData(new byte[] { 0xD2, 0x01, 0x00, 0x00, 0x01 })] // 16 MiB + 1
    [InlineData(new byte[] { 0xD2, 0x7F, 0xFF, 0xFF, 0xFF })] // int.MaxValue
    public void ReadString_OversizedDeclaredLength_ThrowsFormatBeforeAllocation(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadString());

    [Theory]
    [InlineData(new byte[] { 0xC0 })]             // Null
    [InlineData(new byte[] { 0xC3 })]             // Boolean
    [InlineData(new byte[] { 0xD3 })]             // not a string marker
    [InlineData(new byte[] { 0x90 })]             // tiny list
    [InlineData(new byte[] { 0xCC, 0x00 })]       // bytes
    public void ReadString_WrongMarker_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadString());

    [Fact]
    public void WriteString_LoneHighSurrogate_ThrowsFormat()
    {
        // Built in the method body: xUnit mangles lone surrogates used as attribute arguments.
        var value = "\uD800";

        Assert.Throws<PackStreamFormatException>(() => PackStreamTestHelpers.Encode(w => w.WriteString(value)));
    }

    [Fact]
    public void WriteString_LoneLowSurrogate_ThrowsFormat()
    {
        var value = "\uDC00";

        Assert.Throws<PackStreamFormatException>(() => PackStreamTestHelpers.Encode(w => w.WriteString(value)));
    }

    [Fact]
    public void WriteString_EmbeddedLoneHighSurrogate_ThrowsFormat()
    {
        var value = "a\uD800b";

        Assert.Throws<PackStreamFormatException>(() => PackStreamTestHelpers.Encode(w => w.WriteString(value)));
    }

    [Fact]
    public void WriteString_Null_ThrowsArgumentNull() =>
        Assert.Throws<ArgumentNullException>(() => PackStreamTestHelpers.Encode(w => w.WriteString(null!)));

    [Fact]
    public void ReadString_LeavesTrailingBytesUntouched()
    {
        var reader = new PackStreamReader(new byte[] { 0x81, 0x41, 0x2A });

        Assert.Equal("A", reader.ReadString());
        Assert.Equal(2, reader.Position);
        Assert.Equal(42L, reader.ReadInteger());
        Assert.False(reader.HasMore);
    }
}
