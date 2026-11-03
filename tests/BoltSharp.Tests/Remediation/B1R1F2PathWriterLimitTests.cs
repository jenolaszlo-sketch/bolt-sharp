using System.Buffers;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Remediation;

/// <summary>
/// B1-R1 remediation regressions for finding F2 (MODERATE) of
/// docs/reviews/B1-Q1-packstream-qualification.md: WritePath bypassed MaxListItems, so the
/// writer could emit a Path that its own reader rejects under identical limits. All three
/// Path field lists are now bounded on write, symmetrically with the reader.
/// </summary>
public sealed class B1R1F2PathWriterLimitTests
{
    private static readonly PackStreamLimits TightLimits = new(maxListItems: 4);

    // ---- boundaries: accepted at the configured limit ---------------------------

    [Fact]
    public void WritePath_NodeCount_AtBoundary_IsAccepted()
    {
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), TightLimits);

        writer.WritePath(new PackStreamPath(Nodes(4), Relationships(2), new long[] { 1, 0, -1, 3 }));
    }

    [Fact]
    public void WritePath_RelationshipCount_AtBoundary_IsAccepted()
    {
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), TightLimits);

        writer.WritePath(new PackStreamPath(Nodes(1), Relationships(4), Array.Empty<long>()));
    }

    [Fact]
    public void WritePath_IndexCount_AtBoundary_IsAccepted()
    {
        var writer = new PackStreamWriter(new ArrayBufferWriter<byte>(), TightLimits);

        writer.WritePath(new PackStreamPath(Nodes(4), Relationships(2), new long[] { 1, 0, -1, 3 }));
    }

    // ---- just above the boundary: rejected before any byte is emitted -----------

    [Fact]
    public void WritePath_NodeCount_AboveBoundary_IsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, TightLimits);
        var path = new PackStreamPath(Nodes(5), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WritePath_RelationshipCount_AboveBoundary_IsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, TightLimits);
        var path = new PackStreamPath(Nodes(1), Relationships(5), Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WritePath_IndexCount_AboveBoundary_IsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, TightLimits);

        // Path indices must be even in number (Path traversal pairs), so the nearest
        // constructible count above the boundary of 4 is 6, not 5.
        var path = new PackStreamPath(Nodes(4), Relationships(2), new long[] { 1, 0, 1, 1, 2, 2 });

        Assert.Throws<PackStreamFormatException>(() => writer.WritePath(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    // ---- writer/reader symmetry -------------------------------------------------

    [Fact]
    public void WritePath_ThenReadPath_WithIdenticalLimits_RoundTrips()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, TightLimits);
        var path = new PackStreamPath(Nodes(4), Relationships(2), new long[] { 1, 0, -1, 3 });

        writer.WritePath(path);

        var decoded = Assert.IsType<PackStreamPath>(
            new PackStreamReader(buffer.WrittenSpan.ToArray(), TightLimits).ReadValue());

        Assert.Equal(path.Nodes.Count, decoded.Nodes.Count);
        Assert.Equal(path.UnboundRelationships.Count, decoded.UnboundRelationships.Count);
        Assert.Equal(path.Indices, decoded.Indices);
        Assert.Equal(
            buffer.WrittenSpan.ToArray(),
            EncodeWith(TightLimits, v => v.WritePath(decoded)));
    }

    [Fact]
    public void WriteValue_PathLimits_EnforcedThroughGenericEntryPoint()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, TightLimits);
        var path = new PackStreamPath(Nodes(5), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>());

        Assert.Throws<PackStreamFormatException>(() => writer.WriteValue(path));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WritePath_DefaultLimits_ExistingPathsUnaffected()
    {
        // The B1-D2 golden path fixture (official repeated-relationship example) continues
        // to write and read under the default limits.
        var path = SpecExamplePath();

        var bytes = EncodeWith(PackStreamLimits.Default, v => v.WritePath(path));
        var decoded = Assert.IsType<PackStreamPath>(
            new PackStreamReader(bytes, PackStreamLimits.Default).ReadValue());

        Assert.Equal(3, decoded.Nodes.Count);
        Assert.Equal(2, decoded.UnboundRelationships.Count);
        Assert.Equal(new long[] { 1, 1, 1, 0, -2, 1 }, decoded.Indices);
    }

    // ---- helpers ---------------------------------------------------------------

    private static PackStreamNode[] Nodes(int count)
    {
        var nodes = new PackStreamNode[count];
        for (var i = 0; i < count; i++)
        {
            nodes[i] = new PackStreamNode(
                i + 1,
                Array.Empty<string>(),
                new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>()),
                "n:" + (i + 1));
        }

        return nodes;
    }

    private static PackStreamUnboundRelationship[] Relationships(int count)
    {
        var relationships = new PackStreamUnboundRelationship[count];
        for (var i = 0; i < count; i++)
        {
            relationships[i] = new PackStreamUnboundRelationship(
                i + 1,
                "KNOWS",
                new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>()),
                "r:" + (i + 1));
        }

        return relationships;
    }

    private static PackStreamPath SpecExamplePath()
    {
        // (42)-[1000]->(69)-[1000]->(42)<-[1001]-(1), the official specification example.
        return new PackStreamPath(
            new[]
            {
                new PackStreamNode(42, Array.Empty<string>(), EmptyMap(), "n:42"),
                new PackStreamNode(69, Array.Empty<string>(), EmptyMap(), "n:69"),
                new PackStreamNode(1, Array.Empty<string>(), EmptyMap(), "n:1"),
            },
            new[]
            {
                new PackStreamUnboundRelationship(1000, "KNOWS", EmptyMap(), "r:1000"),
                new PackStreamUnboundRelationship(1001, "KNOWS", EmptyMap(), "r:1001"),
            },
            new long[] { 1, 1, 1, 0, -2, 1 });
    }

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    private static byte[] EncodeWith(PackStreamLimits limits, Action<PackStreamWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        write(new PackStreamWriter(buffer, limits));
        return buffer.WrittenSpan.ToArray();
    }
}
