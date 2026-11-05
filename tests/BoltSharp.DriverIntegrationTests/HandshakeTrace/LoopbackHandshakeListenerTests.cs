using System.Diagnostics;
using BoltSharp.Handshake;
using Xunit;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// Negative and boundary probes for the disposable B0-A-TRACE-01 listener. These use
/// test-controlled synthetic peers only; their behaviour is never attributed to the official
/// driver and is kept separate from the authentic driver trace evidence.
/// </summary>
[Collection("B0-A-TRACE-01")]
public sealed class LoopbackHandshakeListenerTests
{
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TraceTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task UnsupportedVersion_Is_Rejected_With_Zero_And_Closed()
    {
        var options = Options(manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        var preamble = SyntheticHandshakeClient.Preamble(
            SyntheticHandshakeClient.Version(4, 4),
            SyntheticHandshakeClient.Version(4, 3),
            SyntheticHandshakeClient.Version(4, 2),
            SyntheticHandshakeClient.Version(4, 1));

        await client.SendAsync(preamble, IoTimeout);
        var response = await client.ReadExactlyAsync(4, IoTimeout);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, response);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("REJECTED", trace.Outcome);
        Assert.False(trace.Accepted);
        Assert.True(trace.ShouldClose);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, trace.ServerResponseBytes);
        Assert.Null(trace.PostHandshake);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task InvalidBoltIdentification_Is_Rejected_Without_A_Response()
    {
        var options = Options(manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        await client.SendAsync(new byte[20], IoTimeout);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("FORMAT_ERROR", trace.Outcome);
        Assert.Empty(trace.ServerResponseBytes);
        Assert.Equal(typeof(BoltHandshakeFormatException).FullName, trace.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task ClientDisconnect_DuringHandshake_Is_Truncated()
    {
        var options = Options(manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        var partial = SyntheticHandshakeClient.Concat(
            SyntheticHandshakeClient.Magic,
            new byte[] { 0x00, 0x00, 0x08 });
        await client.SendAsync(partial, IoTimeout);
        await client.DisposeAsync();

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("TRUNCATED", trace.Outcome);
        Assert.Empty(trace.ServerResponseBytes);
        Assert.Equal(typeof(BoltHandshakeTruncatedException).FullName, trace.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task FragmentedHandshakeInput_Is_Negotiated_Identically()
    {
        var options = Options(manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        var preamble = SyntheticHandshakeClient.Preamble(
            SyntheticHandshakeClient.Version(5, 8),
            SyntheticHandshakeClient.Version(5, 7),
            SyntheticHandshakeClient.Version(5, 6),
            SyntheticHandshakeClient.Version(5, 4));

        foreach (var value in preamble)
        {
            await client.SendAsync(new[] { value }, IoTimeout);
            await Task.Delay(5);
        }

        var response = await client.ReadExactlyAsync(4, IoTimeout);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x05 }, response);

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("ACCEPTED", trace.Outcome);
        Assert.Equal("Legacy", trace.Mode);
        Assert.Equal("5.8", trace.SelectedVersion);
        Assert.Equal(preamble, trace.ClientHandshakeBytes);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task MalformedManifestSelection_UnofferedVersion_Is_Rejected()
    {
        var options = Options(manifestV1Enabled: true);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        await client.SendAsync(SyntheticHandshakeClient.ManifestPreamble(), IoTimeout);
        var offer = await client.ReadManifestOfferAsync(IoTimeout);
        Assert.Equal(SyntheticHandshakeClient.ManifestMarker, offer.AsSpan(0, 4).ToArray());

        // 5.9 was not offered by the listener (only 5.8 is configured).
        var malformedSelection = SyntheticHandshakeClient.Concat(
            new byte[] { 0x00, 0x00, 0x09, 0x05 },
            new byte[] { 0x00 });
        await client.SendAsync(malformedSelection, IoTimeout);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("PROTOCOL_ERROR", trace.Outcome);
        Assert.Null(trace.Accepted);
        Assert.Equal(typeof(BoltHandshakeProtocolException).FullName, trace.ErrorType);
        Assert.Contains("did not offer", trace.SanitizedError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task MalformedManifestSelection_CapabilitySuperset_Is_Rejected()
    {
        var options = Options(manifestV1Enabled: true);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        await client.SendAsync(SyntheticHandshakeClient.ManifestPreamble(), IoTimeout);
        var offer = await client.ReadManifestOfferAsync(IoTimeout);
        Assert.Equal(SyntheticHandshakeClient.ManifestMarker, offer.AsSpan(0, 4).ToArray());

        // The listener offered capabilities 0x0; selecting bit 1 is a non-subset.
        var malformedSelection = SyntheticHandshakeClient.Concat(
            new byte[] { 0x00, 0x00, 0x08, 0x05 },
            new byte[] { 0x01 });
        await client.SendAsync(malformedSelection, IoTimeout);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("PROTOCOL_ERROR", trace.Outcome);
        Assert.Null(trace.Accepted);
        Assert.Equal(typeof(BoltHandshakeProtocolException).FullName, trace.ErrorType);
        Assert.Contains("not a subset", trace.SanitizedError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task Cancellation_DuringNegotiation_Closes_PendingConnection()
    {
        var options = Options(manifestV1Enabled: false);
        var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(
                maxConnections: 1,
                observeFirstMessage: false,
                connectionLifetime: TimeSpan.FromSeconds(30)));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        var partial = SyntheticHandshakeClient.Concat(
            SyntheticHandshakeClient.Magic,
            new byte[] { 0x00, 0x00 });
        await client.SendAsync(partial, IoTimeout);
        await Task.Delay(100);

        listener.Cancel();
        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("CANCELLED", trace.Outcome);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        await listener.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task Listener_Shutdown_With_PendingConnection_Is_Deterministic()
    {
        var options = Options(manifestV1Enabled: false);
        var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(
                maxConnections: 1,
                observeFirstMessage: false,
                connectionLifetime: TimeSpan.FromSeconds(30)));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        await client.SendAsync(SyntheticHandshakeClient.Magic, IoTimeout);
        await Task.Delay(50);

        var stopwatch = Stopwatch.StartNew();
        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("CANCELLED", trace.Outcome);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task MalformedManifestSelection_ReservedByteSet_Is_FormatError()
    {
        // Consolidated from implementation B0-A-TRACE-01-B, test N05: a selection whose reserved
        // high bytes are set is a structural format error, distinct from an unoffered version.
        var options = Options(manifestV1Enabled: true);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        await client.SendAsync(SyntheticHandshakeClient.ManifestPreamble(), IoTimeout);
        var offer = await client.ReadManifestOfferAsync(IoTimeout);
        Assert.Equal(SyntheticHandshakeClient.ManifestMarker, offer.AsSpan(0, 4).ToArray());

        var malformedSelection = new byte[] { 0x01, 0x00, 0x08, 0x05, 0x00 };
        await client.SendAsync(malformedSelection, IoTimeout);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("FORMAT_ERROR", trace.Outcome);
        Assert.Null(trace.Accepted);
        Assert.Equal(typeof(BoltHandshakeFormatException).FullName, trace.ErrorType);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task HandshakeTimeout_DuringNegotiation_ClosesPendingConnection()
    {
        // Consolidated from implementation B0-A-TRACE-01-B, test N06: a client that stalls after
        // identification must not hold the connection past the dedicated handshake timeout.
        var options = Options(manifestV1Enabled: false);
        var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(
                maxConnections: 1,
                observeFirstMessage: false,
                connectionLifetime: TimeSpan.FromSeconds(30),
                handshakeTimeout: TimeSpan.FromMilliseconds(300)));

        await using var client = await SyntheticHandshakeClient.ConnectAsync(listener.Port, IoTimeout);
        await client.SendAsync(SyntheticHandshakeClient.Magic, IoTimeout);

        var trace = await listener.WaitForFirstTraceAsync(TraceTimeout);
        Assert.Equal("HANDSHAKE_TIMEOUT", trace.Outcome);
        Assert.Null(trace.Accepted);
        Assert.True(await client.WaitForCloseWithoutDataAsync(IoTimeout));

        await listener.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-01")]
    public async Task Listener_Binds_Only_To_Loopback_And_Uses_An_Ephemeral_Port()
    {
        var options = Options(manifestV1Enabled: false);
        await using var listener = LoopbackHandshakeListener.Start(
            options,
            Limits(maxConnections: 1, observeFirstMessage: false));

        Assert.Equal("127.0.0.1", listener.LocalEndPoint.Address.ToString());
        Assert.True(listener.Port > 0);
        Assert.Equal("bolt", listener.Uri.Scheme);
        Assert.Equal("127.0.0.1", listener.Uri.Host);
        Assert.Equal(listener.Port, listener.Uri.Port);
    }

    private static BoltHandshakeOptions Options(bool manifestV1Enabled) =>
        new(
            new[] { new BoltProtocolVersion(5, 8) },
            manifestV1Enabled,
            offeredCapabilities: 0);

    private static LoopbackListenerLimits Limits(
        int maxConnections,
        bool observeFirstMessage,
        TimeSpan? connectionLifetime = null,
        TimeSpan? handshakeTimeout = null) =>
        new()
        {
            MaxConnections = maxConnections,
            MaxHandshakeCaptureBytes = 4_096,
            MaxManifestVersions = 32,
            MaxObservedMessageBytes = 64 * 1024,
            ConnectionLifetime = connectionLifetime ?? TimeSpan.FromSeconds(5),
            HandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(1),
            PostMessageObservationWindow = TimeSpan.FromMilliseconds(100),
            ObserveFirstMessage = observeFirstMessage,
        };
}
