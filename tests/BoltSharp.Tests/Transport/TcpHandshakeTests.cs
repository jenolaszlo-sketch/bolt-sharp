using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.Transport;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>B5-A3: B2 handshake integration over real sockets, including fragmentation.</summary>
public sealed class TcpHandshakeTests
{
    [Fact]
    public async Task ValidHandshake_Selects58_AndRepliesExactWord()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        client.SendRaw(TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)));
        var reply = client.RawRead(4);
        // The selected version is written as the big-endian 32-bit word 00 00 08 05 (Bolt 5.8).
        Assert.Equal("00000805", Convert.ToHexString(reply));

        client.AttachFraming();
        client.SendRequest(SessionHarness.Hello());
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());
        Assert.Equal(
            new BoltProtocolVersion(5, 8),
            BoltProtocolVersion.FromUInt32((uint)((reply[0] << 24) | (reply[1] << 16) | (reply[2] << 8) | reply[3])));
    }

    [Fact]
    public async Task HandshakeOneByteAtATime_NegotiatesSuccessfully()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        foreach (var b in TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)))
        {
            client.SendRaw(new[] { b });
        }

        var reply = client.RawRead(4);
        Assert.Equal("00000805", Convert.ToHexString(reply));
    }

    [Fact]
    public async Task InvalidIdentification_IsRefusedSafely()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        // Wrong magic bytes: 59 59 B0 17.
        client.SendRaw(new byte[] { 0x59, 0x59, 0xB0, 0x17, 0, 0, 8, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(1, server.Diagnostics.BoltHandshakeFailures);
    }

    [Fact]
    public async Task TruncatedHandshakeFollowedByEof_ClosesCleanly()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        client.SendRaw(new byte[] { 0x60, 0x60, 0xB0 }); // 3 of 4 magic bytes
        client.Dispose(); // EOF while the handshake is incomplete

        // The truncated connection is classified as a clean client disconnect and the listener
        // remains healthy for the next client.
        using var second = TestBoltClient.Connect(server.BoundEndPoint!);
        second.Handshake(new BoltProtocolVersion(5, 8));
        second.SendRequest(SessionHarness.Hello());
        Assert.IsAssignableFrom<SuccessResponse>(second.ReadResponse());
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task ZeroVersionProposal_IsRejectedAndClosed()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        // Four all-zero proposals: the protocol-zero sentinel, never negotiable.
        client.SendRaw(TransportTestSupport.HandshakeWords(0, 0, 0, 0));
        var reply = client.RawRead(4);
        Assert.Equal("00000000", Convert.ToHexString(reply));
        Assert.Equal("closed", client.WaitForClose(5_000));
    }

    [Fact]
    public async Task UnsupportedNearbyVersions_AreNotSelected()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        // 5.7, 5.9, 6.0 and 5.8: only 5.8 (last, supported) may be selected; the near misses
        // (5.7/5.9/6.0) must not be chosen merely because a nearby revision is supported.
        client.SendRaw(TransportTestSupport.HandshakeFromVersions(
            new BoltProtocolVersion(5, 7), new BoltProtocolVersion(5, 9), new BoltProtocolVersion(6, 0), new BoltProtocolVersion(5, 8)));
        Assert.Equal("00000805", Convert.ToHexString(client.RawRead(4)));
    }

    [Fact]
    public async Task UnsupportedOnlyVersions_RejectWithZeroWord()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        client.SendRaw(TransportTestSupport.HandshakeFromVersions(
            new BoltProtocolVersion(5, 7), new BoltProtocolVersion(5, 9), new BoltProtocolVersion(4, 4), new BoltProtocolVersion(6, 0)));
        Assert.Equal("00000000", Convert.ToHexString(client.RawRead(4)));
        Assert.Equal("closed", client.WaitForClose(5_000));
    }

    [Fact]
    public async Task HandshakeAndHelloInOneWrite_PreservedBeyondBoundary()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        // Handshake and the framed HELLO pipelined in a single write: the adapter must not
        // discard or misclassify the bytes beyond the handshake boundary.
        var hello = TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Hello()));
        var singleWrite = TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)).Concat(hello).ToArray();
        client.SendRaw(singleWrite);

        var reply = client.RawRead(4);
        Assert.Equal("00000805", Convert.ToHexString(reply));
        client.AttachFraming();
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());
    }

    [Fact]
    public async Task ClientDisconnectDuringNegotiation_IsNotATransportFault()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
        {
            client.SendRaw(new byte[] { 0x60, 0x60, 0xB0, 0x17, 0, 0 });
            client.Dispose(); // abrupt disconnect mid-handshake
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsAccepted == 1, TimeSpan.FromSeconds(5)));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task ListenerShutdownDuringNegotiation_CompletesBounded()
    {
        var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        var stopTask = server.StopAsync();
        var finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(stopTask, finished);
        Assert.Equal("closed", client.WaitForClose(5_000));
        await server.DisposeAsync();
    }

    [Fact]
    public async Task DefaultHandshakeOptions_AdvertiseNothing()
    {
        // The accepted B2 default advertises no version; a server configured with it must reject
        // every handshake (version honesty qualification carried into B5-A).
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            HandshakeOptions = BoltHandshakeOptions.Default,
        });
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        client.SendRaw(TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)));
        Assert.Equal("00000000", Convert.ToHexString(client.RawRead(4)));
        Assert.Equal("closed", client.WaitForClose(5_000));
    }
}
