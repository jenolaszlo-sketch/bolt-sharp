using System.Globalization;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte and boundary tests for PackStream 1.0 maps.
/// Expected bytes follow the PackStream specification
/// (https://neo4j.com/docs/bolt/current/packstream/#data-type-dictionary), independently of
/// the codec under test.
/// </summary>
public sealed class MapGoldenTests
{
    [Fact]
    public void WriteMap_Empty() =>
        Assert.Equal(
            new byte[] { 0xA0 },
            PackStreamTestHelpers.Encode(w => w.WriteMap(
                Array.Empty<KeyValuePair<string, PackStreamValue?>>())));

    [Fact]
    public void WriteMap_OneEntry() =>
        Assert.Equal(
            new byte[] { 0xA1, 0x83, 0x6F, 0x6E, 0x65, 0x01 },
            PackStreamTestHelpers.Encode(w => w.WriteMap(
                new[] { new KeyValuePair<string, PackStreamValue?>("one", new PackStreamInteger(1)) })));

    [Fact]
    public void WriteMap_NestedMapValue() =>
        Assert.Equal(
            new byte[] { 0xA1, 0x81, 0x6D, 0xA0 },
            PackStreamTestHelpers.Encode(w => w.WriteMap(
                new[]
                {
                    new KeyValuePair<string, PackStreamValue?>(
                        "m", new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>())),
                })));

    [Theory]
    [InlineData(15, new byte[] { 0xAF })]
    [InlineData(16, new byte[] { 0xD8, 0x10 })]
    [InlineData(255, new byte[] { 0xD8, 0xFF })]
    [InlineData(256, new byte[] { 0xD9, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xD9, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xDA, 0x00, 0x01, 0x00, 0x00 })]
    public void WriteMap_BoundaryCounts(int count, byte[] prefix) =>
        Assert.Equal(
            MapBytes(count, prefix),
            PackStreamTestHelpers.Encode(w => w.WriteMap(MapEntries(count))));

    [Fact]
    public void ReadMap_Empty()
    {
        var value = Assert.IsType<PackStreamMap>(
            PackStreamTestHelpers.Decode(new byte[] { 0xA0 }, r => r.ReadValue()));

        Assert.Empty(value.Entries);
    }

    [Fact]
    public void ReadMap_OneEntry()
    {
        var value = Assert.IsType<PackStreamMap>(
            PackStreamTestHelpers.Decode(new byte[] { 0xA1, 0x83, 0x6F, 0x6E, 0x65, 0x01 }, r => r.ReadValue()));

        Assert.Equal("one", value.Entries[0].Key);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(value.Entries[0].Value).Value);
    }

    [Fact]
    public void ReadMap_DuplicateKeys_LastValueWins()
    {
        // {"a": 1, "a": 2} must collapse to a single entry with value 2.
        var bytes = new byte[] { 0xA2, 0x81, 0x61, 0x01, 0x81, 0x61, 0x02 };

        var value = Assert.IsType<PackStreamMap>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        var entry = Assert.Single(value.Entries);
        Assert.Equal("a", entry.Key);
        Assert.Equal(2, Assert.IsType<PackStreamInteger>(entry.Value).Value);
    }

    [Fact]
    public void ReadMap_DuplicateKeys_KeepsFirstSeenPosition()
    {
        // {"a": 1, "b": 2, "a": 3} -> a=3 then b=2 (first-seen order).
        var bytes = new byte[] { 0xA3, 0x81, 0x61, 0x01, 0x81, 0x62, 0x02, 0x81, 0x61, 0x03 };

        var value = Assert.IsType<PackStreamMap>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Equal(2, value.Entries.Count);
        Assert.Equal("a", value.Entries[0].Key);
        Assert.Equal(3, Assert.IsType<PackStreamInteger>(value.Entries[0].Value).Value);
        Assert.Equal("b", value.Entries[1].Key);
        Assert.Equal(2, Assert.IsType<PackStreamInteger>(value.Entries[1].Value).Value);
    }

    [Fact]
    public void ReadMap_NonStringKey_ThrowsFormat()
    {
        // {"key" position holds an integer key.
        var bytes = new byte[] { 0xA1, 0x01, 0x02 };

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadMap_MixedNestedContainers()
    {
        // {"list": [1, {"k": true}], "s": "x"}
        var bytes = new byte[]
        {
            0xA2,
            0x84, 0x6C, 0x69, 0x73, 0x74,
            0x92, 0x01, 0xA1, 0x81, 0x6B, 0xC3,
            0x81, 0x73,
            0x81, 0x78,
        };

        var value = Assert.IsType<PackStreamMap>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Equal(2, value.Entries.Count);

        var list = Assert.IsType<PackStreamList>(value.Entries[0].Value);
        Assert.Equal(2, list.Items.Count);
        var innerMap = Assert.IsType<PackStreamMap>(list.Items[1]);
        Assert.True(Assert.IsType<PackStreamBoolean>(innerMap.Entries[0].Value).Value);

        Assert.Equal("x", Assert.IsType<PackStreamString>(value.Entries[1].Value).Value);
    }

    [Theory]
    [InlineData(new byte[] { 0xA0 })]
    [InlineData(new byte[] { 0xD8, 0x00 })]
    [InlineData(new byte[] { 0xD9, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xDA, 0x00, 0x00, 0x00, 0x00 })]
    public void ReadMap_AcceptsWiderEmptyRepresentations(byte[] bytes)
    {
        var value = Assert.IsType<PackStreamMap>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Empty(value.Entries);
    }

    [Theory]
    [InlineData(15, new byte[] { 0xAF })]
    [InlineData(16, new byte[] { 0xD8, 0x10 })]
    [InlineData(255, new byte[] { 0xD8, 0xFF })]
    [InlineData(256, new byte[] { 0xD9, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0xD9, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0xDA, 0x00, 0x01, 0x00, 0x00 })]
    public void ReadMap_BoundaryCounts(int count, byte[] prefix)
    {
        var value = Assert.IsType<PackStreamMap>(
            PackStreamTestHelpers.Decode(MapBytes(count, prefix), r => r.ReadValue()));

        Assert.Equal(count, value.Entries.Count);
        Assert.Equal("000000", value.Entries[0].Key);
        Assert.Equal((count - 1).ToString("D6", CultureInfo.InvariantCulture), value.Entries[count - 1].Key);
    }

    [Theory]
    [InlineData(new byte[] { 0xA1 })]                          // missing key
    [InlineData(new byte[] { 0xA1, 0x81, 0x61 })]              // missing value
    [InlineData(new byte[] { 0xA2, 0x81, 0x61, 0x01, 0x81 })]  // truncated second key
    [InlineData(new byte[] { 0xD8 })]                          // missing count byte
    [InlineData(new byte[] { 0xD9, 0x00 })]                    // missing count bytes
    public void ReadMap_Truncated_Throws(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadValue());

    [Theory]
    [InlineData(new byte[] { 0xDA, 0x00, 0x01, 0x86, 0xA1 })] // 100,001 (> default 100,000)
    [InlineData(new byte[] { 0xDA, 0x7F, 0xFF, 0xFF, 0xFF })] // int.MaxValue
    [InlineData(new byte[] { 0xDA, 0xFF, 0xFF, 0xFF, 0xFF })] // uint.MaxValue
    public void ReadMap_ExcessiveDeclaredCount_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());

    private static KeyValuePair<string, PackStreamValue?>[] MapEntries(int count)
    {
        var entries = new KeyValuePair<string, PackStreamValue?>[count];
        for (var i = 0; i < count; i++)
        {
            entries[i] = new KeyValuePair<string, PackStreamValue?>(
                i.ToString("D6", CultureInfo.InvariantCulture),
                new PackStreamInteger(1));
        }

        return entries;
    }

    private static byte[] MapBytes(int count, byte[] prefix)
    {
        const int entrySize = 1 + 6 + 1; // 0x86 + six ASCII digits + 0x01

        var result = new byte[prefix.Length + (entrySize * count)];
        prefix.CopyTo(result, 0);

        var offset = prefix.Length;
        for (var i = 0; i < count; i++)
        {
            result[offset++] = 0x86;
            var key = i.ToString("D6", CultureInfo.InvariantCulture);
            for (var c = 0; c < 6; c++)
            {
                result[offset++] = (byte)key[c];
            }

            result[offset++] = 0x01;
        }

        return result;
    }
}
