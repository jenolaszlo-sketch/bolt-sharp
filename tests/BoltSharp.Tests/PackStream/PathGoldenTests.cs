using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte, traversal and validation tests for the Bolt 5.x Path structure
/// (https://neo4j.com/docs/bolt/current/bolt/structure-semantics/). Expected bytes are
/// constructed from the specification, independently of the codec under test.
/// </summary>
public sealed class PathGoldenTests
{
    // ---- golden byte fixtures -------------------------------------------------

    // A. Single node, no relationships, no indices.
    private static byte[] SingleNodePathBytes() => new byte[]
    {
        0xB3, 0x50,
        0x91,
        0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31, // Node(1, [], {}, "n:1")
        0x90,
        0x90,
    };

    // B. Alice --KNOWS--> Bob.
    private static byte[] ForwardPathBytes() => new byte[]
    {
        0xB3, 0x50,
        0x92,
        0xB4, 0x4E, 0x01, 0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,             // Alice node
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x85, 0x41, 0x6C, 0x69, 0x63, 0x65,
        0x83, 0x6E, 0x3A, 0x31,
        0xB4, 0x4E, 0x02, 0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,             // Bob node
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x83, 0x42, 0x6F, 0x62,
        0x83, 0x6E, 0x3A, 0x32,
        0x91,
        0xB4, 0x72, 0x01, 0x85, 0x4B, 0x4E, 0x4F, 0x57, 0x53, 0xA0, 0x83, 0x72, 0x3A, 0x31, // KNOWS
        0x92, 0x01, 0x01,                                                             // indices [1, 1]
    };

    // C. Bob <--KNOWS-- Alice: the same relationship traversed in reverse.
    private static byte[] ReversePathBytes() => new byte[]
    {
        0xB3, 0x50,
        0x92,
        0xB4, 0x4E, 0x02, 0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,             // Bob node first
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x83, 0x42, 0x6F, 0x62,
        0x83, 0x6E, 0x3A, 0x32,
        0xB4, 0x4E, 0x01, 0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,             // Alice node second
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x85, 0x41, 0x6C, 0x69, 0x63, 0x65,
        0x83, 0x6E, 0x3A, 0x31,
        0x91,
        0xB4, 0x72, 0x01, 0x85, 0x4B, 0x4E, 0x4F, 0x57, 0x53, 0xA0, 0x83, 0x72, 0x3A, 0x31,
        0x92, 0xFF, 0x01,                                                             // indices [-1, 1]
    };

    // D. The official specification example:
    //    (42)-[1000]->(69)-[1000]->(42)<-[1001]-(1)
    private static byte[] SpecExamplePathBytes() => new byte[]
    {
        0xB3, 0x50,
        0x93,
        0xB4, 0x4E, 0x2A, 0x90, 0xA0, 0x84, 0x6E, 0x3A, 0x34, 0x32,   // Node 42
        0xB4, 0x4E, 0x45, 0x90, 0xA0, 0x84, 0x6E, 0x3A, 0x36, 0x39,   // Node 69
        0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31,         // Node 1
        0x92,
        0xB4, 0x72, 0xC9, 0x03, 0xE8, 0x80, 0xA0, 0x86, 0x72, 0x3A, 0x31, 0x30, 0x30, 0x30, // Rel 1000
        0xB4, 0x72, 0xC9, 0x03, 0xE9, 0x80, 0xA0, 0x86, 0x72, 0x3A, 0x31, 0x30, 0x30, 0x31, // Rel 1001
        0x96, 0x01, 0x01, 0x01, 0x00, 0xFE, 0x02,                     // indices [1,1,1,0,-2,2]
    };

    // Node 7 and relationship 7 deliberately share a numeric ID.
    private static byte[] OverlappingIdPathBytes() => new byte[]
    {
        0xB3, 0x50,
        0x91,
        0xB4, 0x4E, 0x07, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x37,          // Node(7, "n:7")
        0x91,
        0xB4, 0x72, 0x07, 0x80, 0xA0, 0x83, 0x72, 0x3A, 0x37,          // UnboundRelationship(7, "r:7")
        0x90,
    };

    // ---- graph objects --------------------------------------------------------

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    private static PackStreamNode Node(long id, string elementId) =>
        new(id, Array.Empty<string>(), EmptyMap(), elementId);

    private static PackStreamUnboundRelationship Relationship(long id, string elementId) =>
        new(id, string.Empty, EmptyMap(), elementId);

