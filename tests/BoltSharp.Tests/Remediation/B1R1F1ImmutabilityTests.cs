using System.Buffers;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Remediation;

/// <summary>
/// B1-R1 remediation regressions for finding F1 (MODERATE) of
/// docs/reviews/B1-Q1-packstream-qualification.md: immutability was not enforced on decoded
/// values. Every public value representation must establish defensively owned state and
/// must not expose mutable backing storage. Both reviewer mutation vectors (array casts and
/// <c>MemoryMarshal.TryGetArray</c>) are replayed here and must fail to change stored
/// values.
/// </summary>
public sealed class B1R1F1ImmutabilityTests
{
    // ---- reviewer mutation vector 1: array casts -------------------------------

    [Fact]
    public void List_Items_ArrayCast_CannotChangeStoredValue()
    {
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamString("two") });
        var bytes = Encode(v => v.WriteValue(list));

        Assert.Throws<InvalidCastException>(
            () => ((PackStreamValue?[])list.Items)[0] = new PackStreamInteger(99));

        Assert.Equal(1, Assert.IsType<PackStreamInteger>(list.Items[0]).Value);
        Assert.Equal("two", Assert.IsType<PackStreamString>(list.Items[1]).Value);
        Assert.Equal(bytes, Encode(v => v.WriteValue(list)));
    }

    [Fact]
    public void List_Items_IListMutation_AttemptsThrow()
    {
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1) });

        var view = Assert.IsAssignableFrom<IList<PackStreamValue?>>(list.Items);
        Assert.Throws<NotSupportedException>(() => view.Add(new PackStreamInteger(2)));
        Assert.Throws<NotSupportedException>(() => view[0] = new PackStreamInteger(9));
        Assert.Throws<NotSupportedException>(() => view.RemoveAt(0));
        Assert.Single(list.Items);
    }

    [Fact]
    public void Map_Entries_ArrayCast_CannotChangeStoredValue()
    {
        var map = new PackStreamMap(
            new[] { new KeyValuePair<string, PackStreamValue?>("a", new PackStreamInteger(1)) });
        var bytes = Encode(v => v.WriteValue(map));

        Assert.Throws<InvalidCastException>(
            () => ((KeyValuePair<string, PackStreamValue?>[])map.Entries)[0] =
                new KeyValuePair<string, PackStreamValue?>("a", new PackStreamInteger(99)));

        Assert.Equal(1, Assert.IsType<PackStreamInteger>(map.Entries[0].Value).Value);
        Assert.Equal(bytes, Encode(v => v.WriteValue(map)));
    }

    [Fact]
    public void Map_Entries_IListMutation_AttemptsThrow()
    {
        var map = new PackStreamMap(
            new[] { new KeyValuePair<string, PackStreamValue?>("a", new PackStreamInteger(1)) });

        var view = Assert.IsAssignableFrom<IList<KeyValuePair<string, PackStreamValue?>>>(map.Entries);
        Assert.Throws<NotSupportedException>(
            () => view.Add(new KeyValuePair<string, PackStreamValue?>("b", new PackStreamInteger(2))));
        Assert.Throws<NotSupportedException>(
            () => view[0] = new KeyValuePair<string, PackStreamValue?>("a", new PackStreamInteger(9)));
        Assert.Single(map.Entries);
    }

    [Fact]
    public void Structure_Fields_ArrayCast_CannotChangeStoredValue()
    {
        var structure = new PackStreamStructure(3, new PackStreamValue?[] { new PackStreamInteger(1), null });
        var bytes = Encode(v => v.WriteValue(structure));

        Assert.Throws<InvalidCastException>(
            () => ((PackStreamValue?[])structure.Fields)[0] = new PackStreamInteger(99));

        Assert.Equal(1, Assert.IsType<PackStreamInteger>(structure.Fields[0]).Value);
        Assert.Null(structure.Fields[1]);
        Assert.Equal(bytes, Encode(v => v.WriteValue(structure)));
    }

    [Fact]
    public void Node_Labels_ArrayCast_CannotChangeStoredNode()
    {
        var node = new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1");
        var bytes = Encode(v => v.WriteNode(node));

        Assert.Throws<InvalidCastException>(() => ((string[])node.Labels)[0] = "MUTATED");

        Assert.Equal("Person", node.Labels[0]);
        Assert.Equal(bytes, Encode(v => v.WriteNode(node)));
    }

    [Fact]
    public void Node_Labels_IListMutation_AttemptsThrow()
    {
        var node = new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1");

        var view = Assert.IsAssignableFrom<ICollection<string>>(node.Labels);
        Assert.Throws<NotSupportedException>(() => view.Add("Evil"));
        Assert.Throws<NotSupportedException>(() => view.Clear());
        Assert.Single(node.Labels);
    }

    [Fact]
    public void Path_Collections_ArrayCasts_CannotChangeStoredPath()
    {
        var path = ValidPath();
        var bytes = Encode(v => v.WritePath(path));

        Assert.Throws<InvalidCastException>(() => ((PackStreamNode[])path.Nodes)[0] = OtherNode());
        Assert.Throws<InvalidCastException>(
            () => ((PackStreamUnboundRelationship[])path.UnboundRelationships)[0] = OtherRelationship());
        Assert.Throws<InvalidCastException>(() => ((long[])path.Indices)[0] = 42);

        Assert.Equal(1, path.Nodes[0].Id);
        Assert.Equal("r:1", path.UnboundRelationships[0].ElementId);
        Assert.Equal(new long[] { 1, 0 }, path.Indices);
        Assert.Equal(bytes, Encode(v => v.WritePath(path)));
    }

    // ---- constructor-input mutation (mutation after construction) --------------

    [Fact]
    public void ConstructorInputMutation_DoesNotChangeEncodedOutput()
    {
        var labels = new List<string> { "Person" };
        var entries = new List<KeyValuePair<string, PackStreamValue?>>
        {
            new("name", new PackStreamString("Alice")),
        };
        var items = new List<PackStreamValue?> { new PackStreamInteger(1) };
        var fields = new List<PackStreamValue?> { new PackStreamInteger(1), new PackStreamInteger(2) };
        var nodes = new List<PackStreamNode> { new(1, new[] { "Person" }, EmptyMap(), "n:1") };
        var relationships = new List<PackStreamUnboundRelationship> { new(1, "KNOWS", EmptyMap(), "r:1") };
        var indices = new List<long> { 1, 0 };

        var node = new PackStreamNode(1, labels, new PackStreamMap(entries), "n:1");
        var map = new PackStreamMap(entries);
        var list = new PackStreamList(items);
        var structure = new PackStreamStructure(7, fields);
        var path = new PackStreamPath(nodes, relationships, indices);

        var expected = Encode(v =>
        {
            v.WriteValue(node);
            v.WriteValue(map);
            v.WriteValue(list);
            v.WriteValue(structure);
            v.WriteValue(path);
        });

        // Mutate every constructor input after construction.
        labels[0] = "MUTATED";
        labels.Add("MORE");
        entries[0] = new("name", new PackStreamString("MUTATED"));
        entries.Add(new("extra", new PackStreamInteger(9)));
        items[0] = new PackStreamInteger(99);
        items.Add(null);
        fields[0] = null;
        fields.Add(null);
        nodes[0] = OtherNode();
        nodes.Add(OtherNode());
        relationships[0] = OtherRelationship();
        relationships.Add(OtherRelationship());
        indices[0] = -1;
        indices.Add(2);

        Assert.Equal(expected, Encode(v =>
        {
            v.WriteValue(node);
            v.WriteValue(map);
            v.WriteValue(list);
            v.WriteValue(structure);
            v.WriteValue(path);
        }));
    }

    // ---- reviewer mutation vector 2: MemoryMarshal.TryGetArray -----------------

    [Fact]
    public void Bytes_TryGetArrayMutation_CannotChangeStoredValue()
    {
        var value = new PackStreamBytes(new byte[] { 0x01, 0x02, 0x03 });

        Assert.True(MemoryMarshal.TryGetArray(value.Value, out var segment));
        segment.Array![segment.Offset] = 0xFF;
        segment.Array![segment.Offset + 2] = 0xFF;

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, value.Value.ToArray());
        Assert.Equal(
            new byte[] { 0xCC, 0x03, 0x01, 0x02, 0x03 },
            Encode(v => v.WriteValue(value)));
    }

    [Fact]
    public void Bytes_NestedInsideAnotherValue_CannotBeCorrupted()
    {
        var inner = new PackStreamBytes(new byte[] { 0xDE, 0xAD });
        var map = new PackStreamMap(
            new[]
            {
                new KeyValuePair<string, PackStreamValue?>("payload", inner),
                new KeyValuePair<string, PackStreamValue?>("n", new PackStreamInteger(7)),
            });
        var bytes = Encode(v => v.WriteValue(map));

        // Mutate the nested byte value through its public memory exposure.
        Assert.True(MemoryMarshal.TryGetArray(inner.Value, out var segment));
        segment.Array![segment.Offset] = 0x00;

        // The stored value, and the encoding of the containing map, are unchanged.
        Assert.Equal(new byte[] { 0xDE, 0xAD }, inner.Value.ToArray());
        Assert.Equal(bytes, Encode(v => v.WriteValue(map)));
    }

    [Fact]
    public void Bytes_MutationOfGetterResult_DoesNotAffectSecondRead()
    {
        var value = new PackStreamBytes(new byte[] { 0x0A, 0x0B });

        var first = value.Value;
        Assert.True(MemoryMarshal.TryGetArray(first, out var segment));
        segment.Array![segment.Offset] = 0xFF;

        Assert.Equal(0x0A, value.Value.ToArray()[0]);
    }

    // ---- decoded values have equivalent ownership guarantees -------------------

    [Fact]
    public void DecodedValues_HaveEquivalentOwnershipGuarantees()
    {
        var source = new PackStreamList(new PackStreamValue?[]
        {
            new PackStreamNode(1, new[] { "Person" }, MapWithOneEntry(), "n:1"),
            new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("b", new PackStreamBytes(new byte[] { 0x0B })) }),
            new PackStreamStructure(3, new PackStreamValue?[] { new PackStreamInteger(5) }),
            ValidPath(),
        });

        var bytes = Encode(v => v.WriteValue(source));
        var decoded = Assert.IsType<PackStreamList>(new PackStreamReader(bytes).ReadValue());

        // The same attack vectors fail against decoded instances.
        Assert.Throws<InvalidCastException>(() => ((PackStreamValue?[])decoded.Items)[0] = null);

        var decodedNode = Assert.IsType<PackStreamNode>(decoded.Items[0]);
        Assert.Throws<InvalidCastException>(() => ((string[])decodedNode.Labels)[0] = "MUTATED");
        Assert.Throws<InvalidCastException>(
            () => ((KeyValuePair<string, PackStreamValue?>[])decodedNode.Properties.Entries)[0] = default);

        var decodedMap = Assert.IsType<PackStreamMap>(decoded.Items[1]);
        Assert.Throws<InvalidCastException>(() => ((KeyValuePair<string, PackStreamValue?>[])decodedMap.Entries)[0] = default);

        var decodedBytes = Assert.IsType<PackStreamBytes>(decodedMap.Entries[0].Value);
        Assert.True(MemoryMarshal.TryGetArray(decodedBytes.Value, out var segment));
        segment.Array![segment.Offset] = 0x00;

        var decodedStructure = Assert.IsType<PackStreamStructure>(decoded.Items[2]);
        Assert.Throws<InvalidCastException>(() => ((PackStreamValue?[])decodedStructure.Fields)[0] = null);

        var decodedPath = Assert.IsType<PackStreamPath>(decoded.Items[3]);
        Assert.Throws<InvalidCastException>(() => ((PackStreamNode[])decodedPath.Nodes)[0] = null!);
        Assert.Throws<InvalidCastException>(() => ((long[])decodedPath.Indices)[0] = 42);

        // Re-encoding the decoded tree still produces the original bytes.
        Assert.Equal(bytes, Encode(v => v.WriteValue(decoded)));
    }

    [Fact]
    public void ReadBytes_ConvenienceApi_RemainsCallerOwned()
    {
        // F1 deliberately preserves the different ownership semantics of ReadBytes():
        // it returns a fresh byte[] that the caller owns and may freely mutate.
        var reader = new PackStreamReader(new byte[] { 0xCC, 0x03, 0x01, 0x02, 0x03 });
        var bytes = reader.ReadBytes();

        bytes[0] = 0xFF;
        Assert.Equal(0xFF, bytes[0]);
    }

    // ---- helpers ---------------------------------------------------------------

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    private static PackStreamMap MapWithOneEntry() =>
        new(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("Alice")) });

    private static PackStreamNode OtherNode() =>
        new(9, new[] { "Other" }, EmptyMap(), "n:9");

    private static PackStreamUnboundRelationship OtherRelationship() =>
        new(9, "OTHER", EmptyMap(), "r:9");

    private static PackStreamPath ValidPath() => new(
        new[] { new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1") },
        new[] { new PackStreamUnboundRelationship(1, "KNOWS", EmptyMap(), "r:1") },
        new long[] { 1, 0 });

    private static byte[] Encode(Action<PackStreamWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        write(new PackStreamWriter(buffer));
        return buffer.WrittenSpan.ToArray();
    }
}
