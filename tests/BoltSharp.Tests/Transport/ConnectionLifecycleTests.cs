using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.Transport;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>§11: disconnect, shutdown and lifecycle determinism.</summary>
public sealed class ConnectionLifecycleTests
{
    [Fact]
    public async Task ClientDisconnectMidSession_ReleasesConnection()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
        {
            client.Handshake(new BoltProtocolVersion(5, 8));
            client.SendRequest(SessionHarness.Hello());
            Assert.IsType<SuccessResponse>(client.ReadResponse());
            client.SendRequest(SessionHarness.Logon());
            Assert.IsType<SuccessResponse>(client.ReadResponse());
            client.Dispose(); // abrupt disconnect, no GOODBYE
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task ListenerShutdownDuringActiveResult_IsBounded()
    {
        var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        // Shutdown while the connection is live: StopAsync completes within the bounded policy
        // and the client observes closure.
        var stopTask = server.StopAsync();
        var finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(stopTask, finished);
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task MessagesAfterTerminalGoodbye_AreNotProcessed()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.MessagesProcessed == 2, TimeSpan.FromSeconds(5)));

        // GOODBYE closes the connection; the pipelined RUN after it is never processed. HELLO,
        // LOGON and GOODBYE itself were processed (3); the RUN was not.
        var goodbye = TransportTestSupport.Frame(TransportRequestEncoder.Encode(new GoodbyeRequest()));
        var run = TransportTestSupport.Frame(TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer")));
        client.SendRaw(goodbye.Concat(run).ToArray());

        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(3, server.Diagnostics.MessagesProcessed);
    }

    [Fact]
    public async Task DisposeAsync_StopsListener_AndReleasesConnections()
    {
        var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        await server.DisposeAsync();
        Assert.False(server.IsRunning);
        Assert.Equal("closed", client.WaitForClose(5_000));
    }

    [Fact]
    public async Task CleanEofBeforeHandshake_CompletesWithoutFault()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
        {
            client.Dispose(); // connect, then close without any byte
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0, server.Diagnostics.BoltHandshakeFailures);
    }
}