    private static PackStreamNode Alice() => new(
        1,
        new[] { "Person" },
        new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("Alice")) }),
        "n:1");

    private static PackStreamNode Bob() => new(
        2,
        new[] { "Person" },
        new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("Bob")) }),
        "n:2");

    private static PackStreamUnboundRelationship Knows() =>
        new(1, "KNOWS", EmptyMap(), "r:1");

    private static PackStreamPath SingleNodePath() =>
        new(new[] { Node(1, "n:1") }, Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>());

    private static PackStreamPath ForwardPath() =>
        new(new[] { Alice(), Bob() }, new[] { Knows() }, new long[] { 1, 1 });

    private static PackStreamPath ReversePath() =>
        new(new[] { Bob(), Alice() }, new[] { Knows() }, new long[] { -1, 1 });

    private static PackStreamPath SpecExamplePath() =>
        new(
            new[] { Node(42, "n:42"), Node(69, "n:69"), Node(1, "n:1") },
            new[] { Relationship(1000, "r:1000"), Relationship(1001, "r:1001") },
            new long[] { 1, 1, 1, 0, -2, 2 });

    // ---- golden-byte tests ----------------------------------------------------

    [Fact]
    public void WritePath_SingleNode_UsesGoldenBytes() =>
        Assert.Equal(SingleNodePathBytes(), PackStreamTestHelpers.Encode(w => w.WritePath(SingleNodePath())));

    [Fact]
    public void WritePath_ForwardRelationship_UsesGoldenBytes() =>
        Assert.Equal(ForwardPathBytes(), PackStreamTestHelpers.Encode(w => w.WritePath(ForwardPath())));

    [Fact]
    public void WritePath_ReverseRelationship_UsesGoldenBytes() =>
        Assert.Equal(ReversePathBytes(), PackStreamTestHelpers.Encode(w => w.WritePath(ReversePath())));

    [Fact]
    public void WritePath_SpecExample_UsesGoldenBytes() =>
        Assert.Equal(SpecExamplePathBytes(), PackStreamTestHelpers.Encode(w => w.WritePath(SpecExamplePath())));

    [Fact]
    public void WriteValue_ProducesTheSameBytesAsWritePath()
    {
        Assert.Equal(SingleNodePathBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(SingleNodePath())));
        Assert.Equal(ForwardPathBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(ForwardPath())));
        Assert.Equal(ReversePathBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(ReversePath())));
        Assert.Equal(SpecExamplePathBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(SpecExamplePath())));
    }

    [Fact]
    public void ReadPath_SingleNode()
    {
        var path = Assert.IsType<PackStreamPath>(
            PackStreamTestHelpers.Decode(SingleNodePathBytes(), r => r.ReadValue()));

        Assert.Single(path.Nodes);
        Assert.Empty(path.UnboundRelationships);
        Assert.Empty(path.Indices);
        Assert.Equal("n:1", path.Nodes[0].ElementId);
    }

    [Fact]
    public void ReadPath_ForwardTraversal()
    {
        var path = Assert.IsType<PackStreamPath>(
            PackStreamTestHelpers.Decode(ForwardPathBytes(), r => r.ReadValue()));

        var (nodeElementIds, relationshipElementIds, forward) = Walk(path);

        Assert.Equal(new[] { "n:1", "n:2" }, nodeElementIds);
        Assert.Equal(new[] { "r:1" }, relationshipElementIds);
        Assert.Equal(new[] { true }, forward);

        // Field order on the wire is nodes, rels, indices.
        Assert.Equal(2, path.Nodes.Count);
        Assert.Single(path.UnboundRelationships);
        Assert.Equal(new long[] { 1, 1 }, path.Indices);
    }

    [Fact]
    public void ReadPath_ReverseTraversal_PreservesTheSignAndTheRelationshipIdentity()
    {
        var path = Assert.IsType<PackStreamPath>(
            PackStreamTestHelpers.Decode(ReversePathBytes(), r => r.ReadValue()));

        var (nodeElementIds, relationshipElementIds, forward) = Walk(path);

        Assert.Equal(new[] { "n:2", "n:1" }, nodeElementIds);
        Assert.Equal(new[] { "r:1" }, relationshipElementIds);
        Assert.Equal(new[] { false }, forward);

        // The index keeps its sign; the relationship entity is unchanged and not rewritten.
        Assert.Equal(new long[] { -1, 1 }, path.Indices);
        Assert.Equal(1, path.UnboundRelationships[0].Id);
        Assert.Equal("r:1", path.UnboundRelationships[0].ElementId);
    }

    [Fact]
    public void ReadPath_SpecExample_RepeatedReferencesAreNotDuplicated()
    {
        var path = Assert.IsType<PackStreamPath>(
            PackStreamTestHelpers.Decode(SpecExamplePathBytes(), r => r.ReadValue()));

        // Three stored nodes and two stored relationships, visited four times.
        Assert.Equal(3, path.Nodes.Count);
        Assert.Equal(2, path.UnboundRelationships.Count);
        Assert.Equal(new long[] { 1, 1, 1, 0, -2, 2 }, path.Indices);

        var (nodeElementIds, relationshipElementIds, forward) = Walk(path);

        Assert.Equal(new[] { "n:42", "n:69", "n:42", "n:1" }, nodeElementIds);
        Assert.Equal(new[] { "r:1000", "r:1000", "r:1001" }, relationshipElementIds);
        Assert.Equal(new[] { true, true, false }, forward);

        // Relationship 1000 is traversed twice but stored exactly once.
        Assert.Equal(1, path.UnboundRelationships.Count(relationship => relationship.Id == 1000));
        Assert.Equal(2, relationshipElementIds.Count(elementId => elementId == "r:1000"));
    }

    [Fact]
    public void ReadPath_PreservesIdentityWhenNumericIdsOverlap()
    {
        var path = Assert.IsType<PackStreamPath>(
            PackStreamTestHelpers.Decode(OverlappingIdPathBytes(), r => r.ReadValue()));

        Assert.Single(path.Nodes);
        Assert.Single(path.UnboundRelationships);
        Assert.Equal(7, path.Nodes[0].Id);
        Assert.Equal(7, path.UnboundRelationships[0].Id);
        Assert.IsNotType<PackStreamNode>(path.UnboundRelationships[0]);
        Assert.Equal("n:7", path.Nodes[0].ElementId);
        Assert.Equal("r:7", path.UnboundRelationships[0].ElementId);
    }

    [Fact]
    public void ReadValue_PathFollowedByScalar_TracksPosition()
    {
        var bytes = Concat(ForwardPathBytes(), new byte[] { 0x2A });
        var reader = new PackStreamReader(bytes);

        var path = Assert.IsType<PackStreamPath>(reader.ReadValue());
        Assert.Equal(2, path.Nodes.Count);
        Assert.Equal(ForwardPathBytes().Length, reader.Position);

        Assert.Equal(42L, reader.ReadInteger());
        Assert.Equal(ForwardPathBytes().Length + 1, reader.Position);
        Assert.False(reader.HasMore);
    }

    // ---- validation -----------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0xB2, 0x50 })] // arity 2
    [InlineData(new byte[] { 0xB4, 0x50 })] // arity 4
    public void ReadPath_WrongArity_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());

    [Fact]
    public void ReadPath_EmptyNodeList_ThrowsFormat() =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(
            new byte[] { 0xB3, 0x50, 0x90, 0x90, 0x90 }, r => r.ReadValue());

    [Fact]
    public void ReadPath_OddNumberOfIndices_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x90, 0x91, 0x01 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_RelationshipIndexZero_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x90, 0x92, 0x00, 0x00 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_RelationshipIndexTooLarge_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x91, 0xB4, 0x72, 0x01, 0x80, 0xA0, 0x83, 0x72, 0x3A, 0x31, 0x92, 0x02, 0x00 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_RelationshipIndexTooNegative_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x91, 0xB4, 0x72, 0x01, 0x80, 0xA0, 0x83, 0x72, 0x3A, 0x31, 0x92, 0xFE, 0x00 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_LongMinValueRelationshipIndex_ThrowsFormatWithoutOverflow()
    {
        var bytes = Concat(
            HeaderWithOneNode(),
            new byte[] { 0x91, 0xB4, 0x72, 0x01, 0x80, 0xA0, 0x83, 0x72, 0x3A, 0x31 },
            new byte[] { 0x92, 0xCB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_NegativeNodeIndex_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x90, 0x92, 0x01, 0xFF });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_NodeIndexOutOfRange_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x90, 0x92, 0x01, 0x01 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_NonNodeElement_ThrowsFormat()
    {
        // nodes = [1]
        var bytes = new byte[] { 0xB3, 0x50, 0x91, 0x01, 0x90, 0x90 };

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_NonUnboundRelationshipElement_ThrowsFormat()
    {
        // rels = [Node]
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x91, 0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31, 0x90 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_NonIntegerIndex_ThrowsFormat()
    {
        // indices = ["A"]
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x90, 0x91, 0x81, 0x41 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_NodesFieldNotAList_ThrowsFormat()
    {
        var bytes = new byte[] { 0xB3, 0x50, 0x01, 0x90, 0x90 };

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_IndicesFieldNotAList_ThrowsFormat()
    {
        var bytes = Concat(HeaderWithOneNode(), new byte[] { 0x90, 0x01 });

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Theory]
    [InlineData(new byte[] { 0xB3, 0x50 })]                                              // no fields
    [InlineData(new byte[] { 0xB3, 0x50, 0x91 })]                                        // truncated node list
    [InlineData(new byte[] { 0xB3, 0x50, 0x91, 0xB4, 0x4E })]                            // truncated node
    [InlineData(new byte[] { 0xB3, 0x50, 0x91, 0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31, 0x90 })] // missing indices
    public void ReadPath_Truncated_ThrowsTruncated(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadValue());

    [Fact]
    public void ReadPath_RequiresFourNestingLevels()
    {
        // Path (1) -> field lists (2) -> node (3) -> the node's own containers (4).
        var bytes = SingleNodePathBytes();

        Assert.IsType<PackStreamPath>(
            new PackStreamReader(bytes, new PackStreamLimits(maxDepth: 4)).ReadValue());

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(bytes, new PackStreamLimits(maxDepth: 3)).ReadValue());
    }

    [Fact]
    public void ReadPath_ElementBudgetCoversTheWholeStructure()
    {
        // path + nodes list + node + its four fields + rels list + indices list = 9 values.
        var bytes = SingleNodePathBytes();

        Assert.IsType<PackStreamPath>(
            new PackStreamReader(bytes, new PackStreamLimits(maxTotalElements: 9)).ReadValue());

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(bytes, new PackStreamLimits(maxTotalElements: 8)).ReadValue());
    }

    [Fact]
    public void ReadPath_OversizedDeclaredNodeCount_ThrowsFormatBeforeAllocation()
    {
        // nodes declared as 100,001 items (> default MaxListItems) with no payload.
        var bytes = new byte[] { 0xB3, 0x50, 0xD6, 0x00, 0x01, 0x86, 0xA1, 0x90, 0x90 };

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Fact]
    public void ReadPath_EmptyNodeList_PositionReflectsConsumedBytes()
    {
        var reader = new PackStreamReader(new byte[] { 0xB3, 0x50, 0x90, 0x90, 0x90 });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(3, reader.Position);
    }

    [Fact]
    public void ReadPath_WrongArity_PositionReflectsConsumedBytes()
    {
        var reader = new PackStreamReader(new byte[] { 0xB2, 0x50 });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void ConstructPath_EmptyNodes_ThrowsArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackStreamPath(
                Array.Empty<PackStreamNode>(),
                Array.Empty<PackStreamUnboundRelationship>(),
                Array.Empty<long>()));

    [Fact]
    public void ConstructPath_OddIndices_ThrowsArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackStreamPath(
                new[] { Node(1, "n:1") },
                Array.Empty<PackStreamUnboundRelationship>(),
                new long[] { 1 }));

    [Fact]
    public void ConstructPath_ZeroRelationshipIndex_ThrowsArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackStreamPath(
                new[] { Node(1, "n:1") },
                Array.Empty<PackStreamUnboundRelationship>(),
                new long[] { 0, 0 }));

    [Fact]
    public void ConstructPath_LongMinValueIndex_ThrowsArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PackStreamPath(
                new[] { Node(1, "n:1") },
                new[] { Relationship(1, "r:1") },
                new long[] { long.MinValue, 0 }));

    // ---- helpers --------------------------------------------------------------

    /// <summary>Marker + tag + a one-entry node list containing Node(1, "n:1").</summary>
    private static byte[] HeaderWithOneNode() => new byte[]
    {
        0xB3, 0x50,
        0x91,
        0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31,
    };

    private static (List<string> NodeElementIds, List<string> RelationshipElementIds, List<bool> Forward) Walk(
        PackStreamPath path)
    {
        var nodeElementIds = new List<string> { path.Nodes[0].ElementId };
        var relationshipElementIds = new List<string>();
        var forward = new List<bool>();

        for (var i = 0; i < path.Indices.Count; i += 2)
        {
            var relationshipIndex = path.Indices[i];
            var nodeIndex = path.Indices[i + 1];

            // Range validation already guarantees |relationshipIndex| fits in an int.
            var oneBased = relationshipIndex > 0 ? relationshipIndex : -relationshipIndex;
            relationshipElementIds.Add(path.UnboundRelationships[(int)oneBased - 1].ElementId);
            forward.Add(relationshipIndex > 0);
            nodeElementIds.Add(path.Nodes[(int)nodeIndex].ElementId);
        }

        return (nodeElementIds, relationshipElementIds, forward);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(part => part.Length);
        var result = new byte[total];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
