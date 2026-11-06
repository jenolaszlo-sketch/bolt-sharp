using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T06: failure and recovery. A legal protocol FAILURE, an unexpected socket termination and
/// an incomplete response must all surface accurately and must never be reported as a clean end.
/// </summary>
[Collection("B0-B")]
public sealed class FailureRecoveryTests
{
    [Fact]
    [Trait("Task", "B0-B-T06")]
    public async Task T06_ProtocolFailure_IsSurfacedAccurately()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Failure(
            ("gql_status", ProtocolFixtures.Str("42001")),
            ("neo4j_code", ProtocolFixtures.Str("Neo.ClientError.Statement.SyntaxError")),
            ("description", ProtocolFixtures.Str("Invalid input for the deliberately unsupported query.")),
            ("diagnostic_record", ProtocolFixtures.Map()));
        // After a FAILURE the connection is in the FAILED state: subsequent pipelined requests are
        // answered with IGNORED until a RESET returns the connection to READY (Bolt server states).
        script.On("PULL").Ignored();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            var session = driver.AsyncSession();
            try
            {
                var cursor = await DriverHarness.Bounded(session.RunAsync("NOT A SUPPORTED QUERY"));
                await DriverHarness.Bounded(cursor.ConsumeAsync());
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // The protocol failure can also surface when the driver drains the failed pipeline on
            // session disposal; capture it rather than letting it escape.
            try
            {
                await DriverHarness.Bounded(session.DisposeAsync().AsTask());
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
        finally
        {
            try
            {
                await driver.DisposeAsync();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t06-protocol-failure.json", ScenarioSupport.CaptureDocument("T06-failure", capture));

        Assert.NotNull(failure);
        Assert.IsAssignableFrom<Neo4jException>(failure);
        Assert.Contains(capture.Frames, f => f.Name == "FAILURE");
        // The pipelined PULL after a FAILURE must be IGNORED, not answered with data.
        Assert.Contains(capture.Frames, f => f.Name == "IGNORED");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    [Fact]
    [Trait("Task", "B0-B-T06")]
    public async Task T06_UnexpectedSocketTermination_IsNotTreatedAsCleanEnd()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("n"))));
        // Close the connection abruptly instead of answering PULL.
        script.On("PULL").Close();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            await DriverHarness.Bounded(cursor.FetchAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t06-socket-termination.json", ScenarioSupport.CaptureDocument("T06-termination", capture));

        Assert.NotNull(failure);
        // The outcome must be an error, never a silent clean completion.
        Assert.DoesNotContain(capture.Frames, f => f.Name == "RECORD");
        Assert.Contains(capture.Requests, r => r.Name == "PULL");
    }

    [Fact]
    [Trait("Task", "B0-B-T06")]
    public async Task T06_IncompleteResponse_IsSurfacedAsFailure()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("n"))));
        // Announce a 5-byte chunk but send only 2 bytes, then close: a truncated frame.
        script.On("PULL").Raw(0x00, 0x05, 0x41, 0x42).Close();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN n"));
            await DriverHarness.Bounded(cursor.FetchAsync());
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t06-incomplete-response.json", ScenarioSupport.CaptureDocument("T06-incomplete", capture));

        Assert.NotNull(failure);
        Assert.DoesNotContain(capture.Frames, f => f.Name == "RECORD");
    }
}
