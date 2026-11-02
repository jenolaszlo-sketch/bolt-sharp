using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte and boundary tests for PackStream 1.0 lists.
/// Expected bytes follow the PackStream specification
/// (https://neo4j.com/docs/bolt/current/packstream/#data-type-list), independently of
/// the codec under test.
/// </summary>
public sealed class ListGoldenTests
{
    [Fact]
    public void WriteList_Empty() =>
        Assert.Equal(
            new byte[] { 0x90 },
            PackStreamTestHelpers.Encode(w => w.WriteList(Array.Empty<PackStreamValue?>())));

    [Fact]
    public void WriteList_ThreeIntegers() =>
        Assert.Equal(
            new byte[] { 0x93, 0x01, 0x02, 0x03 },
            PackStreamTestHelpers.Encode(w => w.WriteList(new PackStreamValue?[] { Int(1), Int(2), Int(3) })));

    [Fact]
    public void WriteList_MixedScalarTypes() =>
        Assert.Equal(
            new byte[]
            {
                0x94,
                0x01,
                0x83, 0x74, 0x77, 0x6F,
                0xC3,
                0xC1, 0x3F, 0xF8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            PackStreamTestHelpers.Encode(w => w.WriteList(
                new PackStreamValue?[] { Int(1), Str("two"), Bool(true), new PackStreamFloat(1.5) })));

    [Fact]
    public void WriteList_NestedLists() =>
        Assert.Equal(
            new byte[] { 0x92, 0x91, 0x01, 0x91, 0x02 },
            PackStreamTestHelpers.Encode(w => w.WriteList(
                new PackStreamValue?[]
                {
                    new PackStreamList(new PackStreamValue?[] { Int(1) }),
                    new PackStreamList(new PackStreamValue?[] { Int(2) }),
                })));

    [Fact]
    public void WriteList_MapAndStructure() =>
        Assert.Equal(
            new byte[] { 0x92, 0xA1, 0x81, 0x6B, 0x01, 0xB1, 0x07, 0xC3 },
            PackStreamTestHelpers.Encode(w => w.WriteList(
                new PackStreamValue?[]
                {
                    new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("k", Int(1)) }),
                    new PackStreamStructure(7, new PackStreamValue?[] { Bool(true) }),
                })));

    [Theory]
    [InlineData(15, new byte[] { 0x9F })]
    [InlineData(16, new byte[] { 0xD4, 0x10 })]
    [InlineData(255, new byte[] { 0xD4, 0xFF })]
    [InlineData(256, new byte[] { 0xD5, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xD5, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xD6, 0x00, 0x01, 0x00, 0x00 })]
    public void WriteList_BoundaryCounts(int count, byte[] prefix) =>
        Assert.Equal(
            ListOfOnes(count, prefix),
            PackStreamTestHelpers.Encode(w => w.WriteList(Ones(count))));

    [Fact]
    public void ReadList_Empty()
    {
        var value = Assert.IsType<PackStreamList>(
            PackStreamTestHelpers.Decode(new byte[] { 0x90 }, r => r.ReadValue()));

        Assert.Empty(value.Items);
    }

    [Fact]
    public void ReadList_ThreeIntegers()
    {
        var value = Assert.IsType<PackStreamList>(
            PackStreamTestHelpers.Decode(new byte[] { 0x93, 0x01, 0x02, 0x03 }, r => r.ReadValue()));

        Assert.Equal(3, value.Items.Count);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(value.Items[0]).Value);
        Assert.Equal(2, Assert.IsType<PackStreamInteger>(value.Items[1]).Value);
        Assert.Equal(3, Assert.IsType<PackStreamInteger>(value.Items[2]).Value);
    }

    [Fact]
    public void ReadList_MixedNestedContainers()
    {
        // [ {"k": 1}, <tag 7> [true] ]
        var bytes = new byte[] { 0x92, 0xA1, 0x81, 0x6B, 0x01, 0xB1, 0x07, 0xC3 };

        var value = Assert.IsType<PackStreamList>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        var map = Assert.IsType<PackStreamMap>(value.Items[0]);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(map.Entries[0].Value).Value);

        var structure = Assert.IsType<PackStreamStructure>(value.Items[1]);
        Assert.Equal(7, structure.Tag);
        Assert.True(Assert.IsType<PackStreamBoolean>(structure.Fields[0]).Value);
    }

    [Theory]
    [InlineData(new byte[] { 0x90 })]
    [InlineData(new byte[] { 0xD4, 0x00 })]
    [InlineData(new byte[] { 0xD5, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xD6, 0x00, 0x00, 0x00, 0x00 })]
    public void ReadList_AcceptsWiderEmptyRepresentations(byte[] bytes)
    {
        var value = Assert.IsType<PackStreamList>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Empty(value.Items);
    }

    [Fact]
    public void ReadList_AcceptsNonminimalLengthEncoding()
    {
        var value = Assert.IsType<PackStreamList>(
            PackStreamTestHelpers.Decode(new byte[] { 0xD4, 0x02, 0x01, 0x02 }, r => r.ReadValue()));

        Assert.Equal(2, value.Items.Count);
    }

    [Theory]
    [InlineData(15, new byte[] { 0x9F })]
    [InlineData(16, new byte[] { 0xD4, 0x10 })]
    [InlineData(255, new byte[] { 0xD4, 0xFF })]
    [InlineData(256, new byte[] { 0xD5, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xD5, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xD6, 0x00, 0x01, 0x00, 0x00 })]
    public void ReadList_BoundaryCounts(int count, byte[] prefix)
    {
        var value = Assert.IsType<PackStreamList>(
            PackStreamTestHelpers.Decode(ListOfOnes(count, prefix), r => r.ReadValue()));

        Assert.Equal(count, value.Items.Count);
    }

    [Theory]
    [InlineData(new byte[] { 0x91 })]                    // declares 1 item, none present
    [InlineData(new byte[] { 0x93, 0x01, 0x02 })]        // declares 3 items, 2 present
    [InlineData(new byte[] { 0xD4 })]                    // missing count byte
    [InlineData(new byte[] { 0xD4, 0x02, 0x01 })]        // missing second item
    [InlineData(new byte[] { 0xD5, 0x00 })]              // missing count bytes
    [InlineData(new byte[] { 0xD6, 0x00, 0x00, 0x05, 0x01 })] // declares 5, 1 present
    public void ReadList_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadValue());

    [Theory]
    [InlineData(new byte[] { 0xD6, 0x00, 0x01, 0x86, 0xA1 })] // 100,001 (> default 100,000)
    [InlineData(new byte[] { 0xD6, 0x7F, 0xFF, 0xFF, 0xFF })] // int.MaxValue
    [InlineData(new byte[] { 0xD6, 0xFF, 0xFF, 0xFF, 0xFF })] // uint.MaxValue
    public void ReadList_ExcessiveDeclaredCount_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());

    [Fact]
    public void ReadList_DeclaredCountEqualToLimitButMissing_ThrowsTruncatedNotLimit()
    {
        // 100,000 is exactly the default MaxListItems, so the count is accepted and the
        // failure is a deterministic truncation (and no large collection is pre-allocated).
        var bytes = new byte[] { 0xD6, 0x00, 0x01, 0x86, 0xA0 };

        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadValue());
    }

    private static PackStreamInteger Int(long value) => new(value);

    private static PackStreamString Str(string value) => new(value);

    private static PackStreamBoolean Bool(bool value) => new(value);

    private static PackStreamValue?[] Ones(int count)
    {
        var items = new PackStreamValue?[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = new PackStreamInteger(1);
        }

        return items;
    }

    private static byte[] ListOfOnes(int count, byte[] prefix)
    {
        var result = new byte[prefix.Length + count];
        prefix.CopyTo(result, 0);
        result.AsSpan(prefix.Length).Fill(0x01);
        return result;
    }
}
