using System.Diagnostics;
using BoltSharp.Handshake;
using Neo4j.Driver;
using Xunit;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// B0-A-TRACE-01 serializer. The authentic driver probes capture raw handshake bytes into
/// process-local fields, so they must not run concurrently with each other.
/// </summary>
[CollectionDefinition("B0-A-TRACE-01", DisableParallelization = true)]
public sealed class HandshakeTraceCollection
{
}

/// <summary>
/// B0-A-TRACE-01: authentic, loopback-only handshake probes using the pinned official
/// <c>Neo4j.Driver</c> 6.4.0. The listener delegates negotiation to the existing BoltSharp B2-B
/// negotiator. It implements no HELLO/LOGON/session behaviour, so a driver exception after
/// handshake negotiation is expected and is recorded rather than treated as success.
/// </summary>
[Collection("B0-A-TRACE-01")]
public sealed class HandshakeTraceProbeTests
{
    private static readonly TimeSpan DriverTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan TraceTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    [Trait("Task", "B0-A-TRACE-01")]
    public async Task T01_T04_OfficialDriver_ManifestNegotiation_Is_Observed()
    {
        var handshakeOptions = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: true,
            offeredCapabilities: 0);

        var limits = new LoopbackListenerLimits
        {
            MaxConnections = 3,
            MaxHandshakeCaptureBytes = 4_096,
            MaxManifestVersions = 32,
            MaxObservedMessageBytes = 64 * 1024,
            ConnectionLifetime = TimeSpan.FromSeconds(10),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(2),
            PostMessageObservationWindow = TimeSpan.FromMilliseconds(250),
        };

        await using var listener = LoopbackHandshakeListener.Start(handshakeOptions, limits);

        DriverProbeObservation driverObservation;
        HandshakeTrace trace;
        try
        {
            var driverTask = RunOfficialDriverAsync(listener.Uri, DriverTimeout);
            trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
            driverObservation = await driverTask;
        }
        finally
        {
            listener.Cancel();
        }

        var replay = await HandshakeTraceAnalysis.ReplayAsync(
            trace.ClientHandshakeBytes,
            handshakeOptions,
            trace.ServerResponseBytes,
            limits.MaxManifestVersions,
            limits.MaxHandshakeCaptureBytes);

        var report = HandshakeTraceAnalysis.BuildReport(
            "manifest-v1",
            listener.Uri,
            trace,
            driverObservation,
            replay,
            handshakeOptions,
            limits.MaxManifestVersions,
            limits.MaxHandshakeCaptureBytes);

        TraceEvidence.WriteJson("driver-manifest-trace.json", report);
        TraceEvidence.WriteHex("driver-manifest-client-handshake.hex", trace.ClientHandshakeBytes);
        TraceEvidence.WriteHex("driver-manifest-server-response.hex", trace.ServerResponseBytes);

        // T01 — the real driver always begins with the Bolt magic and exactly four proposals.
        Assert.True(trace.ClientHandshakeBytes.Length >= 20,
            "The official driver did not provide the fixed 20-byte Bolt handshake preamble.");
        Assert.Equal(
            new byte[] { 0x60, 0x60, 0xB0, 0x17 },
            trace.ClientHandshakeBytes.AsSpan(0, 4).ToArray());
        Assert.False(trace.HandshakeCaptureTruncated);

        // T02 — the live listener used the production B2-B negotiator, and replaying the captured
        // client bytes through a fresh negotiator reproduces the exact server response.
        Assert.NotEmpty(trace.ServerResponseBytes);
        Assert.True(replay.MatchesLiveResponse,
            "Replaying the captured client bytes through the production negotiator did not reproduce the live response.");

        // T03/T04 — with Manifest v1 offered by the server, the driver's selection must be within
        // the advertised version and capability set, and the first post-handshake message is
        // recorded without retaining its body.
        Assert.Equal("ACCEPTED", trace.Outcome);
        Assert.True(trace.Accepted);
        Assert.Equal("ManifestV1", trace.Mode);
        Assert.Equal("5.8", trace.SelectedVersion);
        Assert.NotNull(trace.PostHandshake);
        Assert.Equal("MESSAGE_READ", trace.PostHandshake!.Status);
        Assert.True(trace.PostHandshake.FramingValid);
        Assert.Equal("HELLO", trace.PostHandshake.SignatureName);
        Assert.NotNull(trace.PostHandshake.MessageBodyLength);
        Assert.Null(trace.PostHandshake.ErrorType);

