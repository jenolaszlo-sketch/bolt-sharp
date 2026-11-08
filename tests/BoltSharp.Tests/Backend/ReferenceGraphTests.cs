using BoltSharp.Backend;
using BoltSharp.PackStream;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>B3-C2: the deterministic Alice/Bob/KNOWS reference graph fixture.</summary>
public sealed class ReferenceGraphTests
{
    [Fact]
    public void Graph_UsesSpecAndB0BIdentities()
    {
        var graph = ReferenceGraph.Default;

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Single(graph.Relationships);

        var alice = graph.Nodes[0];
        Assert.Equal(1, alice.Id);
        Assert.Equal("n:1", alice.ElementId);
        Assert.Equal(new[] { "Person" }, alice.Labels);
        Assert.Equal("Alice", BoltMessageFieldsTestAccess.GetString(alice.Properties, "name"));

        var bob = graph.Nodes[1];
        Assert.Equal(2, bob.Id);
        Assert.Equal("n:2", bob.ElementId);
        Assert.Equal(new[] { "Person" }, bob.Labels);
        Assert.Equal("Bob", BoltMessageFieldsTestAccess.GetString(bob.Properties, "name"));

        // The relationship deliberately reuses numeric ID 1 (Alice's ID) to catch aliasing bugs.
        var knows = graph.Relationships[0];
        Assert.Equal(1, knows.Id);
        Assert.Equal(1, alice.Id);
        Assert.Equal("r:1", knows.ElementId);
        Assert.Equal("KNOWS", knows.Type);
        Assert.Equal(alice.Id, knows.StartNodeId);
        Assert.Equal(bob.Id, knows.EndNodeId);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);
        Assert.Equal(2024, BoltMessageFieldsTestAccess.GetInteger(knows.Properties, "since"));
    }

    [Fact]
    public void Graph_DefaultIsASingleDeterministicInstance()
    {
        Assert.Same(ReferenceGraph.Default, ReferenceGraph.Default);
        Assert.Equal("Alice", BoltMessageFieldsTestAccess.GetString(ReferenceGraph.Default.Nodes[0].Properties, "name"));
    }

    [Fact]
    public void Graph_IsImmutableAfterConstruction()
    {
        var nodes = new List<PackStreamNode>
        {
            new(7, new[] { "Person" }, BackendTestSupport.Map(("name", new PackStreamString("X"))), "n:7"),
        };
        var relationships = new List<PackStreamRelationship>();
        var graph = new ReferenceGraph(nodes, relationships);

        nodes.Clear();
        relationships.Add(new PackStreamRelationship(9, 1, 2, "KNOWS", BackendTestSupport.Map(), "r:9", "n:1", "n:2"));

        Assert.Single(graph.Nodes);
        Assert.Empty(graph.Relationships);
        Assert.Throws<NotSupportedException>(() => ((IList<PackStreamNode>)graph.Nodes).Add(graph.Nodes[0]));
        Assert.Equal("X", BoltMessageFieldsTestAccess.GetString(graph.Nodes[0].Properties, "name"));
    }

    [Fact]
    public void Graph_FreshInstancesAreValueEqual()
    {
        var first = new ReferenceGraph();
        var second = new ReferenceGraph();

        Assert.Equal(first.Nodes.Count, second.Nodes.Count);
        Assert.Equal(first.Nodes[0].Id, second.Nodes[0].Id);
        Assert.Equal(first.Nodes[0].ElementId, second.Nodes[0].ElementId);
        Assert.Equal(first.Relationships[0].ElementId, second.Relationships[0].ElementId);
        Assert.Equal(first.Relationships[0].StartNodeElementId, second.Relationships[0].StartNodeElementId);
        Assert.Equal(first.Relationships[0].EndNodeElementId, second.Relationships[0].EndNodeElementId);
        Assert.Equal(
            BoltMessageFieldsTestAccess.GetInteger(first.Relationships[0].Properties, "since"),
            BoltMessageFieldsTestAccess.GetInteger(second.Relationships[0].Properties, "since"));
    }
}
