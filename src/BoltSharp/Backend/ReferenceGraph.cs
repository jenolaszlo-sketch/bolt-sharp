using System.Collections.ObjectModel;

using BoltSharp.PackStream;

namespace BoltSharp.Backend;

/// <summary>
/// The immutable, deterministic reference graph used by the B3-C reference backend:
/// <c>(Alice:Person)-[:KNOWS]-&gt;(Bob:Person)</c>.
/// <para>
/// Identities and values follow <c>docs/boltsharp-spec.md</c> §7 and the retained B0-B t04
/// fixture (independently decoded 23/23 in B0-B-Q1): node IDs 1 and 2 with element IDs
/// <c>n:1</c>/<c>n:2</c>, relationship ID 1 (deliberately equal to Alice's numeric ID to catch
/// node/relationship aliasing bugs) with element ID <c>r:1</c>, property <c>since = 2024</c>,
/// and start/end element IDs <c>n:1</c>/<c>n:2</c>.
/// </para>
/// <para>
/// The graph is immutable after construction: graph values are B1 immutable types, the node and
/// relationship collections are read-only wrappers over private copies, and no mutation API
/// exists. The same <see cref="Default"/> instance is safe to share between operations; every
/// operation reads it without modifying it.
/// </para>
/// </summary>
public sealed class ReferenceGraph
{
    /// <summary>Alice's numeric node ID (B0-B t04 / spec §7).</summary>
    public const long AliceId = 1;

    /// <summary>Bob's numeric node ID.</summary>
    public const long BobId = 2;

    /// <summary>The KNOWS relationship's numeric ID (1, equal to Alice's node ID by design).</summary>
    public const long KnowsRelationshipId = 1;

    /// <summary>Alice's element ID.</summary>
    public const string AliceElementId = "n:1";

    /// <summary>Bob's element ID.</summary>
    public const string BobElementId = "n:2";

    /// <summary>The KNOWS relationship's element ID.</summary>
    public const string KnowsElementId = "r:1";

    /// <summary>The single node label used by the fixture.</summary>
    public const string PersonLabel = "Person";

    /// <summary>The single relationship type used by the fixture.</summary>
    public const string KnowsType = "KNOWS";

    /// <summary>The node property key holding the display name.</summary>
    public const string NameProperty = "name";

    /// <summary>The relationship property key retained from the B0-B fixture.</summary>
    public const string SinceProperty = "since";

    /// <summary>The relationship property value retained from the B0-B fixture.</summary>
    public const long SinceValue = 2024;

    private static readonly ReferenceGraph DefaultGraph = new();

    /// <summary>The canonical reference graph instance (deterministic, immutable, shared).</summary>
    public static ReferenceGraph Default => DefaultGraph;

    /// <summary>Creates the canonical reference graph (Alice, Bob, Alice-KNOWS-&gt;Bob).</summary>
    public ReferenceGraph()
        : this(CreateDefaultNodes(), CreateDefaultRelationships())
    {
    }

    /// <summary>
    /// Creates a graph from explicit nodes and relationships. Defensive copies are taken, so the
    /// graph cannot be changed through the caller's collections afterwards. This overload exists
    /// for testing; production consumers use <see cref="Default"/>.
    /// </summary>
    public ReferenceGraph(
        IReadOnlyList<PackStreamNode> nodes,
        IReadOnlyList<PackStreamRelationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(relationships);
        Nodes = new ReadOnlyCollection<PackStreamNode>(CopyNodes(nodes));
        Relationships = new ReadOnlyCollection<PackStreamRelationship>(CopyRelationships(relationships));
    }

    /// <summary>All nodes, in the deterministic fixture order (Alice, then Bob).</summary>
    public IReadOnlyList<PackStreamNode> Nodes { get; }

    /// <summary>All relationships, in the deterministic fixture order.</summary>
    public IReadOnlyList<PackStreamRelationship> Relationships { get; }

    private static PackStreamNode[] CreateDefaultNodes() => new[]
    {
        new PackStreamNode(
            AliceId,
            new[] { PersonLabel },
            Map((NameProperty, new PackStreamString("Alice"))),
            AliceElementId),
        new PackStreamNode(
            BobId,
            new[] { PersonLabel },
            Map((NameProperty, new PackStreamString("Bob"))),
            BobElementId),
    };

    private static PackStreamRelationship[] CreateDefaultRelationships() => new[]
    {
        new PackStreamRelationship(
            KnowsRelationshipId,
            AliceId,
            BobId,
            KnowsType,
            Map((SinceProperty, new PackStreamInteger(SinceValue))),
            KnowsElementId,
            AliceElementId,
            BobElementId),
    };

    private static PackStreamMap Map(params (string Key, PackStreamValue? Value)[] entries)
    {
        var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
        foreach (var (key, value) in entries)
        {
            list.Add(new KeyValuePair<string, PackStreamValue?>(key, value));
        }

        return new PackStreamMap(list);
    }

    private static PackStreamNode[] CopyNodes(IReadOnlyList<PackStreamNode> nodes)
    {
        var copy = new PackStreamNode[nodes.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = nodes[i] ?? throw new ArgumentException("Graph nodes must not be null.", nameof(nodes));
        }

        return copy;
    }

    private static PackStreamRelationship[] CopyRelationships(IReadOnlyList<PackStreamRelationship> relationships)
    {
        var copy = new PackStreamRelationship[relationships.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = relationships[i]
                ?? throw new ArgumentException("Graph relationships must not be null.", nameof(relationships));
        }

        return copy;
    }
}
