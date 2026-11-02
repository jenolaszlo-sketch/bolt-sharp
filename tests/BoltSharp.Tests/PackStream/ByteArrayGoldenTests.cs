using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte and validation tests for PackStream 1.0 byte arrays.
/// Expected bytes follow the PackStream specification
/// (https://neo4j.com/docs/bolt/current/packstream/#data-type-bytes), independently of
/// the codec under test.
/// </summary>
public sealed class ByteArrayGoldenTests
{
    [Fact]
    public void WriteBytes_Empty() =>
        Assert.Equal(new byte[] { 0xCC, 0x00 }, PackStreamTestHelpers.Encode(w => w.WriteBytes(ReadOnlySpan<byte>.Empty)));

    [Fact]
    public void WriteBytes_ThreeValues() =>
        Assert.Equal(
            new byte[] { 0xCC, 0x03, 0x01, 0x02, 0x03 },
            PackStreamTestHelpers.Encode(w => w.WriteBytes(new byte[] { 0x01, 0x02, 0x03 })));

    [Fact]
    public void WriteBytes_ArbitraryBinaryValues()
    {
        var value = new byte[] { 0x00, 0xFF, 0x80, 0x00, 0x7F };

        Assert.Equal(
            new byte[] { 0xCC, 0x05, 0x00, 0xFF, 0x80, 0x00, 0x7F },
            PackStreamTestHelpers.Encode(w => w.WriteBytes(value)));
    }

    [Theory]
    [InlineData(1, new byte[] { 0xCC, 0x01 })]
    [InlineData(255, new byte[] { 0xCC, 0xFF })]
    [InlineData(256, new byte[] { 0xCD, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xCD, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xCE, 0x00, 0x01, 0x00, 0x00 })]
    public void WriteBytes_Length_UsesBigEndianPrefix(int length, byte[] prefix)
    {
        var payload = new byte[length];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)i;
        }

        var actual = PackStreamTestHelpers.Encode(w => w.WriteBytes(payload));

        var expected = new byte[prefix.Length + length];
        prefix.CopyTo(expected, 0);
        payload.CopyTo(expected, prefix.Length);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(new byte[] { 0xCC, 0x00 }, new byte[0])]
    [InlineData(new byte[] { 0xCC, 0x03, 0x01, 0x02, 0x03 }, new byte[] { 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0xCC, 0x02, 0x00, 0xFF }, new byte[] { 0x00, 0xFF })]
    // Wider-than-necessary representations of a single byte must be accepted.
    [InlineData(new byte[] { 0xCD, 0x00, 0x01, 0x2A }, new byte[] { 0x2A })]
    [InlineData(new byte[] { 0xCE, 0x00, 0x00, 0x00, 0x01, 0x2A }, new byte[] { 0x2A })]
    public void ReadBytes_Valid(byte[] bytes, byte[] expected) =>
        Assert.Equal(expected, PackStreamTestHelpers.Decode(bytes, r => r.ReadBytes()));

    [Theory]
    [InlineData(new byte[] { 0xCC })]                    // missing length byte
    [InlineData(new byte[] { 0xCC, 0x03, 0x01, 0x02 })]  // declares 3 bytes, 2 present
    [InlineData(new byte[] { 0xCD, 0x00 })]              // missing length byte
    [InlineData(new byte[] { 0xCD, 0x00, 0x01 })]        // missing payload byte
    [InlineData(new byte[] { 0xCE, 0x00, 0x00, 0x00 })]  // missing length byte
    public void ReadBytes_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadBytes());

    [Theory]
    [InlineData(new byte[] { 0xCE, 0x01, 0x00, 0x00, 0x01 })] // 16 MiB + 1
    [InlineData(new byte[] { 0xCE, 0xFF, 0xFF, 0xFF, 0xFF })] // uint.MaxValue
    public void ReadBytes_OversizedDeclaredLength_ThrowsFormatBeforeAllocation(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadBytes());

    [Theory]
    [InlineData(new byte[] { 0xC0 })]                 // Null
    [InlineData(new byte[] { 0x80 })]                 // tiny string
    [InlineData(new byte[] { 0xD0, 0x01, 0x41 })]     // string
    [InlineData(new byte[] { 0xCF })]                 // not a byte-array marker
    public void ReadBytes_WrongMarker_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadBytes());

    [Fact]
    public void ReadBytes_ReturnsDefensiveCopy()
    {
        var input = new byte[] { 0xCC, 0x01, 0x2A };
        var reader = new PackStreamReader(input);

        var result = reader.ReadBytes();
        input[2] = 0xFF;

        Assert.Equal(new byte[] { 0x2A }, result);
    }

    [Fact]
    public void ReadStringThenBytesThenInteger_TracksPosition()
    {
        var bytes = new byte[] { 0x81, 0x41, 0xCC, 0x02, 0x00, 0xFF, 0x2A };
        var reader = new PackStreamReader(bytes);

        Assert.Equal("A", reader.ReadString());
        Assert.Equal(2, reader.Position);

        Assert.Equal(new byte[] { 0x00, 0xFF }, reader.ReadBytes());
        Assert.Equal(6, reader.Position);

        Assert.Equal(42L, reader.ReadInteger());
        Assert.Equal(7, reader.Position);
        Assert.False(reader.HasMore);
    }

    [Fact]
    public void MultipleStringAndByteWrites_AppendInOrder()
    {
        var actual = PackStreamTestHelpers.Encode(w =>
        {
            w.WriteString("A");
            w.WriteBytes(new byte[] { 0x00, 0xFF });
        });

        Assert.Equal(new byte[] { 0x81, 0x41, 0xCC, 0x02, 0x00, 0xFF }, actual);
    }
}
