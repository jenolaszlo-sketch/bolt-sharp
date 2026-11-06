using BoltSharp.DriverIntegrationTests.HandshakeTrace;
using BoltSharp.Handshake;
using BoltSharp.IntegrationTests.Infrastructure;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.IntegrationTests.ActualServer;

/// <summary>
/// B0-B-T08: the same official driver against the genuine BoltSharp listener (real production B2-B
/// negotiation and B2-A framing), not the fake responder. The listener implements no messaging
/// protocol, so this records the exact, classified interoperability boundary.
/// </summary>
[Collection("B0-B")]
public sealed class BoltSharpListenerTests
{
    private sealed class Boundary
    {
        public required string Mode { get; init; }

        public required string Outcome { get; init; }

        public required string SelectedVersion { get; init; }

        public string? FirstMessageStatus { get; init; }

        public string? FirstMessageSignature { get; init; }

        public string? DriverFailureType { get; init; }

        public string? DriverFailureMessage { get; init; }
    }

    private static async Task<Boundary> RunAgainstListenerAsync(bool manifestV1Enabled)
    {
        var options = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: manifestV1Enabled);
        var limits = new LoopbackListenerLimits
        {
            MaxConnections = 3,
            ObserveFirstMessage = true,
            ConnectionLifetime = TimeSpan.FromSeconds(10),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(3),
            PostMessageObservationWindow = TimeSpan.FromMilliseconds(200),
        };

        await using var listener = LoopbackHandshakeListener.Start(options, limits);

        var driver = GraphDatabase.Driver(
            listener.Uri,
            AuthTokens.Basic(DriverHarness.ServerUser, DriverHarness.ServerPassword),
            o => o.WithEncryptionLevel(EncryptionLevel.None).WithConnectionTimeout(TimeSpan.FromSeconds(10)));

        Exception? failure = null;
        try
        {
            await driver.VerifyConnectivityAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await driver.DisposeAsync();
        }

        var trace = await listener.WaitForFirstTraceAsync(TimeSpan.FromSeconds(15));
        return new Boundary
        {
            Mode = trace.Mode ?? "unknown",
            Outcome = trace.Outcome,
            SelectedVersion = trace.SelectedVersion ?? "none",
            FirstMessageStatus = trace.PostHandshake?.Status,
            FirstMessageSignature = trace.PostHandshake?.SignatureName,
            DriverFailureType = failure?.GetType().FullName,
            DriverFailureMessage = failure is null
                ? null
                : failure.Message.Replace('\r', ' ').Replace('\n', ' ').Trim(),
        };
    }

    [Fact]
    [Trait("Task", "B0-B-T08")]
    public async Task T08_RealListener_LegacyHandshake_BoundaryIsClassified()
    {
        var boundary = await RunAgainstListenerAsync(manifestV1Enabled: false);
        EvidenceSink.WriteJson("t08-real-listener-legacy.json", boundary);

        // Genuine BoltSharp handshake succeeds and the first post-handshake message is observed.
        Assert.Equal("ACCEPTED", boundary.Outcome);
        Assert.Equal("5.8", boundary.SelectedVersion);
        Assert.Equal("MESSAGE_READ", boundary.FirstMessageStatus);
        Assert.Equal("HELLO", boundary.FirstMessageSignature);

        // The first unsupported step is HELLO: the listener implements no messaging protocol, so the
        // driver cannot complete initialization. The boundary is surfaced to the client as a failure.
        Assert.NotNull(boundary.DriverFailureType);
    }

    [Fact]
    [Trait("Task", "B0-B-T08")]
    public async Task T08_RealListener_ManifestHandshake_BoundaryIsClassified()
    {
        var boundary = await RunAgainstListenerAsync(manifestV1Enabled: true);
        EvidenceSink.WriteJson("t08-real-listener-manifest.json", boundary);

        Assert.Equal("ACCEPTED", boundary.Outcome);
        Assert.Equal("5.8", boundary.SelectedVersion);
        Assert.Equal("MESSAGE_READ", boundary.FirstMessageStatus);
        Assert.Equal("HELLO", boundary.FirstMessageSignature);
        Assert.NotNull(boundary.DriverFailureType);
    }
}
