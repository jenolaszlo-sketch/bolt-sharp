using System.Buffers;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Remediation;

/// <summary>
/// B1-R1 remediation regressions for finding F3 (MODERATE) of
/// docs/reviews/B1-Q1-packstream-qualification.md: the writer emitted partial output when a
/// nested value failed validation. Every public composite write entry point now validates
/// the full value before committing any bytes, so a validation failure leaves the
/// destination <see cref="IBufferWriter{T}"/> exactly as it was.
/// </summary>
public sealed class B1R1F3ExceptionSafetyTests
{
    private const string InvalidUtf16 = "\uD800"; // unpaired high surrogate

    // ---- nested lists -----------------------------------------------------------

    [Fact]
    public void WriteList_InvalidStringNestedDeep_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var list = new PackStreamList(new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamList(new PackStreamValue?[] { new PackStreamString(InvalidUtf16) }),
        });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(list.Items));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteList_ItemCountExceeded_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxListItems: 2));
        var inner = new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2), new PackStreamInteger(3) });
        var outer = new PackStreamList(new PackStreamValue?[] { inner });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(outer.Items));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteList_DepthExceeded_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxDepth: 2));
        var value = new PackStreamList(new PackStreamValue?[]
        {
            new PackStreamList(new PackStreamValue?[] { new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1) }) }),
        });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(value.Items));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- nested maps ------------------------------------------------------------

    [Fact]
    public void WriteMap_InvalidStringInNestedValue_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var map = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>("ok", new PackStreamInteger(1)),
            new KeyValuePair<string, PackStreamValue?>(
                "bad",
                new PackStreamList(new PackStreamValue?[] { new PackStreamString(InvalidUtf16) })),
        });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteMap(map.Entries));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteMap_NullKey_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var entries = new KeyValuePair<string, PackStreamValue?>[]
        {
            new("ok", new PackStreamInteger(1)),
            new(null!, new PackStreamInteger(2)),
        };

        Assert.Throws<PackStreamFormatException>(() => writer.WriteMap(entries));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- generic structures -----------------------------------------------------

    [Fact]
    public void WriteStructure_ReservedTagInvalidArity_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        Assert.Throws<PackStreamFormatException>(
            () => writer.WriteStructure(0x4E, new PackStreamValue?[] { new PackStreamInteger(1) }));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteStructure_InvalidStringInNestedField_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var structure = new PackStreamStructure(0x42, new PackStreamValue?[]
        {
            new PackStreamList(new PackStreamValue?[] { new PackStreamString(InvalidUtf16) }),
        });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteValue(structure));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- graph entities ---------------------------------------------------------

    [Fact]
    public void WriteNode_InvalidLabel_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var node = new PackStreamNode(1, new[] { InvalidUtf16 }, EmptyMap(), "n:1");

        Assert.Throws<PackStreamFormatException>(() => writer.WriteNode(node));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteNode_InvalidPropertyString_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var properties = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>("note", new PackStreamString(InvalidUtf16)),
        });
        var node = new PackStreamNode(1, new[] { "Person" }, properties, "n:1");

        Assert.Throws<PackStreamFormatException>(() => writer.WriteNode(node));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteRelationship_InvalidPropertyString_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var properties = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>("note", new PackStreamString(InvalidUtf16)),
        });
        var relationship = new PackStreamRelationship(1, 1, 2, "KNOWS", properties, "r:1", "n:1", "n:2");

        Assert.Throws<PackStreamFormatException>(() => writer.WriteRelationship(relationship));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteUnboundRelationship_InvalidType_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var relationship = new PackStreamUnboundRelationship(1, InvalidUtf16, EmptyMap(), "r:1");

        Assert.Throws<PackStreamFormatException>(() => writer.WriteUnboundRelationship(relationship));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- paths ------------------------------------------------------------------

    [Fact]
    public void WritePath_OversizedFieldList_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxListItems: 4));
        var path = new PackStreamPath(Nodes(5), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WritePath_InvalidStringInsideNodeProperty_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var properties = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>("note", new PackStreamString(InvalidUtf16)),
        });
        var path = new PackStreamPath(
            new[] { new PackStreamNode(1, Array.Empty<string>(), properties, "n:1") },
            Array.Empty<PackStreamUnboundRelationship>(),
            Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- size-limit violations nested in containers ------------------------------

    [Fact]
    public void WriteList_StringExceedingConfiguredLength_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxStringByteLength: 4));
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamString("too long") });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(list.Items));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteList_BytesExceedingConfiguredLength_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxBytesLength: 2));
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamBytes(new byte[] { 1, 2, 3 }) });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(list.Items));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteValue_MixedTree_InvalidDeepValue_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var tree = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>("node", new PackStreamNode(
                1,
                new[] { "Person" },
                new PackStreamMap(new[]
                {
                    new KeyValuePair<string, PackStreamValue?>(
                        "nested",
                        new PackStreamList(new PackStreamValue?[] { new PackStreamString(InvalidUtf16) })),
                }),
                "n:1")),
        });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteValue(tree));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- top-level scalar validation is also atomic ------------------------------

    [Fact]
    public void WriteString_InvalidUtf16_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        Assert.Throws<PackStreamFormatException>(() => writer.WriteString(InvalidUtf16));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteBytes_ExceedingConfiguredLength_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, new PackStreamLimits(maxBytesLength: 2));

        Assert.Throws<PackStreamFormatException>(() => writer.WriteBytes(new byte[] { 1, 2, 3 }));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- destination state after a failed write ----------------------------------

    [Fact]
    public void FailedWrite_LeavesDestinationEmpty_SubsequentValidWriteStartsAtZero()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        Assert.Throws<PackStreamFormatException>(
            () => writer.WriteList(new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamString(InvalidUtf16) }));
        Assert.Equal(0, buffer.WrittenCount);

        writer.WriteInteger(7);
        Assert.Equal(new byte[] { 0x07 }, buffer.WrittenSpan.ToArray());
    }

    // ---- valid writes still match existing golden bytes ---------------------------

    [Fact]
    public void ValidWrites_StillMatchGoldenBytes()
    {
        // A preflight pass must not change canonical output.
        Assert.Equal(
            new byte[] { 0x93, 0x01, 0x02, 0x03 },
            Encode(w => w.WriteList(new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2), new PackStreamInteger(3) })));

        Assert.Equal(
            new byte[] { 0xA2, 0x81, 0x61, 0x01, 0x81, 0x62, 0x02 },
            Encode(w => w.WriteMap(new[]
            {
                new KeyValuePair<string, PackStreamValue?>("a", new PackStreamInteger(1)),
                new KeyValuePair<string, PackStreamValue?>("b", new PackStreamInteger(2)),
            })));

        Assert.Equal(
            new byte[] { 0xB4, 0x4E, 0x01, 0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E, 0xA0, 0x83, 0x6E, 0x3A, 0x31 },
            Encode(w => w.WriteNode(new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1"))));
    }

    // ---- the guarantee covers value validation, not destination failures ----------

    [Fact]
    public void DestinationOriginatedFailures_AreOutsideTheAtomicityContract()
    {
        // The exception-safety contract covers validation of the supplied value. A
        // destination IBufferWriter that itself throws is outside the contract; its
        // contents are undefined and the caller must discard the buffer.
        var writer = new PackStreamWriter(new ThrowingBufferWriter());

        Assert.Throws<InvalidOperationException>(() => writer.WriteInteger(1));
    }

    // ---- helpers ---------------------------------------------------------------

    private sealed class ThrowingBufferWriter : IBufferWriter<byte>
    {
        public void Advance(int count) => throw new InvalidOperationException("destination failure");

        public Memory<byte> GetMemory(int sizeHint) => new byte[Math.Max(sizeHint, 1)];

        public Span<byte> GetSpan(int sizeHint) => new byte[Math.Max(sizeHint, 1)];
    }

    private static PackStreamNode[] Nodes(int count)
    {
        var nodes = new PackStreamNode[count];
        for (var i = 0; i < count; i++)
        {
            nodes[i] = new PackStreamNode(i + 1, Array.Empty<string>(), EmptyMap(), "n:" + (i + 1));
        }

        return nodes;
    }

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    private static byte[] Encode(Action<PackStreamWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        write(new PackStreamWriter(buffer));
        return buffer.WrittenSpan.ToArray();
    }
}
