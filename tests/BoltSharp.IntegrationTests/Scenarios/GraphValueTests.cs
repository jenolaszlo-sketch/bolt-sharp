using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

// The numeric Id / StartNodeId / EndNodeId members are obsolete in driver 6.x in favour of element
// ids, but B0-B explicitly verifies that the legacy numeric identity is still reconstructed.
#pragma warning disable CS0618

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T04: graph value reconstruction. The fake responder returns the deterministic synthetic
/// graph (Node A = Alice, Node B = Bob, directed KNOWS). The real driver must reconstruct typed
/// <see cref="INode"/> / <see cref="IRelationship"/> values with correct identities, labels,
/// properties and direction. The reused numeric id 1 on node and relationship is intentional.
/// </summary>
[Collection("B0-B")]
public sealed class GraphValueTests
{
    [Fact]
    [Trait("Task", "B0-B-T04")]
    public async Task T04_SyntheticGraph_RealDriverReconstructsTypedGraph()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(
            ProtocolFixtures.Str("a"), ProtocolFixtures.Str("r"), ProtocolFixtures.Str("b"))));

        var alice = ProtocolFixtures.Node(1, "n:1", new[] { "Person" }, ("name", ProtocolFixtures.Str("Alice")));
        var bob = ProtocolFixtures.Node(2, "n:2", new[] { "Person" }, ("name", ProtocolFixtures.Str("Bob")));
        var knows = ProtocolFixtures.Relationship(
            1, 1, 2, "KNOWS", "r:1", "n:1", "n:2", ("since", ProtocolFixtures.Int(2024)));
        script.On("PULL").Record(alice, knows, bob).Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        INode? nodeA = null;
        IRelationship? rel = null;
        INode? nodeB = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("MATCH (a)-[r]->(b) RETURN a,r,b"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            var record = cursor.Current;
            nodeA = record["a"].As<INode>();
            rel = record["r"].As<IRelationship>();
            nodeB = record["b"].As<INode>();
            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        EvidenceSink.WriteJson("t04-graph-values.json", ScenarioSupport.CaptureDocument("T04", server.Capture));

        Assert.NotNull(nodeA);
        Assert.NotNull(rel);
        Assert.NotNull(nodeB);

        // Distinct typed identities despite the reused numeric id 1.
        Assert.Equal(1, nodeA!.Id);
        Assert.Equal(2, nodeB!.Id);
        Assert.Equal(1, rel!.Id);
        Assert.IsAssignableFrom<INode>(nodeA);
        Assert.IsAssignableFrom<IRelationship>(rel);

        Assert.Equal("n:1", nodeA.ElementId);
        Assert.Equal("n:2", nodeB.ElementId);
        Assert.Equal("r:1", rel.ElementId);
        Assert.Contains("Person", nodeA.Labels);
        Assert.Contains("Person", nodeB.Labels);
        Assert.Equal("Alice", nodeA.Properties["name"].As<string>());
        Assert.Equal("Bob", nodeB.Properties["name"].As<string>());

        // Correct direction and endpoints.
        Assert.Equal("KNOWS", rel.Type);
        Assert.Equal(nodeA.Id, rel.StartNodeId);
        Assert.Equal(nodeB.Id, rel.EndNodeId);
        Assert.Equal("n:1", rel.StartNodeElementId);
        Assert.Equal("n:2", rel.EndNodeElementId);
        Assert.Equal(2024, rel.Properties["since"].As<long>());
    }
}
