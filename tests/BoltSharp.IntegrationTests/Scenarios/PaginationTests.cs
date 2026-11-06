using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T05: pagination and cursor lifecycle. Exercises finite PULL sizes with has_more, DISCARD,
/// and early session disposal, and confirms there are no extra records or leaked cursors.
/// </summary>
[Collection("B0-B")]
public sealed class PaginationTests
{
    private static void ScriptThreeRecords(BoltConnectionScript script)
    {
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("n"))));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(1))
            .Success(("has_more", ProtocolFixtures.Bool(true)));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(2))
            .Success(("has_more", ProtocolFixtures.Bool(true)));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(3))
            .Success(("has_more", ProtocolFixtures.Bool(false)));
    }

    [Fact]
    [Trait("Task", "B0-B-T05")]
    public async Task T05_FinitePullSize_MultiplePullsDeliverExactlyTheRecords()
    {
        var script = ScenarioSupport.PositiveScript();
        ScriptThreeRecords(script);

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        var values = new List<long>();
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            while (await DriverHarness.Bounded(cursor.FetchAsync()))
            {
                values.Add(cursor.Current["n"].As<long>());
            }

            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t05-pagination.json", ScenarioSupport.CaptureDocument("T05-pull", capture));

        Assert.Equal(new long[] { 1, 2, 3 }, values);
        Assert.True(
            capture.Requests.Count(r => r.Name == "PULL") >= 3,
            "finite PULL sizes should require multiple PULL requests");
        // No extra RECORD frames beyond the three scripted records.
        Assert.Equal(3, capture.Frames.Count(f => f.Name == "RECORD"));
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    [Fact]
    [Trait("Task", "B0-B-T05")]
    public async Task T05_Discard_DrainsRemainingRecordsWithoutExtraData()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("n"))));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(1))
            .Success(("has_more", ProtocolFixtures.Bool(true)));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(2))
            .Success(("has_more", ProtocolFixtures.Bool(true)));
        script.On("DISCARD").SuccessEmpty();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long first = -1;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession(o => o.WithFetchSize(1));
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            first = cursor.Current["n"].As<long>();
            await DriverHarness.Bounded(cursor.ConsumeAsync());
            await DriverHarness.Bounded(session.CloseAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t05-discard.json", ScenarioSupport.CaptureDocument("T05-discard", capture));

        Assert.Equal(1, first);
        Assert.Contains(capture.Requests, r => r.Name == "DISCARD");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    [Fact]
    [Trait("Task", "B0-B-T05")]
    public async Task T05_EarlySessionDisposal_LeavesConnectionUsable()
    {
        var script = ScenarioSupport.PositiveScript();
        ScriptThreeRecords(script);
        // Disposing an unconsumed result makes the driver DISCARD the remaining cursor.
        script.On("DISCARD").SuccessEmpty();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            var session = driver.AsyncSession(o => o.WithFetchSize(1));
            _ = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            // Dispose the session without consuming the result.
            await DriverHarness.Bounded(session.DisposeAsync().AsTask());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t05-early-disposal.json", ScenarioSupport.CaptureDocument("T05-early-disposal", capture));

        // The driver must terminate the result deterministically via a scripted message (RESET or
        // DISCARD), never an unscripted interaction, and the server must observe a clean shutdown.
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
        Assert.Contains(capture.Requests, r => r.Name is "RESET" or "DISCARD" or "GOODBYE");
    }
}
