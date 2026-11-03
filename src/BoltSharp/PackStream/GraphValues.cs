using System.Collections.ObjectModel;

namespace BoltSharp.PackStream;

/// <summary>
/// A Bolt graph Node structure (tag <c>4E</c>, four fields). Numeric IDs and element IDs
/// are preserved independently; numeric IDs are not assumed to be unique across entity types.
/// The labels are defensively copied on construction and exposed only through a read-only
/// wrapper (B1-R1, finding F1).
/// </summary>
public sealed class PackStreamNode : PackStreamValue
{
    public PackStreamNode(long id, IReadOnlyList<string> labels, PackStreamMap properties, string elementId)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(elementId);

        Id = id;
        Labels = new ReadOnlyCollection<string>(CopyLabels(labels));
        Properties = properties;
        ElementId = elementId;
    }

    /// <summary>
    /// Internal constructor used by the reader. Takes ownership of the labels array the
    /// reader has just built (never null, no null entries, not mutated or exposed
    /// afterwards), so no defensive copy is required.
    /// </summary>
    internal PackStreamNode(long id, string[] ownedLabels, PackStreamMap properties, string elementId)
    {
        Id = id;
        Labels = new ReadOnlyCollection<string>(ownedLabels);
        Properties = properties;
        ElementId = elementId;
    }

    /// <summary>Numeric node ID.</summary>
    public long Id { get; }

    /// <summary>
    /// Node labels, in order. Never null; individual entries are never null. The returned
    /// collection is a read-only wrapper: casting it back to an array fails, and mutation
    /// attempts throw <see cref="NotSupportedException"/>.
    /// </summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>Node properties. Keys are strings; values may be any supported value.</summary>
    public PackStreamMap Properties { get; }

    /// <summary>Opaque element ID string. Never generated or rewritten by the codec.</summary>
    public string ElementId { get; }

    private static string[] CopyLabels(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var copy = new string[labels.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = labels[i] ?? throw new ArgumentException("Node labels must not be null.", nameof(labels));
        }

        return copy;
    }
}

/// <summary>
/// A Bolt graph Relationship structure (tag <c>52</c>, eight fields). Endpoint numeric IDs
/// and endpoint element IDs are preserved exactly; direction is not modified.
/// </summary>
public sealed class PackStreamRelationship : PackStreamValue
{
    public PackStreamRelationship(
        long id,
        long startNodeId,
        long endNodeId,
        string type,
        PackStreamMap properties,
        string elementId,
        string startNodeElementId,
        string endNodeElementId)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(elementId);
        ArgumentNullException.ThrowIfNull(startNodeElementId);
        ArgumentNullException.ThrowIfNull(endNodeElementId);

        Id = id;
        StartNodeId = startNodeId;
        EndNodeId = endNodeId;
        Type = type;
        Properties = properties;
        ElementId = elementId;
        StartNodeElementId = startNodeElementId;
        EndNodeElementId = endNodeElementId;
    }

    public long Id { get; }

    public long StartNodeId { get; }

    public long EndNodeId { get; }

    public string Type { get; }

    public PackStreamMap Properties { get; }

    public string ElementId { get; }

    public string StartNodeElementId { get; }

    public string EndNodeElementId { get; }
}

/// <summary>
/// A Bolt graph UnboundRelationship structure (tag <c>72</c>, four fields). It carries no
/// endpoints; the codec never invents them.
/// </summary>
public sealed class PackStreamUnboundRelationship : PackStreamValue
{
    public PackStreamUnboundRelationship(long id, string type, PackStreamMap properties, string elementId)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(elementId);

        Id = id;
        Type = type;
        Properties = properties;
        ElementId = elementId;
    }

    public long Id { get; }

    public string Type { get; }

    public PackStreamMap Properties { get; }

    public string ElementId { get; }
}

/// <summary>
/// A Bolt Path structure (tag <c>50</c>, three fields: nodes, unbound relationships and
/// traversal indices). Nodes and relationships are referenced by position, so the same
/// entity may be revisited repeatedly without being duplicated. All three field lists are
/// defensively copied on construction (public constructor) or exclusively owned (internal
/// reader constructor) and exposed only through read-only wrappers (B1-R1, finding F1).
/// </summary>
public sealed class PackStreamPath : PackStreamValue
{
    public PackStreamPath(
        IReadOnlyList<PackStreamNode> nodes,
        IReadOnlyList<PackStreamUnboundRelationship> unboundRelationships,
        IReadOnlyList<long> indices)
        : this(CopyNodes(nodes), CopyUnboundRelationships(unboundRelationships), CopyIndices(indices))
    {
    }

