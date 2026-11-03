using System.Buffers;

using BoltSharp.PackStream;

using Xunit;

using BoltSharp.Tests.PackStream;

namespace BoltSharp.Tests.Remediation;

/// <summary>
/// B1-R1 remediation regressions for finding F4 (MINOR) of
/// docs/reviews/B1-Q1-packstream-qualification.md: generic structures could carry reserved
/// graph tags with field counts no reader accepts, producing dead bytes. The writer now
/// requires the exact arity for tags 4E (Node, 4), 52 (Relationship, 8), 72
/// (UnboundRelationship, 4) and 50 (Path, 3), while unknown tags remain writable with any
/// field count.
/// </summary>
public sealed class B1R1F4ReservedGraphTagTests
{
    // ---- invalid arities are rejected before any byte is emitted -----------------

    [Theory]
    [InlineData(0x4E, 0)]
    [InlineData(0x4E, 3)]
    [InlineData(0x4E, 5)]
    [InlineData(0x4E, 15)]
    [InlineData(0x52, 0)]
    [InlineData(0x52, 7)]
    [InlineData(0x52, 9)]
    [InlineData(0x52, 15)]
    [InlineData(0x72, 0)]
    [InlineData(0x72, 3)]
    [InlineData(0x72, 5)]
    [InlineData(0x72, 15)]
    [InlineData(0x50, 0)]
    [InlineData(0x50, 2)]
    [InlineData(0x50, 4)]
    [InlineData(0x50, 15)]
    public void WriteStructure_ReservedGraphTagWithInvalidArity_IsRejected(int tag, int fieldCount)
    {
        var buffer = new ArrayBufferWriter<byte>();

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamWriter(buffer).WriteStructure((byte)tag, IntegerFields(fieldCount)));

        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- the correct arities remain writable (established behaviour) --------------

    [Theory]
    [InlineData(0x4E, 4, 0xB4)]
    [InlineData(0x52, 8, 0xB8)]
    [InlineData(0x72, 4, 0xB4)]
    [InlineData(0x50, 3, 0xB3)]
    public void WriteStructure_ReservedGraphTagWithValidArity_RemainsWritable(int tag, int fieldCount, byte expectedMarker)
    {
        var bytes = PackStreamTestHelpers.Encode(w => w.WriteStructure((byte)tag, IntegerFields(fieldCount)));

        Assert.Equal(expectedMarker, bytes[0]);
        Assert.Equal((byte)tag, bytes[1]);
    }

    [Fact]
    public void WriteStructure_NodeTagWithValidArity_DecodesAsNode()
    {
        var bytes = PackStreamTestHelpers.Encode(w => w.WriteStructure(0x4E, new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamList(Array.Empty<PackStreamValue?>()),
            EmptyMap(),
            new PackStreamString("n:1"),
        }));

        var node = Assert.IsType<PackStreamNode>(new PackStreamReader(bytes).ReadValue());
        Assert.Equal("n:1", node.ElementId);
    }

    [Fact]
    public void WriteValue_StructureWithReservedTagAndInvalidArity_IsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var structure = new PackStreamStructure(0x50, new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteValue(structure));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- unknown generic tags are unaffected (future protocol extension) ----------

    [Theory]
    [InlineData(0x49, 2)]  // temporal-style tag, as exercised by reviewer Probe15
    [InlineData(0x00, 0)]
    [InlineData(0x7F, 15)]
    [InlineData(0x42, 1)]
    public void WriteStructure_UnknownTags_AcceptAnyFieldCount(int tag, int fieldCount)
    {
        var bytes = PackStreamTestHelpers.Encode(w => w.WriteStructure((byte)tag, IntegerFields(fieldCount)));

        Assert.Equal((byte)(0xB0 + fieldCount), bytes[0]);
        Assert.Equal((byte)tag, bytes[1]);

        var structure = Assert.IsType<PackStreamStructure>(new PackStreamReader(bytes).ReadValue());
        Assert.Equal((byte)tag, structure.Tag);
        Assert.Equal(fieldCount, structure.Fields.Count);
    }

    // ---- helpers ---------------------------------------------------------------

    private static PackStreamValue?[] IntegerFields(int count)
    {
        var fields = new PackStreamValue?[count];
        for (var i = 0; i < count; i++)
        {
            fields[i] = new PackStreamInteger(1);
        }

        return fields;
    }

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());
}
