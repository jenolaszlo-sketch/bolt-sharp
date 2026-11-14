using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Transport;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>§17: multi-client isolation, bounded concurrency and shutdown with active clients.</summary>
public sealed class MultiClientTests
{
    [Fact]
    public async Task TwoClients_MaintainIndependentSessions()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var clientA = TransportTestSupport.ConnectAndAuthorize(server);
        using var clientB = TransportTestSupport.ConnectAndAuthorize(server);

        // Separate queries, interleaved: each connection keeps its own session state.
        clientA.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        clientB.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        var aRun = Assert.IsType<SuccessResponse>(clientA.ReadResponse());
        var bRun = Assert.IsType<SuccessResponse>(clientB.ReadResponse());
        Assert.NotEqual(aRun, bRun);

        clientA.SendRequest(new PullRequest(1000, null));
        clientB.SendRequest(new PullRequest(1000, null));
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(clientA.ReadResult()[0]).Data.Items[0]).Value);
        Assert.Equal(42, Assert.IsType<PackStreamInteger>(Assert.IsType<RecordResponse>(clientB.ReadResult()[0]).Data.Items[0]).Value);

        // Independent close: A disconnects; B continues working.
        clientA.Dispose();
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 1, TimeSpan.FromSeconds(5)));

        clientB.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(clientB.ReadResponse());
    }

    [Fact]
    public async Task OneIdleClient_DoesNotBlockAnother()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var idle = TestBoltClient.Connect(server.BoundEndPoint!); // connects, never handshakes
        using var active = TestBoltClient.Connect(server.BoundEndPoint!);
        active.Handshake(new BoltProtocolVersion(5, 8));
        active.SendRequest(SessionHarness.Hello());
        TransportTestSupport.ExpectSuccess(active.ReadResponse(), "HELLO");
        active.SendRequest(SessionHarness.Logon());
        TransportTestSupport.ExpectSuccess(active.ReadResponse(), "LOGON");
        active.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        TransportTestSupport.ExpectSuccess(active.ReadResponse(), "RUN");
        active.SendRequest(new PullRequest(1000, null));
        Assert.Equal(2, active.ReadResult().Length);

        // Both connections remain active: the idle one made no progress but holds its slot.
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 2, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FailureInOneClient_IsolatedFromOthers()
    {
        await using var server = await TransportTestSupport.StartServerAsync();

        // Subscribe before any connection is made: the diagnostics events are raised by the
        // server's connection task after its counters are updated, so they are the authoritative
        // completion signal for this test (owner finding OWNER-F02). Observing the bad client's
        // local socket disposal is NOT evidence that the server finished failure accounting.
        var failed = TransportTestSupport.ProbeEvent(server, BoltTransportEventKind.ConnectionFailed);
        var completed = TransportTestSupport.ProbeEvent(server, BoltTransportEventKind.ConnectionCompleted);

        using var bad = TestBoltClient.Connect(server.BoundEndPoint!);
        using var good = TransportTestSupport.ConnectAndAuthorize(server, "RETURN 42 AS answer");

        // The bad client sends invalid handshake bytes (not the Bolt magic) and disconnects: the
        // server rejects the connection during the B2 handshake and fails it closed.
        bad.SendRaw(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });
        bad.Dispose();

        // Wait for the server-side failure transition before asserting counters or isolation.
        var failureEvent = await TransportTestSupport.AwaitEventAsync(
            failed, server, "ConnectionFailed for the invalid-handshake client");
        Assert.Equal("handshake:BoltHandshakeFormatException", failureEvent.Detail);
        Assert.Equal(2, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(1, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(1, server.Diagnostics.ConnectionsActive);

        // The good client is unaffected: its already-open operation still completes normally.
        good.SendRequest(new PullRequest(1000, null));
        Assert.Equal(2, good.ReadResult().Length);

        // Retire the good connection and wait for its authoritative completion accounting too.
        good.Dispose();
        var completionEvent = await TransportTestSupport.AwaitEventAsync(
            completed, server, "ConnectionCompleted for the healthy client");
        Assert.Equal("closed", completionEvent.Detail);

        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(1, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(2, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(0, server.Diagnostics.ConnectionsRejectedForCapacity);
        Assert.Equal(4, server.Diagnostics.MessagesProcessed); // HELLO, LOGON, RUN, PULL on the good client
    }

    [Fact]
    public async Task ShutdownWithMultipleActiveClients_SignalsAll()
    {
        var server = await TransportTestSupport.StartServerAsync();
        using var clientA = TransportTestSupport.ConnectAndAuthorize(server);
        using var clientB = TransportTestSupport.ConnectAndAuthorize(server, "RETURN 42 AS answer");
        Assert.Equal(2, server.Diagnostics.ConnectionsActive);

        await server.StopAsync();

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal("closed", clientA.WaitForClose(5_000));
        Assert.Equal("closed", clientB.WaitForClose(5_000));
        await server.DisposeAsync();
    }

    [Fact]
    public async Task ConcurrentAdmission_RespectsCapacityBound()
    {
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            MaxConnections = 3,
        });

        var clients = new List<TestBoltClient>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                clients.Add(TestBoltClient.Connect(server.BoundEndPoint!));
            }

            Assert.True(TransportTestSupport.WaitUntil(
                () => server.Diagnostics.ConnectionsActive == 3, TimeSpan.FromSeconds(5)));

            // The fourth connection is refused; the three active ones stay connected.
            using var excess = TestBoltClient.Connect(server.BoundEndPoint!);
            Assert.Equal("closed", excess.WaitForClose(5_000));
            Assert.Equal(1, server.Diagnostics.ConnectionsRejectedForCapacity);
            Assert.Equal(3, server.Diagnostics.ConnectionsActive);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task CredentialsNeverAppearInDiagnostics_AcrossClients()
    {
        var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var server = await TransportTestSupport.StartServerAsync();
        server.Diagnostics.Event += e => observed.Enqueue(e.Detail ?? string.Empty);

        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon(principal: "sentinel-principal", credentials: "sentinel-credential"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        // The sentinel credential material never appears in any diagnostic event.
        foreach (var detail in observed.ToArray())
        {
            Assert.DoesNotContain("sentinel-credential", detail);
            Assert.DoesNotContain("sentinel-principal", detail);
        }
    }
}