    /// <summary>
    /// Internal constructor used by the reader. It takes ownership of the arrays the reader
    /// has just built, avoiding a second defensive copy.
    /// </summary>
    internal PackStreamPath(PackStreamNode[] nodes, PackStreamUnboundRelationship[] unboundRelationships, long[] indices)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(unboundRelationships);
        ArgumentNullException.ThrowIfNull(indices);

        if (nodes.Length == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodes), nodes.Length, "A Path must contain at least one node.");
        }

        ValidateTraversal(nodes.Length, unboundRelationships.Length, indices);

        Nodes = new ReadOnlyCollection<PackStreamNode>(nodes);
        UnboundRelationships = new ReadOnlyCollection<PackStreamUnboundRelationship>(unboundRelationships);
        Indices = new ReadOnlyCollection<long>(indices);
    }

    /// <summary>
    /// Nodes referenced by the path. Never empty. The returned collection is a read-only
    /// wrapper: casting it back to an array fails, and mutation attempts throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    public IReadOnlyList<PackStreamNode> Nodes { get; }

    /// <summary>
    /// Unbound relationships referenced by the path. They carry no endpoints. The returned
    /// collection is a read-only wrapper: casting it back to an array fails, and mutation
    /// attempts throw <see cref="NotSupportedException"/>.
    /// </summary>
    public IReadOnlyList<PackStreamUnboundRelationship> UnboundRelationships { get; }

    /// <summary>
    /// Traversal indices as they appear on the wire: alternating 1-based relationship
    /// indexes (negative means reverse traversal, zero is invalid) and 0-based node indexes.
    /// The sign and order are preserved exactly. The returned collection is a read-only
    /// wrapper: casting it back to an array fails, and mutation attempts throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    public IReadOnlyList<long> Indices { get; }

    private static void ValidateTraversal(int nodeCount, int relationshipCount, long[] indices)
    {
        if ((indices.Length & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indices), indices.Length, "Path indices must contain an even number of entries.");
        }

        for (var i = 0; i < indices.Length; i += 2)
        {
            var relationshipIndex = indices[i];
            var nodeIndex = indices[i + 1];

            // Relationship indexes are 1-based; negative values mean reverse traversal and
            // zero is invalid. Only comparisons are used, so long.MinValue cannot overflow.
            if (relationshipIndex > 0)
            {
                if (relationshipIndex > relationshipCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(indices), relationshipIndex, "Relationship index is outside the relationship list.");
                }
            }
            else if (relationshipIndex < 0)
            {
                if (relationshipIndex < -relationshipCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(indices), relationshipIndex, "Relationship index is outside the relationship list.");
                }
            }
            else
            {
                throw new ArgumentOutOfRangeException(
                    nameof(indices), relationshipIndex, "Relationship index zero is invalid.");
            }

            // Node indexes are 0-based and nonnegative.
            if (nodeIndex < 0 || nodeIndex >= nodeCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(indices), nodeIndex, "Node index is outside the node list.");
            }
        }
    }

    private static PackStreamNode[] CopyNodes(IReadOnlyList<PackStreamNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var copy = new PackStreamNode[nodes.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = nodes[i] ?? throw new ArgumentException("Path nodes must not be null.", nameof(nodes));
        }

        return copy;
    }

    private static PackStreamUnboundRelationship[] CopyUnboundRelationships(
        IReadOnlyList<PackStreamUnboundRelationship> unboundRelationships)
    {
        ArgumentNullException.ThrowIfNull(unboundRelationships);

        var copy = new PackStreamUnboundRelationship[unboundRelationships.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = unboundRelationships[i]
                ?? throw new ArgumentException(
                    "Path unbound relationships must not be null.", nameof(unboundRelationships));
        }

        return copy;
    }

    private static long[] CopyIndices(IReadOnlyList<long> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        var copy = new long[indices.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = indices[i];
        }

        return copy;
    }
}
