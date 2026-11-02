namespace BoltSharp.PackStream;

/// <summary>
/// A Bolt graph Node structure (tag <c>4E</c>, four fields). Numeric IDs and element IDs
/// are preserved independently; numeric IDs are not assumed to be unique across entity types.
/// </summary>
public sealed class PackStreamNode : PackStreamValue
{
    public PackStreamNode(long id, IReadOnlyList<string> labels, PackStreamMap properties, string elementId)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(elementId);

        Id = id;
        Labels = CopyLabels(labels);
        Properties = properties;
        ElementId = elementId;
    }

    /// <summary>Numeric node ID.</summary>
    public long Id { get; }

    /// <summary>Node labels, in order. Never null; individual entries are never null.</summary>
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
