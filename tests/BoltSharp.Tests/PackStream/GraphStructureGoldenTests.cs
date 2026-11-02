using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.PackStream;

/// <summary>
/// Golden-byte, identity and validation tests for the Bolt 5.x graph structures
/// (https://neo4j.com/docs/bolt/current/bolt/structure-semantics/). Expected bytes are
/// constructed from the specification, independently of the codec under test.
/// </summary>
public sealed class GraphStructureGoldenTests
{
    // ---- golden byte fixtures -------------------------------------------------

    private static byte[] AliceBytes() => new byte[]
    {
        0xB4, 0x4E,                                             // Node marker + tag
        0x01,                                                   // id = 1
        0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,         // labels = ["Person"]
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x85, 0x41, 0x6C, 0x69, 0x63, 0x65, // {"name": "Alice"}
        0x83, 0x6E, 0x3A, 0x31,                                 // element_id = "n:1"
    };

    private static byte[] BobBytes() => new byte[]
    {
        0xB4, 0x4E,
        0x02,                                                   // id = 2
        0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x83, 0x42, 0x6F, 0x62, // {"name": "Bob"}
        0x83, 0x6E, 0x3A, 0x32,                                 // element_id = "n:2"
    };

    private static byte[] RelationshipBytes() => new byte[]
    {
        0xB8, 0x52,                                             // Relationship marker + tag
        0x01,                                                   // id = 1
        0x01,                                                   // startNodeId = 1
        0x02,                                                   // endNodeId = 2
        0x85, 0x4B, 0x4E, 0x4F, 0x57, 0x53,                     // type = "KNOWS"
        0xA1, 0x85, 0x73, 0x69, 0x6E, 0x63, 0x65, 0xC9, 0x07, 0xE8, // {"since": 2024}
        0x83, 0x72, 0x3A, 0x31,                                 // element_id = "r:1"
        0x83, 0x6E, 0x3A, 0x31,                                 // start_node_element_id = "n:1"
        0x83, 0x6E, 0x3A, 0x32,                                 // end_node_element_id = "n:2"
    };

    private static byte[] UnboundBytes() => new byte[]
    {
        0xB4, 0x72,                                             // UnboundRelationship marker + tag
        0x01,                                                   // id = 1
        0x85, 0x4B, 0x4E, 0x4F, 0x57, 0x53,                     // type = "KNOWS"
        0xA0,                                                   // properties = {}
        0x83, 0x72, 0x3A, 0x31,                                 // element_id = "r:1"
    };

    // ---- graph objects --------------------------------------------------------

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

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

