using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T07: transport fragmentation. The same valid byte stream is delivered with different write
/// segmentation (whole, per-byte, fixed small chunks, coalesced messages, split handshake). A TCP
/// write boundary is never a Bolt message boundary, so observable results must be identical.
/// </summary>
[Collection("B0-B")]
public sealed class FragmentationTests
{
    private static async Task<long> RunScalarQueryAsync(WriteSplitMode splitMode, int chunkSize)
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        script.On("PULL")
            .Split(splitMode, chunkSize)
            .Record(ProtocolFixtures.Int(42))
            .Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            // The handshake response is always written whole: see
            // T07_HandshakeFragmentation_DriverRejectsIt for the observed driver limitation.
            HandshakeSplit = WriteSplitMode.Whole,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long value;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            value = cursor.Current["answer"].As<long>();
            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        EvidenceSink.WriteJson(
            $"t07-fragmentation-{splitMode.ToString().ToLowerInvariant()}-{chunkSize}.json",
            ScenarioSupport.CaptureDocument($"T07-{splitMode}-{chunkSize}", server.Capture));
        return value;
    }

    [Fact]
    [Trait("Task", "B0-B-T07")]
    public async Task T07_WholeWrites_Baseline()
    {
        Assert.Equal(42, await RunScalarQueryAsync(WriteSplitMode.Whole, 7));
    }

    [Fact]
    [Trait("Task", "B0-B-T07")]
    public async Task T07_PerByteWrites_SplitsHeadersAndStructures()
    {
        Assert.Equal(42, await RunScalarQueryAsync(WriteSplitMode.PerByte, 1));
    }

    [Fact]
    [Trait("Task", "B0-B-T07")]
    public async Task T07_FixedSmallChunks()
    {
        Assert.Equal(42, await RunScalarQueryAsync(WriteSplitMode.FixedChunk, 3));
    }

    /// <summary>
    /// Observed driver limitation (recorded for the gap matrix): when the server writes the 4-byte
    /// handshake response across more than one TCP segment, the official driver's handshake reader
    /// performs a single read and fails rather than looping until four bytes arrive. A conforming
    /// server should still write the handshake response in one write.
    /// </summary>
    [Fact]
    [Trait("Task", "B0-B-T07")]
    public async Task T07_HandshakeFragmentation_DriverRejectsIt()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        script.On("PULL").Record(ProtocolFixtures.Int(42)).Success(("has_more", ProtocolFixtures.Bool(false)));

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            HandshakeSplit = WriteSplitMode.PerByte,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        Exception? failure = null;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync(), TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        try
        {
            await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
        }

        EvidenceSink.WriteJson("t07-handshake-fragmentation.json", ScenarioSupport.CaptureDocument("T07-handshake-frag", server.Capture));

        Assert.NotNull(failure);
        Assert.Contains("handshake", failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Task", "B0-B-T07")]
    public async Task T07_CoalescedMessages_EquivalentResult()
    {
        var script = ScenarioSupport.PositiveScript();
        script.On("RUN").Success(("fields", ProtocolFixtures.List(ProtocolFixtures.Str("answer"))));
        script.On("PULL")
            .Record(ProtocolFixtures.Int(42))
            .Success(("has_more", ProtocolFixtures.Bool(false)))
            .Coalesce();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        long value = -1;
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
            await using var session = driver.AsyncSession();
            var cursor = await DriverHarness.Bounded(session.RunAsync("RETURN 42 AS answer"));
            Assert.True(await DriverHarness.Bounded(cursor.FetchAsync()));
            value = cursor.Current["answer"].As<long>();
            await DriverHarness.Bounded(cursor.ConsumeAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        EvidenceSink.WriteJson("t07-fragmentation-coalesced.json", ScenarioSupport.CaptureDocument("T07-coalesced", server.Capture));

        Assert.Equal(42, value);
        Assert.Contains(server.Capture.Frames, f => f.Name == "COALESCED");
    }
}
