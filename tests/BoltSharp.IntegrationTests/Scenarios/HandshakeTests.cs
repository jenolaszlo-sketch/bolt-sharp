using BoltSharp.IntegrationTests.Infrastructure;
using Xunit;

namespace BoltSharp.IntegrationTests.Scenarios;

/// <summary>
/// B0-B-T01: authentic driver handshake discovery. Captures the real client's Bolt magic, four
/// version proposals and negotiated revision, and the behaviour when the server rejects all
/// versions. The selected revision is matched against independently decoded byte expectations.
/// </summary>
[Collection("B0-B")]
public sealed class HandshakeTests
{
    [Fact]
    [Trait("Task", "B0-B-T01")]
    public async Task T01_LegacyHandshake_IsCapturedDecodedAndSelected()
    {
        var script = new BoltConnectionScript();
        script.ExpectHello();
        script.On("LOGON").SuccessEmpty();
        script.On("RESET").SuccessEmpty();
        script.On("GOODBYE").Close();

        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = script,
            Handshake = HandshakeMode.Legacy58,
            Bound = TimeSpan.FromSeconds(20),
        });

        var driver = DriverHarness.CreateDriver(server.Port);
        try
        {
            await DriverHarness.Bounded(driver.VerifyConnectivityAsync());
        }
        finally
        {
            await driver.DisposeAsync();
        }

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t01-handshake.json", ScenarioSupport.CaptureDocument("T01", capture));

        // Independent byte-level expectations of the Bolt 5.x handshake.
        Assert.Equal("5.8", capture.NegotiatedVersion);
        Assert.Equal("60 60 B0 17", WireCapture.HexSpaced(capture.ClientHandshake.AsSpan(0, 4)));
        Assert.Equal(20, capture.ClientHandshake.Length);
        Assert.Equal("00 00 08 05", WireCapture.HexSpaced(capture.ServerHandshake));

        var proposals = ScenarioSupport.DecodeProposals(capture.ClientHandshake);
        Assert.Equal(4, proposals.Count);
        Assert.Contains(proposals, p => p.Kind == "manifest-v1" && p.RawHex == "000001FF");
        Assert.Contains(
            proposals,
            p => p.Kind == "range" && p.Version == "5.8" && p.RawHex == "00080805");

        // HELLO must follow the handshake: the driver proceeds to initialization.
        Assert.Contains(capture.Requests, r => r.Name == "HELLO");
        Assert.DoesNotContain(capture.Requests, r => r.Unscripted);
    }

    [Fact]
    [Trait("Task", "B0-B-T01")]
    public async Task T01_ServerRejectsAllVersions_ConnectionFailsWithoutHello()
    {
        await using var server = ScriptedBoltServer.Start(new BoltServerOptions
        {
            Script = new BoltConnectionScript(),
            Handshake = HandshakeMode.RejectAll,
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

        await server.WaitForCompletionAsync(TimeSpan.FromSeconds(20));
        var capture = server.Capture;
        EvidenceSink.WriteJson("t01-handshake-rejected.json", ScenarioSupport.CaptureDocument("T01-reject", capture));

        Assert.NotNull(failure);
        Assert.Equal("00 00 00 00", WireCapture.HexSpaced(capture.ServerHandshake));
        Assert.Equal("rejected", capture.NegotiatedVersion);
        // A rejected handshake must not proceed to HELLO.
        Assert.DoesNotContain(capture.Requests, r => r.Name == "HELLO");
        Assert.Empty(capture.Requests);
    }
}