    private static PackStreamRelationship Knows() => new(
        1,
        1,
        2,
        "KNOWS",
        new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("since", new PackStreamInteger(2024)) }),
        "r:1",
        "n:1",
        "n:2");

    private static PackStreamUnboundRelationship UnboundKnows() => new(
        1,
        "KNOWS",
        EmptyMap(),
        "r:1");

    // ---- golden-byte tests ----------------------------------------------------

    [Fact]
    public void WriteNode_UsesGoldenBytes() =>
        Assert.Equal(AliceBytes(), PackStreamTestHelpers.Encode(w => w.WriteNode(Alice())));

    [Fact]
    public void WriteRelationship_UsesGoldenBytes() =>
        Assert.Equal(RelationshipBytes(), PackStreamTestHelpers.Encode(w => w.WriteRelationship(Knows())));

    [Fact]
    public void WriteUnboundRelationship_UsesGoldenBytes() =>
        Assert.Equal(UnboundBytes(), PackStreamTestHelpers.Encode(w => w.WriteUnboundRelationship(UnboundKnows())));

    [Fact]
    public void WriteValue_ProducesTheSameBytesAsTheTypedWriters()
    {
        Assert.Equal(AliceBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(Alice())));
        Assert.Equal(BobBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(Bob())));
        Assert.Equal(RelationshipBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(Knows())));
        Assert.Equal(UnboundBytes(), PackStreamTestHelpers.Encode(w => w.WriteValue(UnboundKnows())));
    }

    [Fact]
    public void ReadNode_DecodesGoldenBytes()
    {
        var node = Assert.IsType<PackStreamNode>(
            PackStreamTestHelpers.Decode(AliceBytes(), r => r.ReadValue()));

        Assert.Equal(1, node.Id);
        Assert.Equal(new[] { "Person" }, node.Labels);
        Assert.Equal("Alice", Assert.IsType<PackStreamString>(node.Properties.Entries[0].Value).Value);
        Assert.Equal("n:1", node.ElementId);
    }

    [Fact]
    public void ReadRelationship_DecodesGoldenBytes()
    {
        var relationship = Assert.IsType<PackStreamRelationship>(
            PackStreamTestHelpers.Decode(RelationshipBytes(), r => r.ReadValue()));

        Assert.Equal(1, relationship.Id);
        Assert.Equal(1, relationship.StartNodeId);
        Assert.Equal(2, relationship.EndNodeId);
        Assert.Equal("KNOWS", relationship.Type);
        Assert.Equal(2024, Assert.IsType<PackStreamInteger>(relationship.Properties.Entries[0].Value).Value);
        Assert.Equal("r:1", relationship.ElementId);
        Assert.Equal("n:1", relationship.StartNodeElementId);
        Assert.Equal("n:2", relationship.EndNodeElementId);
    }

    [Fact]
    public void ReadUnboundRelationship_DecodesGoldenBytes()
    {
        var relationship = Assert.IsType<PackStreamUnboundRelationship>(
            PackStreamTestHelpers.Decode(UnboundBytes(), r => r.ReadValue()));

        Assert.Equal(1, relationship.Id);
        Assert.Equal("KNOWS", relationship.Type);
        Assert.Empty(relationship.Properties.Entries);
        Assert.Equal("r:1", relationship.ElementId);
    }

    // ---- identity tests -------------------------------------------------------

    [Fact]
    public void Identity_NodeIdOneAndRelationshipIdOneRemainDistinctEntities()
    {
        var bytes = Concat(AliceBytes(), BobBytes(), RelationshipBytes(), UnboundBytes());
        var reader = new PackStreamReader(bytes);

        var alice = Assert.IsType<PackStreamNode>(reader.ReadValue());
        var bob = Assert.IsType<PackStreamNode>(reader.ReadValue());
        var knows = Assert.IsType<PackStreamRelationship>(reader.ReadValue());
        var unbound = Assert.IsType<PackStreamUnboundRelationship>(reader.ReadValue());

        // Numeric ID 1 is used by both entity types and must not be conflated.
        Assert.Equal(1, alice.Id);
        Assert.Equal(1, knows.Id);
        Assert.IsNotType<PackStreamNode>(knows);

        // The relationship points from Alice to Bob.
        Assert.Equal(1, knows.StartNodeId);
        Assert.Equal(2, knows.EndNodeId);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);

        // Element IDs survive on every entity.
        Assert.Equal("n:1", alice.ElementId);
        Assert.Equal("n:2", bob.ElementId);
        Assert.Equal("r:1", knows.ElementId);
        Assert.Equal("r:1", unbound.ElementId);
    }

    [Fact]
    public void Relationship_PreservesDirection()
    {
        var relationship = Assert.IsType<PackStreamRelationship>(
            PackStreamTestHelpers.Decode(RelationshipBytes(), r => r.ReadValue()));

        Assert.Equal(1, relationship.StartNodeId);
        Assert.Equal(2, relationship.EndNodeId);
        Assert.Equal("n:1", relationship.StartNodeElementId);
        Assert.Equal("n:2", relationship.EndNodeElementId);
    }

    [Fact]
    public void UnboundRelationship_CarriesNoEndpoints()
    {
        // The UnboundRelationship golden fixture is exactly four fields long and its
        // representation exposes no endpoint members; nothing is invented during decoding.
        var relationship = Assert.IsType<PackStreamUnboundRelationship>(
            PackStreamTestHelpers.Decode(UnboundBytes(), r => r.ReadValue()));

        Assert.Equal(1, relationship.Id);
        Assert.Equal("r:1", relationship.ElementId);
        Assert.Equal("KNOWS", relationship.Type);
        Assert.Empty(relationship.Properties.Entries);
    }

    // ---- structural coverage --------------------------------------------------

    [Fact]
    public void WriteNode_EmptyLabelsAndProperties()
    {
        var node = new PackStreamNode(0, Array.Empty<string>(), EmptyMap(), string.Empty);

        Assert.Equal(
            new byte[] { 0xB4, 0x4E, 0x00, 0x90, 0xA0, 0x80 },
            PackStreamTestHelpers.Encode(w => w.WriteNode(node)));
    }

    [Fact]
    public void WriteNode_MultipleLabels()
    {
        var node = new PackStreamNode(1, new[] { "A", "B" }, EmptyMap(), "n:1");

        Assert.Equal(
            new byte[] { 0xB4, 0x4E, 0x01, 0x92, 0x81, 0x41, 0x81, 0x42, 0xA0, 0x83, 0x6E, 0x3A, 0x31 },
            PackStreamTestHelpers.Encode(w => w.WriteNode(node)));
    }

    [Fact]
    public void WriteNode_UnicodeLabelsAndProperties()
    {
        var node = new PackStreamNode(
            0,
            new[] { "\u00DCn\u00EFc\u00F6d\u00E9" },
            new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("gr\u00F6\u00DFer")) }),
            "u:1");

        var expected = new byte[]
        {
            0xB4, 0x4E, 0x00,
            0x91, 0x8B, 0xC3, 0x9C, 0x6E, 0xC3, 0xAF, 0x63, 0xC3, 0xB6, 0x64, 0xC3, 0xA9,
            0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x88, 0x67, 0x72, 0xC3, 0xB6, 0xC3, 0x9F, 0x65, 0x72,
            0x83, 0x75, 0x3A, 0x31,
        };

        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteNode(node)));
    }

    [Theory]
    [InlineData(long.MinValue, new byte[] { 0xCB, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(long.MaxValue, new byte[] { 0xCB, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    public void WriteNode_Int64IdBoundaries(long id, byte[] idBytes)
    {
        var node = new PackStreamNode(id, Array.Empty<string>(), EmptyMap(), "n:1");

        var expected = new byte[] { 0xB4, 0x4E }
            .Concat(idBytes)
            .Concat(new byte[] { 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31 })
            .ToArray();

        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteNode(node)));
    }

    [Fact]
    public void Node_NestedPropertyMapsAndLists()
    {
        var properties = new PackStreamMap(new[]
        {
            new KeyValuePair<string, PackStreamValue?>(
                "tags",
                new PackStreamList(new PackStreamValue?[] { new PackStreamString("x"), new PackStreamString("y") })),
            new KeyValuePair<string, PackStreamValue?>(
                "meta",
                new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("k", new PackStreamInteger(1)) })),
        });

        var node = new PackStreamNode(7, new[] { "A", "B" }, properties, "n:1");

        var expected = new byte[]
        {
            0xB4, 0x4E, 0x07,
            0x92, 0x81, 0x41, 0x81, 0x42,
            0xA2,
            0x84, 0x74, 0x61, 0x67, 0x73, 0x92, 0x81, 0x78, 0x81, 0x79,
            0x84, 0x6D, 0x65, 0x74, 0x61, 0xA1, 0x81, 0x6B, 0x01,
            0x83, 0x6E, 0x3A, 0x31,
        };

        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteNode(node)));
    }

    [Fact]
    public void Relationship_EmptyType_IsStructurallyValid()
    {
        var relationship = new PackStreamRelationship(1, 1, 2, string.Empty, EmptyMap(), "r:1", "n:1", "n:2");

        var expected = new byte[]
        {
            0xB8, 0x52, 0x01, 0x01, 0x02,
            0x80,
            0xA0,
            0x83, 0x72, 0x3A, 0x31,
            0x83, 0x6E, 0x3A, 0x31,
            0x83, 0x6E, 0x3A, 0x32,
        };

        Assert.Equal(expected, PackStreamTestHelpers.Encode(w => w.WriteRelationship(relationship)));

        var decoded = Assert.IsType<PackStreamRelationship>(
            PackStreamTestHelpers.Decode(expected, r => r.ReadValue()));
        Assert.Equal(string.Empty, decoded.Type);
    }

    // ---- validation -----------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0xB0, 0x4E })]                        // Node arity 0
    [InlineData(new byte[] { 0xB3, 0x4E, 0x01, 0x90, 0xA0 })]      // Node arity 3
    [InlineData(new byte[] { 0xB4, 0x52, 0x01, 0x01, 0x01, 0x02 })] // Relationship arity 4
    [InlineData(new byte[] { 0xB8, 0x72, 0x01 })]                  // UnboundRelationship arity 8
    public void ReadValue_GraphTagWithWrongArity_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());

    [Theory]
    [InlineData(new byte[] { 0xB4, 0x4E, 0x81, 0x41, 0x90, 0xA0, 0x80 })] // id is a string
    [InlineData(new byte[] { 0xB4, 0x4E, 0x01, 0x01, 0xA0, 0x80 })]       // labels is an integer
    [InlineData(new byte[] { 0xB4, 0x4E, 0x01, 0x91, 0x01, 0xA0, 0x80 })] // labels[0] is an integer
    [InlineData(new byte[] { 0xB4, 0x4E, 0x01, 0x90, 0x90, 0x80 })]       // properties is a list
    [InlineData(new byte[] { 0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x01 })]       // element_id is an integer
    public void ReadNode_IncorrectFieldType_ThrowsFormat(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());

    [Fact]
    public void ReadRelationship_IncorrectFieldType_ThrowsFormat()
    {
        // Field 4 (type) holds an integer instead of a string; all eight fields are present.
        var bytes = new byte[]
        {
            0xB8, 0x52,
            0x01, 0x01, 0x02,
            0x01,
            0xA0,
            0x83, 0x72, 0x3A, 0x31,
            0x83, 0x6E, 0x3A, 0x31,
            0x83, 0x6E, 0x3A, 0x32,
        };

        PackStreamTestHelpers.AssertThrows<PackStreamFormatException>(bytes, r => r.ReadValue());
    }

    [Theory]
    [InlineData(new byte[] { 0xB4, 0x4E })]                              // no fields at all
    [InlineData(new byte[] { 0xB4, 0x4E, 0x01, 0x90 })]                  // missing properties/element_id
    [InlineData(new byte[] { 0xB8, 0x52, 0x01 })]                        // missing remaining fields
    [InlineData(new byte[] { 0xB4, 0x72, 0x01, 0x85 })]                  // truncated type string
    public void ReadValue_TruncatedGraphStructure_ThrowsTruncated(byte[] bytes) =>
        PackStreamTestHelpers.AssertThrows<PackStreamTruncatedException>(bytes, r => r.ReadValue());

    [Fact]
    public void ReadValue_TruncatedNode_PositionReflectsConsumedBytes()
    {
        var reader = new PackStreamReader(new byte[] { 0xB4, 0x4E });

        Assert.Throws<PackStreamTruncatedException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void ReadValue_WrongArity_PositionReflectsConsumedBytes()
    {
        var reader = new PackStreamReader(new byte[] { 0xB0, 0x4E });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void ReadNode_RequiresNestingDepthForItsContainers()
    {
        var bytes = new byte[] { 0xB4, 0x4E, 0x00, 0x90, 0xA0, 0x80 };

        Assert.IsType<PackStreamNode>(
            new PackStreamReader(bytes, new PackStreamLimits(maxDepth: 2)).ReadValue());

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(bytes, new PackStreamLimits(maxDepth: 1)).ReadValue());
    }

    [Fact]
    public void ReadNode_ElementBudgetCoversGraphFields()
    {
        var bytes = new byte[] { 0xB4, 0x4E, 0x00, 0x90, 0xA0, 0x80 };

        Assert.IsType<PackStreamNode>(
            new PackStreamReader(bytes, new PackStreamLimits(maxTotalElements: 5)).ReadValue());

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(bytes, new PackStreamLimits(maxTotalElements: 4)).ReadValue());
    }

    [Fact]
    public void GraphTags_AreReserved_GenericStructureWithNodeTagDecodesAsNode()
    {
        // A generic structure that carries the Node tag and valid Node fields is decoded
        // as a Node, never as an ordinary structure or map.
        var structure = new PackStreamStructure(0x4E, new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamList(new PackStreamValue?[] { new PackStreamString("Person") }),
            new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("Alice")) }),
            new PackStreamString("n:1"),
        });

        var bytes = PackStreamTestHelpers.Encode(w => w.WriteValue(structure));
        Assert.Equal(AliceBytes(), bytes);

        var decoded = Assert.IsType<PackStreamNode>(PackStreamTestHelpers.Decode(bytes, r => r.ReadValue()));
        Assert.Equal("n:1", decoded.ElementId);
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
