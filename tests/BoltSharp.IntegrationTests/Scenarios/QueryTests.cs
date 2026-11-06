using BoltSharp.IntegrationTests.Infrastructure;
using BoltSharp.PackStream;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T03: basic read-only query. Proves the real driver returns a scalar value from an authentic
/// controlled network interaction, and captures the exact RUN/PULL requests it sends.
/// </summary>
[Collection("B0-B")]
public sealed class QueryTests
{
    [Fact]
    [Trait("Task", "B0-B-T03")]
    public async Task T03_ReturnScalar_RealDriverReconstructsValue()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(42))
            .Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long observed = -1;
        IResultSummary? summary = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            observed = cursor.Current["answer"].As<long>();
            summary = await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t03-query-scalar.json", ScenarioSupport.CaptureDocument("T03", capture));

        Assert.Equal(42, observed);
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Counters.NodesCreated + summary.Counters.PropertiesSet);

        var run = Assert.Single(capture.Requests, r => r.Name == "RUN");
        var query = (run.Structure.Fields.ElementAtOrDefault(0) as PackStreamString)?.Value;
        Assert.Equal("RETURN 42 AS answer", query);
        Assert.Contains(capture.Requests, r => r.Name == "PULL");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
        // Cursor/session disposal must not require an unscripted interaction.
        Assert.Contains(capture.Frames, f => f.Name == "RECORD");
    }
}
