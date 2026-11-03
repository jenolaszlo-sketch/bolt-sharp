using System.Buffers;
using System.Runtime.InteropServices;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Qualification;

/// <summary>
/// B1-Q2 independent requalification probes.
/// These probes were designed by the requalification reviewer directly against the
/// remediation commit 93759a2 and the B1-Q1 findings, independently of the B1-R1
/// remediation tests. They replay the reviewer's attack vectors against the remediated
/// implementation, close coverage gaps the remediation tests left open, and attempt to
/// falsify the remediation's contracts (including traversal stability for the writer's
/// two-pass exception-safety design).
/// <para>
/// Probes suffixed <c>_Documented</c> pass while demonstrating a boundary of a contract
/// that the B1-R1 evidence overstates; they are findings for
/// docs/reviews/B1-Q2-packstream-requalification.md, not endorsements.
/// </para>
/// </summary>
public sealed class B1Q2RequalificationProbes
{
    // ------------------------------------------------------------------
    // F1: immutability of public value representations.
    // ------------------------------------------------------------------

    [Fact]
    public void B1Q2_F1_AllPublicCollections_RejectArrayCasts()
    {
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1) });
        var map = new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("k", new PackStreamInteger(1)) });
        var structure = new PackStreamStructure(7, new PackStreamValue?[] { new PackStreamInteger(1) });
        var node = new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1");
        var path = new PackStreamPath(
            new[] { new PackStreamNode(1, Array.Empty<string>(), EmptyMap(), "n:1") },
            new[] { new PackStreamUnboundRelationship(1, "KNOWS", EmptyMap(), "r:1") },
            new long[] { 1, 0 });

        Assert.Throws<InvalidCastException>(() => _ = (PackStreamValue?[])list.Items);
        Assert.Throws<InvalidCastException>(() => _ = (KeyValuePair<string, PackStreamValue?>[])map.Entries);
        Assert.Throws<InvalidCastException>(() => _ = (PackStreamValue?[])structure.Fields);
        Assert.Throws<InvalidCastException>(() => _ = (string[])node.Labels);
        Assert.Throws<InvalidCastException>(() => _ = (PackStreamNode[])path.Nodes);
        Assert.Throws<InvalidCastException>(() => _ = (PackStreamUnboundRelationship[])path.UnboundRelationships);
        Assert.Throws<InvalidCastException>(() => _ = (long[])path.Indices);
    }

    [Fact]
    public void B1Q2_F1_AllPublicCollections_IListViews_AreReadOnly()
    {
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1) });
        var map = new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("k", new PackStreamInteger(1)) });
        var structure = new PackStreamStructure(7, new PackStreamValue?[] { new PackStreamInteger(1) });
        var node = new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1");
        var path = new PackStreamPath(
            new[] { new PackStreamNode(1, Array.Empty<string>(), EmptyMap(), "n:1") },
            new[] { new PackStreamUnboundRelationship(1, "KNOWS", EmptyMap(), "r:1") },
            new long[] { 1, 0 });

        var listView = (IList<PackStreamValue?>)list.Items;
        Assert.Throws<NotSupportedException>(() => listView.Add(null));
        Assert.Throws<NotSupportedException>(() => listView.Insert(0, null));
        Assert.Throws<NotSupportedException>(() => listView.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => listView.Clear());
        Assert.Throws<NotSupportedException>(() => listView[0] = null);

        var mapView = (ICollection<KeyValuePair<string, PackStreamValue?>>)map.Entries;
        Assert.Throws<NotSupportedException>(() => mapView.Add(new("x", null)));
        Assert.Throws<NotSupportedException>(() => mapView.Clear());

        var structureView = (IList<PackStreamValue?>)structure.Fields;
        Assert.Throws<NotSupportedException>(() => structureView.Clear());

        var labelsView = (ICollection<string>)node.Labels;
        Assert.Throws<NotSupportedException>(() => labelsView.Add("Evil"));

        var pathNodesView = (IList<PackStreamNode>)path.Nodes;
        Assert.Throws<NotSupportedException>(() => pathNodesView.Clear());
        var pathRelsView = (IList<PackStreamUnboundRelationship>)path.UnboundRelationships;
        Assert.Throws<NotSupportedException>(() => pathRelsView.Clear());
        var indicesView = (IList<long>)path.Indices;
        Assert.Throws<NotSupportedException>(() => indicesView[0] = 42);
        Assert.Throws<NotSupportedException>(() => indicesView.Clear());

        // Stored values unchanged after every rejected mutation attempt.
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(list.Items[0]).Value);
        Assert.Equal("Person", node.Labels[0]);
        Assert.Equal(new long[] { 1, 0 }, path.Indices);
    }

    [Fact]
    public void B1Q2_F1_Bytes_ConstructorInputMutation_CannotChangeStoredValue()
    {
        // Coverage gap in B1-R1: the remediation tests replayed the MemoryMarshal vector
        // but not constructor-input mutation for byte payloads.
        var source = new byte[] { 0x01, 0x02, 0x03 };
        var value = new PackStreamBytes(source);
        var expected = new byte[] { 0xCC, 0x03, 0x01, 0x02, 0x03 };

        source[0] = 0xFF;

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, value.Value.ToArray());
        Assert.Equal(expected, Encode(v => v.WriteValue(value)));
    }

    [Fact]
    public void B1Q2_F1_Path_ConstructorInputArrays_MutationAfterConstruction_CannotChangeStoredPath()
    {
        // The original reviewer vector used array inputs; the remediation test exercised
        // list inputs. Array inputs must be defensively copied too.
        var nodes = new[] { new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1") };
        var relationships = new[] { new PackStreamUnboundRelationship(1, "KNOWS", EmptyMap(), "r:1") };
        var indices = new long[] { 1, 0 };
        var path = new PackStreamPath(nodes, relationships, indices);
        var expected = Encode(v => v.WritePath(path));

        nodes[0] = new PackStreamNode(9, new[] { "Other" }, EmptyMap(), "n:9");
        relationships[0] = new PackStreamUnboundRelationship(9, "OTHER", EmptyMap(), "r:9");
        indices[0] = -1;

        Assert.Equal(1, path.Nodes[0].Id);
        Assert.Equal("r:1", path.UnboundRelationships[0].ElementId);
        Assert.Equal(new long[] { 1, 0 }, path.Indices);
        Assert.Equal(expected, Encode(v => v.WritePath(path)));
    }

    [Fact]
    public void B1Q2_F1_Bytes_NestedThreeLevelsDeep_CannotChangeRootEncoding()
    {
        var bytes = new PackStreamBytes(new byte[] { 0xAA });
        var value = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>(
                "l",
                new PackStreamList(new PackStreamValue?[]
                {
                    new PackStreamStructure(9, new PackStreamValue?[] { bytes }),
                })),
        });
        var expected = Encode(v => v.WriteValue(value));

        Assert.True(MemoryMarshal.TryGetArray(bytes.Value, out var segment));
        segment.Array![segment.Offset] = 0x00;

        Assert.Equal(expected, Encode(v => v.WriteValue(value)));
    }

    // ------------------------------------------------------------------
    // F2: Path writer limits.
    // ------------------------------------------------------------------

    [Fact]
    public void B1Q2_F2_PathLimits_KeyOnMaxListItems_NotMaxMapEntries()
    {
        // Falsification attempt: if the implementation bounded path field lists with the
        // wrong limit, a three-node path under maxListItems 4 but above maxMapEntries 2
        // would be rejected. The correct implementation accepts it.
        var limits = new PackStreamLimits(maxListItems: 4, maxMapEntries: 2);

        var buffer = new ArrayBufferWriter<byte>();
        new PackStreamWriter(buffer, limits).WritePath(
            new PackStreamPath(Nodes(3), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>()));

        Assert.True(buffer.WrittenCount > 0);
        Assert.IsType<PackStreamPath>(new PackStreamReader(buffer.WrittenSpan.ToArray(), limits).ReadValue());

        // Above the list limit it is still rejected.
        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamWriter(new ArrayBufferWriter<byte>(), limits).WritePath(
                new PackStreamPath(Nodes(5), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>())));
    }

    [Fact]
    public void B1Q2_F2_Path_ZeroListLimit_RejectsAnyPath_DestinationUnchanged()
    {
        var limits = new PackStreamLimits(maxListItems: 0);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, limits);
        var path = new PackStreamPath(Nodes(1), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void B1Q2_F2_ManualOversizedPathWire_IsRejectedByReader_UnderIdenticalLimits()
    {
        // The inverse of the original Probe07 asymmetry: the writer refuses to emit a
        // five-node path under maxListItems 4, and wire bytes constructed by hand with a
        // five-item node list are still rejected by the reader under identical limits.
        var limits = new PackStreamLimits(maxListItems: 4);
        var node = new byte[] { 0xB4, 0x4E, 0x00, 0x90, 0xA0, 0x80 };
        var oversized = new List<byte> { 0xB3, 0x50, 0x95 }; // Path, node list declaring 5 items
        for (var i = 0; i < 5; i++)
        {
            oversized.AddRange(node);
        }

        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(oversized.ToArray(), limits).ReadValue());
    }

    // ------------------------------------------------------------------
    // F3: exception safety of writes.
    // ------------------------------------------------------------------

    [Fact]
    public void B1Q2_F3_WriteNode_InvalidElementId_LastField_DestinationUnchanged()
    {
        // The deepest possible failure inside a Node: the fourth and final field.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var node = new PackStreamNode(1, Array.Empty<string>(), EmptyMap(), "\uD800");

        Assert.Throws<PackStreamFormatException>(() => writer.WriteNode(node));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void B1Q2_F3_WriteRelationship_InvalidEighthField_DestinationUnchanged()
    {
        // The deepest possible failure inside a Relationship: the eighth and final field.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var relationship = new PackStreamRelationship(
            1, 1, 2, "KNOWS", EmptyMap(), "r:1", "n:1", "\uD800");

        Assert.Throws<PackStreamFormatException>(() => writer.WriteRelationship(relationship));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void B1Q2_F3_WritePath_InvalidStringInSecondNode_DestinationUnchanged()
    {
        // Under the pre-remediation writer, every byte of the first node (and the outer
        // headers) would already be committed when the second node's property failed.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var properties = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>("note", new PackStreamString("\uD800")),
        });
        var path = new PackStreamPath(
            new[]
            {
                new PackStreamNode(1, Array.Empty<string>(), EmptyMap(), "n:1"),
                new PackStreamNode(2, Array.Empty<string>(), properties, "n:2"),
            },
            Array.Empty<PackStreamUnboundRelationship>(),
            Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void B1Q2_F3_WriteValue_UnsupportedValueSubclass_NestedInList_DestinationUnchanged()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);
        var list = new PackStreamList(new PackStreamValue?[] { new UnsupportedValue() });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteValue(list));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void B1Q2_F3_FailedWrite_CommitsNothing_SubsequentValidWritesAreByteExact()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        writer.WriteInteger(1);
        Assert.Throws<PackStreamFormatException>(
            () => writer.WriteString("\uD800"));
        writer.WriteString("hi");

        Assert.Equal(new byte[] { 0x01, 0x82, 0x68, 0x69 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void B1Q2_F3_DestinationReturningShortSpan_FailsWithForeignException_OutsideTheContract()
    {
        // A destination that violates the IBufferWriter contract (span shorter than the
        // requested sizeHint) produces a non-PackStream exception. This documents the
        // exclusion boundary of the atomicity contract: destination-originated failures
        // are not covered.
        var writer = new PackStreamWriter(new ShortSpanWriter());
        var exception = Record.Exception(() => writer.WriteInteger(1000)); // INT_16: requests 3 bytes

        Assert.NotNull(exception);
        Assert.IsNotType<PackStreamException>(exception);
    }

    [Fact]
    public void B1Q2_F3_StableCallerCollection_TwoPassWrite_MatchesGoldenBytes()
    {
        // A well-behaved non-array IReadOnlyList must produce exactly the canonical bytes
        // even though the writer traverses it twice.
        var items = new ComputedList();
        var bytes = Encode(w => w.WriteList(items));

        Assert.Equal(new byte[] { 0x92, 0x01, 0x82, 0x6F, 0x6B }, bytes);
    }

    [Fact]
    public void B1Q2_F3_UnstableCallerCollection_CanDefeatTwoPassAtomicity_Documented()
    {
        // Falsification attempt against the two-pass design: a caller-supplied collection
        // that hands out a valid value during the validation pass and an invalid one during
        // the emission pass is committed partially (the list header alone). The B1-R1
        // evidence states "a failed write never commits partial output" without recording
        // the traversal-stability assumption this relies on; library-provided values are
        // immutable and unaffected. This probe documents the boundary of the guarantee.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(new MutatingBetweenTraversals()));

        Assert.Equal(1, buffer.WrittenCount);
        Assert.Equal(0x91, buffer.WrittenSpan[0]); // the TINY_LIST(1) header, nothing else
    }

    // ------------------------------------------------------------------
    // F4: reserved graph-tag arity.
    // ------------------------------------------------------------------

    [Fact]
    public void B1Q2_F4_ValidArities_AllFourReservedTags_EncodeAndDecodeAsTypedGraphStructures()
    {
        // Generic structures carrying reserved graph tags with the exact arities remain
        // wire-compatible and are decoded as the typed graph structures.
        var nodeBytes = Encode(w => w.WriteStructure(0x4E, new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamList(new PackStreamValue?[] { new PackStreamString("Person") }),
            EmptyMap(),
            new PackStreamString("n:1"),
        }));
        Assert.IsType<PackStreamNode>(new PackStreamReader(nodeBytes).ReadValue());

        var relationshipBytes = Encode(w => w.WriteStructure(0x52, new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamInteger(1),
            new PackStreamInteger(2),
            new PackStreamString("KNOWS"),
            EmptyMap(),
            new PackStreamString("r:1"),
            new PackStreamString("n:1"),
            new PackStreamString("n:2"),
        }));
        Assert.IsType<PackStreamRelationship>(new PackStreamReader(relationshipBytes).ReadValue());

        var unboundBytes = Encode(w => w.WriteStructure(0x72, new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamString("KNOWS"),
            EmptyMap(),
            new PackStreamString("r:1"),
        }));
        Assert.IsType<PackStreamUnboundRelationship>(new PackStreamReader(unboundBytes).ReadValue());

        var pathBytes = Encode(w => w.WriteStructure(0x50, new PackStreamValue?[]
        {
            new PackStreamList(new PackStreamValue?[]
            {
                new PackStreamNode(1, Array.Empty<string>(), EmptyMap(), "n:1"),
            }),
            new PackStreamList(new PackStreamValue?[]
            {
                new PackStreamUnboundRelationship(1, "KNOWS", EmptyMap(), "r:1"),
            }),
            new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(0) }),
        }));
        Assert.IsType<PackStreamPath>(new PackStreamReader(pathBytes).ReadValue());
    }

    [Fact]
    public void B1Q2_F4_ReservedTagValidArityWrongFieldTypes_WritableButNotTypedDecodable_Documented()
    {
        // The F4 remediation is scoped to arity. A generic structure with a reserved graph
        // tag, valid arity, and field types the graph structure does not accept is still
        // emitted and is then rejected by the typed reader. This is consistent with the
        // original finding (arity only) and with preserved producer behaviour, but it means
        // the writer does not guarantee "every emitted byte is decodable" for generic
        // graph-tag structures. Documented as a residual risk in the B1-Q2 report.
        var bytes = Encode(w => w.WriteStructure(0x4E, new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamInteger(2),
            new PackStreamInteger(3),
            new PackStreamInteger(4),
        }));

        Assert.Equal(0xB4, bytes[0]);
        Assert.Equal(0x4E, bytes[1]);
        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(bytes).ReadValue());
    }

    [Theory]
    [InlineData(0x01, 0)]
    [InlineData(0x48, 1)]
    [InlineData(0x7F, 15)]
    public void B1Q2_F4_UnknownTags_AnyArity_RoundTrip(int tag, int fieldCount)
    {
        var fields = new PackStreamValue?[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            fields[i] = new PackStreamInteger(i);
        }

        var bytes = Encode(w => w.WriteStructure((byte)tag, fields));
        var decoded = Assert.IsType<PackStreamStructure>(new PackStreamReader(bytes).ReadValue());

        Assert.Equal((byte)tag, decoded.Tag);
        Assert.Equal(fieldCount, decoded.Fields.Count);
    }

    // ------------------------------------------------------------------
    // F5: reader failure contract.
    // ------------------------------------------------------------------

    [Fact]
    public void B1Q2_F5_MalformedUtf8_Position_ExcludesUnconsumedPayload()
    {
        // STRING_8 declaring two bytes whose payload (C3 28) is malformed UTF-8: the
        // payload bytes are not consumed, so Position stops at the length prefix. This
        // pins the precise meaning of "bytes consumed before the failure".
        var reader = new PackStreamReader(new byte[] { 0xD0, 0x02, 0xC3, 0x28 });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadString());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void B1Q2_F5_BudgetExhaustion_IsADecodingException_ReaderMustBeDiscarded()
    {
        var limits = new PackStreamLimits(maxTotalElements: 2);
        var reader = new PackStreamReader(new byte[] { 0x93, 0x01, 0x02, 0x03 }, limits);

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);

        // The supported recovery: discard the reader and re-parse the remainder.
        Assert.Equal(2L, new PackStreamReader(new byte[] { 0x02, 0x03 }, limits).ReadInteger());
    }

    [Fact]
    public void B1Q2_F5_NestedFailure_PositionReflectsBytesConsumedAtTheFailurePoint()
    {
        // 91 (list marker) B0 (structure marker) 4E (tag) — the invalid node arity is
        // raised with all three bytes consumed.
        var reader = new PackStreamReader(new byte[] { 0x91, 0xB0, 0x4E });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(3, reader.Position);
    }

    // ---- helpers ---------------------------------------------------------

    private sealed class UnsupportedValue : PackStreamValue
    {
    }

    /// <summary>
    /// A well-behaved caller collection: every traversal returns equal content.
    /// </summary>
    private sealed class ComputedList : IReadOnlyList<PackStreamValue?>
    {
        public int Count => 2;

        public PackStreamValue? this[int index] => index switch
        {
            0 => new PackStreamInteger(1),
            1 => new PackStreamString("ok"),
            _ => throw new IndexOutOfRangeException(),
        };

        public IEnumerator<PackStreamValue?> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A hostile caller collection: the value handed out for index 0 changes between the
    /// writer's validation pass and its emission pass.
    /// </summary>
    private sealed class MutatingBetweenTraversals : IReadOnlyList<PackStreamValue?>
    {
        private int _reads;

        public int Count => 1;

        public PackStreamValue? this[int index]
        {
            get
            {
                _reads++;
                return _reads <= 1 ? new PackStreamString("ok") : new PackStreamString("\uD800");
            }
        }

        public IEnumerator<PackStreamValue?> GetEnumerator()
        {
            yield return this[0];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A destination that violates the IBufferWriter contract by returning spans shorter
    /// than the requested sizeHint.
    /// </summary>
    private sealed class ShortSpanWriter : IBufferWriter<byte>
    {
        public void Advance(int count)
        {
        }

        public Memory<byte> GetMemory(int sizeHint) => new byte[1];

        public Span<byte> GetSpan(int sizeHint) => new byte[1];
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