        // The listener does not answer HELLO, so full connectivity must not succeed.
        Assert.False(driverObservation.CompletedSuccessfully);
        Assert.False(string.IsNullOrWhiteSpace(driverObservation.ExceptionType));
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    [Trait("Task", "B0-A-TRACE-01")]
    public async Task T01_T04_OfficialDriver_LegacyNegotiation_Is_Observed()
    {
        var handshakeOptions = new BoltHandshakeOptions(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled: false,
            offeredCapabilities: 0);

        var limits = new LoopbackListenerLimits
        {
            MaxConnections = 3,
            MaxHandshakeCaptureBytes = 4_096,
            MaxManifestVersions = 32,
            MaxObservedMessageBytes = 64 * 1024,
            ConnectionLifetime = TimeSpan.FromSeconds(10),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(2),
            PostMessageObservationWindow = TimeSpan.FromMilliseconds(250),
        };

        await using var listener = LoopbackHandshakeListener.Start(handshakeOptions, limits);

        DriverProbeObservation driverObservation;
        HandshakeTrace trace;
        try
        {
            var driverTask = RunOfficialDriverAsync(listener.Uri, DriverTimeout);
            trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
            driverObservation = await driverTask;
        }
        finally
        {
            listener.Cancel();
        }

        var replay = await HandshakeTraceAnalysis.ReplayAsync(
            trace.ClientHandshakeBytes,
            handshakeOptions,
            trace.ServerResponseBytes,
            limits.MaxManifestVersions,
            limits.MaxHandshakeCaptureBytes);

        var report = HandshakeTraceAnalysis.BuildReport(
            "legacy",
            listener.Uri,
            trace,
            driverObservation,
            replay,
            handshakeOptions,
            limits.MaxManifestVersions,
            limits.MaxHandshakeCaptureBytes);

        TraceEvidence.WriteJson("driver-legacy-trace.json", report);
        TraceEvidence.WriteHex("driver-legacy-client-handshake.hex", trace.ClientHandshakeBytes);
        TraceEvidence.WriteHex("driver-legacy-server-response.hex", trace.ServerResponseBytes);

        Assert.True(trace.ClientHandshakeBytes.Length >= 20,
            "The official driver did not provide the fixed 20-byte Bolt handshake preamble.");
        Assert.Equal(
            new byte[] { 0x60, 0x60, 0xB0, 0x17 },
            trace.ClientHandshakeBytes.AsSpan(0, 4).ToArray());
        Assert.False(trace.HandshakeCaptureTruncated);

        // A legacy selection response is exactly four bytes and must be reproducible.
        Assert.Equal(4, trace.ServerResponseBytes.Length);
        Assert.True(replay.MatchesLiveResponse,
            "Replaying the captured client bytes through the production negotiator did not reproduce the live response.");

        Assert.Equal("ACCEPTED", trace.Outcome);
        Assert.True(trace.Accepted);
        Assert.Equal("Legacy", trace.Mode);
        Assert.Equal("5.8", trace.SelectedVersion);

        // Legacy continuation is demonstrated only by a valid first framed message following the
        // four-byte selection response.
        Assert.NotNull(trace.PostHandshake);
        Assert.Equal("MESSAGE_READ", trace.PostHandshake!.Status);
        Assert.True(trace.PostHandshake.FramingValid);
        Assert.Equal("HELLO", trace.PostHandshake.SignatureName);

        Assert.False(driverObservation.CompletedSuccessfully);
        Assert.False(string.IsNullOrWhiteSpace(driverObservation.ExceptionType));
    }

    internal static async Task<DriverProbeObservation> RunOfficialDriverAsync(
        Uri endpoint,
        TimeSpan timeout)
    {
        var observation = new DriverProbeObservation();
        var stopwatch = Stopwatch.StartNew();
        IDriver? driver = null;
        Task? connectivity = null;

        try
        {
            // Routed through the single validated construction path (B2-C-R1, finding Q1-F01):
            // DriverTestSettings validates the listener URI and CreateDriver is the only driver
            // construction call site in this project. AuthTokens.None is a documented
            // non-secret configuration; no post-handshake body is persisted.
            var settings = new DriverTestSettings(endpoint);
            driver = settings.CreateDriver();

            connectivity = driver.VerifyConnectivityAsync();
            _ = connectivity.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            await connectivity.WaitAsync(timeout).ConfigureAwait(false);
            observation.CompletedSuccessfully = true;
        }
        catch (TimeoutException)
        {
            observation.TimedOut = true;
            observation.ExceptionType = typeof(TimeoutException).FullName;
            observation.SanitizedMessage =
                "VerifyConnectivityAsync did not complete within the probe timeout.";
        }
        catch (Exception ex)
        {
            observation.ExceptionType = ex.GetType().FullName;
            observation.SanitizedMessage = TraceEvidence.Sanitize(ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            observation.ElapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            if (driver is not null)
            {
                try
                {
                    await driver.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    observation.SanitizedMessage = Append(
                        observation.SanitizedMessage,
                        $"Driver disposal: {TraceEvidence.Sanitize(ex.Message)}");
                }
            }
        }

        return observation;
    }

    private static string Append(string? existing, string addition) =>
        string.IsNullOrWhiteSpace(existing) ? addition : $"{existing} {addition}";
}
