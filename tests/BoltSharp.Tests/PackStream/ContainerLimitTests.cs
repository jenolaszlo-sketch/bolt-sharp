using System.Buffers;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Resource-limit, depth, budget and reader-position behaviour for recursive containers.
/// </summary>
public sealed class ContainerLimitTests
{
    [Fact]
    public void DefaultLimits_ContainersHaveDocumentedDefaults()
    {
        Assert.Equal(100, PackStreamLimits.Default.MaxDepth);
        Assert.Equal(100_000, PackStreamLimits.Default.MaxListItems);
        Assert.Equal(100_000, PackStreamLimits.Default.MaxMapEntries);
        Assert.Equal(1_000_000, PackStreamLimits.Default.MaxTotalElements);
    }

    [Fact]
    public void NegativeContainerLimits_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackStreamLimits(maxDepth: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackStreamLimits(maxListItems: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackStreamLimits(maxMapEntries: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PackStreamLimits(maxTotalElements: -1));
    }

    [Fact]
    public void ReadValue_NestingAtDefaultDepthLimit_Succeeds()
    {
        var value = Assert.IsType<PackStreamList>(
            PackStreamTestHelpers.Decode(NestedListBytes(100), r => r.ReadValue()));

        Assert.Equal(100, ContainerDepth(value));
    }

    [Fact]
    public void ReadValue_NestingBeyondDefaultDepthLimit_ThrowsFormat() =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(NestedListBytes(101), r => r.ReadValue());

    [Fact]
    public void ReadValue_CustomDepthLimit_AllowsAtLimit()
    {
        var reader = new PackStreamReader(NestedListBytes(2), new PackStreamLimits(maxDepth: 2));

        Assert.IsType<PackStreamList>(reader.ReadValue());
    }

    [Fact]
    public void ReadValue_CustomDepthLimit_RejectsBeyond()
    {
        var reader = new PackStreamReader(NestedListBytes(3), new PackStreamLimits(maxDepth: 2));

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
    }

    [Fact]
    public void ReadValue_ElementBudget_AllowsExactBudget()
    {
        // List + three items = 4 values.
        var reader = new PackStreamReader(
            new byte[] { 0x93, 0x01, 0x02, 0x03 },
            new PackStreamLimits(maxTotalElements: 4));

        Assert.IsType<PackStreamList>(reader.ReadValue());
    }

    [Fact]
    public void ReadValue_ElementBudget_ExhaustionThrows()
    {
        var reader = new PackStreamReader(
            new byte[] { 0x93, 0x01, 0x02, 0x03 },
            new PackStreamLimits(maxTotalElements: 3));

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
    }

    [Fact]
    public void ReadValue_ZeroElementBudget_RejectsScalar()
    {
        var reader = new PackStreamReader(new byte[] { 0x01 }, new PackStreamLimits(maxTotalElements: 0));

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
    }

    [Fact]
    public void ReadList_CustomItemLimit_Rejects()
    {
        var reader = new PackStreamReader(
            new byte[] { 0xD4, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01 },
            new PackStreamLimits(maxListItems: 4));

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
    }

    [Fact]
    public void ReadMap_CustomEntryLimit_Rejects()
    {
        var reader = new PackStreamReader(
            new byte[] { 0xD8, 0x05, 0x81, 0x61, 0x01, 0x81, 0x62, 0x01, 0x81, 0x63, 0x01, 0x81, 0x64, 0x01, 0x81, 0x65, 0x01 },
            new PackStreamLimits(maxMapEntries: 4));

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
    }

    [Fact]
    public void WriteList_CustomItemLimit_Rejects()
    {
        var items = new PackStreamValue?[5];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new PackStreamInteger(1);
        }

        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), new PackStreamLimits(maxListItems: 4));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(items));
    }

    [Fact]
    public void WriteMap_CustomEntryLimit_Rejects()
    {
        var entries = new KeyValuePair<string, PackStreamValue?>[5];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new KeyValuePair<string, PackStreamValue?>("k" + i, new PackStreamInteger(1));
        }

        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), new PackStreamLimits(maxMapEntries: 4));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteMap(entries));
    }

    [Fact]
    public void WriteValue_CustomDepthLimit_RejectsBeyond()
    {
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), new PackStreamLimits(maxDepth: 2));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteValue(NestedValue(3)));
    }

    [Fact]
    public void WriteValue_CustomDepthLimit_AllowsAtLimit()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxDepth: 3));

        writer.WriteValue(NestedValue(3));

        Assert.Equal(4, buffer.WrittenCount); // 91 91 91 01
    }

    [Fact]
    public void WriteValue_Null_WritesNull() =>
        Assert.Equal(new byte[] { 0xC0 }, PackStreamTestHelpers.Encode(w => w.WriteValue(null)));

    [Fact]
    public void ReadValue_Null_ReturnsNull() =>
        Assert.Null(PackStreamTestHelpers.Decode(new byte[] { 0xC0 }, r => r.ReadValue()));

    [Fact]
    public void ReadValue_TruncatedList_PositionReflectsConsumedBytes()
    {
        var reader = new PackStreamReader(new byte[] { 0x93, 0x01, 0x02 });

        Assert.Throws<PackStreamTruncatedException>(() => reader.ReadValue());
        Assert.Equal(3, reader.Position);
    }

    [Fact]
    public void ReadValue_SequentialValues_TrackPosition()
    {
        var reader = new PackStreamReader(new byte[] { 0x93, 0x01, 0x02, 0x03, 0x2A });

        var list = Assert.IsType<PackStreamList>(reader.ReadValue());
        Assert.Equal(3, list.Items.Count);
        Assert.Equal(4, reader.Position);

        Assert.Equal(42L, reader.ReadInteger());
        Assert.Equal(5, reader.Position);
        Assert.False(reader.HasMore);
    }

    [Fact]
    public void RoundTrip_MixedTree()
    {
        var value = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>(
                "list",
                new PackStreamList(new PackStreamValue?[]
                {
                    new PackStreamInteger(1),
                    new PackStreamString("two"),
                    new PackStreamBoolean(true),
                    new PackStreamStructure(3, new PackStreamValue?[] { null }),
                })),
            new KeyValuePair<string, PackStreamValue?>(
                "bytes", new PackStreamBytes(new byte[] { 0x00, 0xFF })),
        });

        var bytes = PackStreamTestHelpers.Encode(w => w.WriteValue(value));
        var decoded = Assert.IsType<PackStreamMap>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));

        Assert.Equal(2, decoded.Entries.Count);

        var list = Assert.IsType<PackStreamList>(decoded.Entries[0].Value);
        Assert.Equal(4, list.Items.Count);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(list.Items[0]).Value);
        Assert.Equal("two", Assert.IsType<PackStreamString>(list.Items[1]).Value);

        var structure = Assert.IsType<PackStreamStructure>(list.Items[3]);
        Assert.Equal(3, structure.Tag);
        Assert.Null(structure.Fields[0]);

        Assert.Equal(new byte[] { 0x00, 0xFF }, Assert.IsType<PackStreamBytes>(decoded.Entries[1].Value).Value.ToArray());
    }

    private static byte[] NestedListBytes(int depth)
    {
        var bytes = new byte[depth + 1];
        for (var i = 0; i < depth; i++)
        {
            bytes[i] = 0x91; // TINY_LIST with one item
        }

        bytes[^1] = 0x01;
        return bytes;
    }

    private static PackStreamValue NestedValue(int depth)
    {
        PackStreamValue value = new PackStreamInteger(1);
        for (var i = 0; i < depth; i++)
        {
            value = new PackStreamList(new PackStreamValue?[] { value });
        }

        return value;
    }

    private static int ContainerDepth(PackStreamList list)
    {
        var depth = 1;
        var current = list;
        while (current.Items.Count == 1 && current.Items[0] is PackStreamList next)
        {
            current = next;
            depth++;
        }

        return depth;
    }
}
