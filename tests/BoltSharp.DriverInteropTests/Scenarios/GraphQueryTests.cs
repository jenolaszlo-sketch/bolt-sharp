using BoltSharp.DriverInteropTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

// The legacy numeric Id / StartNodeId / EndNodeId members are obsolete in driver 6.x in favour of
// element ids, but the B5-B specification explicitly requires asserting them where exposed.
#pragma warning disable CS0618

namespace BoltSharp.DriverInteropTests.Scenarios;

/// <summary>
/// B5-B4 — the official driver must materialize the reference graph record into typed
/// <see cref="INode"/>/<see cref="IRelationship"/> values visible to application code, including
/// the deliberate numeric node/relationship id collision (1) and the typed element-id spaces.
/// </summary>
[Collection("B5-B")]
public sealed class GraphQueryTests
{
    private const string GraphQuery = "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b";

    /// <summary>The accepted B5-A golden RECORD bytes (produced by the production encoder).</summary>
    private const string GoldenRecordHex =
        "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31" +
        "B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
        "B44E029186506572736F6EA1846E616D6583426F62836E3A32";

    [Fact]
    [Trait("Task", "B5-B4")]
    public async Task B5B4_01_GraphQuery_OfficialDriverMaterializesTypedValues()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);

        var driver = DriverFactory.Plaintext(server.HostPort);
        INode? alice = null;
        INode? bob = null;
        IRelationship? knows = null;
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync(GraphQuery));

            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            var record = cursor.Current;
            Assert.Equal(new[] { "a", "r", "b" }, record.Keys);

            alice = record["a"].As<INode>();
            knows = record["r"].As<IRelationship>();
            bob = record["b"].As<INode>();

            Assert.False(await DriverFactory.Bounded(cursor.FetchAsync()));
            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.NotNull(alice);
        Assert.NotNull(bob);
        Assert.NotNull(knows);

        // Alice.
        Assert.Equal(1, alice!.Id);
        Assert.Equal("n:1", alice.ElementId);
        Assert.Contains("Person", alice.Labels);
        Assert.Equal("Alice", alice.Properties["name"].As<string>());

        // Bob.
        Assert.Equal(2, bob!.Id);
        Assert.Equal("n:2", bob.ElementId);
        Assert.Contains("Person", bob.Labels);
        Assert.Equal("Bob", bob.Properties["name"].As<string>());

        // The relationship shares numeric id 1 with Alice: typed identity must remain distinct.
        Assert.Equal(1, knows!.Id);
        Assert.Equal("r:1", knows.ElementId);
        Assert.NotEqual(alice.ElementId, knows.ElementId);
        Assert.Equal("KNOWS", knows.Type);
        Assert.Equal(2024, knows.Properties["since"].As<long>());
        Assert.Equal(alice.Id, knows.StartNodeId);
        Assert.Equal(bob.Id, knows.EndNodeId);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);
        Assert.IsAssignableFrom<INode>(alice);
        Assert.IsAssignableFrom<IRelationship>(knows);

        EvidenceWriter.Write("b5b4-01-graph-typed-values", new
        {
            scenario = "B5B4-01",
            title = "Typed INode/IRelationship materialization with distinct identity spaces",
            uri_scheme = "bolt",
            driver_visible = new
            {
                a = new { numeric_id = alice.Id, element_id = alice.ElementId, labels = alice.Labels, name = alice.Properties["name"].As<string>() },
                r = new
                {
                    numeric_id = knows.Id,
                    element_id = knows.ElementId,
                    type = knows.Type,
                    start_element_id = knows.StartNodeElementId,
                    end_element_id = knows.EndNodeElementId,
                    start_numeric_id = knows.StartNodeId,
                    end_numeric_id = knows.EndNodeId,
                    since = knows.Properties["since"].As<long>(),
                },
                b = new { numeric_id = bob.Id, element_id = bob.ElementId, labels = bob.Labels, name = bob.Properties["name"].As<string>() },
            },
            counters = EvidenceWriter.CounterDocument(server.Counters()),
            server_events = EvidenceWriter.EventDocument(server.Events()),
        });
    }

    [Fact]
    [Trait("Task", "B5-B4")]
    public async Task B5B4_02_GraphQuery_WireCapture_RecordMatchesAcceptedGoldenBytes()
    {
        EvidenceWriter.WriteEnvironment();
        await using var server = await B5BServerHost.StartPlaintextAsync(TestCredentials.Verifier);
        await using var tap = await WireTapProxy.StartAsync(server.Endpoint);

        var driver = DriverFactory.Plaintext(tap.HostPort);
        try
        {
            await DriverFactory.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverFactory.Bounded(session.RunAsync(GraphQuery));
            Assert.True(await DriverFactory.Bounded(cursor.FetchAsync()));
            Assert.Equal("n:1", cursor.Current["a"].As<INode>().ElementId);
            await DriverFactory.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        Assert.True(await tap.WaitForClosedConnectionsAsync(1, TimeSpan.FromSeconds(10)));
        var connection = tap.SingleConnection();
        var transcript = WireTranscript.Build(connection);

        var record = Assert.Single(transcript.ServerMessages, m => m.Name == "RECORD");
        Assert.Equal(GoldenRecordHex, record.Hex);
        Assert.Equal("RECORD [3 value(s): Node,Relationship,Node]", record.Description);
        Assert.Contains(
            transcript.ServerMessages,
            m => m.Name == "SUCCESS" && m.Description == "SUCCESS {fields=[a,r,b]}");
        Assert.Equal("B170A0", transcript.ServerMessages.Last().Hex);
        Assert.Null(transcript.ParseError);

        var run = Assert.Single(transcript.ClientMessages, m => m.Name == "RUN");
        Assert.Equal($"RUN \"{GraphQuery}\" parameters=0 entry(ies) extra={{}}", run.Description);

        EvidenceWriter.Write("b5b4-02-graph-wire-capture", new
        {
            scenario = "B5B4-02",
            title = "Graph RECORD byte-identical to the accepted B5-A golden vector",
            uri_scheme = "bolt",
            golden_match = true,
            record_hex = record.Hex,
            record_description = record.Description,
            transcript = transcript.ToDocument(connection),
            counters = EvidenceWriter.CounterDocument(server.Counters()),
        });
    }
}
