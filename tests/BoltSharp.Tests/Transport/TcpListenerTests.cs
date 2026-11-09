using BoltSharp.Handshake;
using BoltSharp.Messages;
using System.Net;

using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>B5-A1: listener lifecycle, admission bounds and secure defaults.</summary>
public sealed class TcpListenerTests
{
    [Fact]
    public async Task Start_BindsEphemeralPort_AndReportsEndPoint()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        Assert.NotNull(server.BoundEndPoint);
        Assert.True(server.BoundEndPoint!.Port is > 0 and <= 65535);
        Assert.Equal(IPAddress.Loopback, server.BoundEndPoint.Address);
        Assert.True(server.IsRunning);
    }

    [Fact]
    public async Task DefaultConfiguration_Plaintext_IsLoopbackOnly()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        Assert.Null(server.Options.Tls);
        Assert.Equal(IPAddress.Loopback, server.Options.BindAddress);
        Assert.NotNull(server.Options.OperationProviderFactory);
    }

    [Fact]
    public void PlaintextNonLoopbackBind_IsRefusedAtConstruction()
    {
        var options = new BoltTcpServerOptions { BindAddress = IPAddress.Any };
        var exception = Assert.Throws<InvalidOperationException>(() => BoltTcpServer.Create(options));
        Assert.Contains("loopback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlaintextExternalV6Bind_IsRefusedAtConstruction()
    {
        var options = new BoltTcpServerOptions { BindAddress = IPAddress.IPv6Any };
        Assert.Throws<InvalidOperationException>(() => BoltTcpServer.Create(options));
    }

    [Fact]
    public async Task DoubleStart_IsRefused()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await server.StartAsync());
    }

    [Fact]
    public async Task Stop_IsIdempotent_AndReportsStopped()
    {
        var server = await TransportTestSupport.StartServerAsync();
        await server.StopAsync();
        await server.StopAsync();
        Assert.False(server.IsRunning);
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Stop_PreventsNewConnections()
    {
        var server = await TransportTestSupport.StartServerAsync();
        await server.StopAsync();

        // Depending on timing the OS either refuses the connect or accepts into a dead backlog;
        // in both cases the server must never register an accepted connection.
        try
        {
            using var probe = new System.Net.Sockets.TcpClient();
            probe.Connect(server.BoundEndPoint!);
        }
        catch (Exception)
        {
            // Refused at connect: the listener socket is closed.
        }

        Assert.Equal(0, server.Diagnostics.ConnectionsAccepted);
    }

    [Fact]
    public async Task RestartIsNotSupported_NewInstanceIsRequired()
    {
        var server = await TransportTestSupport.StartServerAsync();
        await server.StopAsync();
        await using var replacement = await TransportTestSupport.StartServerAsync();
        Assert.NotEqual(server.BoundEndPoint!.Port, replacement.BoundEndPoint!.Port);
        await server.DisposeAsync();
    }

    [Theory]
    [InlineData(65536)]
    [InlineData(-1)]
    public void InvalidPort_IsRefusedAtConstruction(int port)
    {
        var options = new BoltTcpServerOptions { Port = port };
        Assert.ThrowsAny<ArgumentException>(() => BoltTcpServer.Create(options));
    }

    [Fact]
    public void ZeroMaxConnections_IsRefusedAtConstruction()
    {
        var options = new BoltTcpServerOptions { MaxConnections = 0 };
        Assert.ThrowsAny<ArgumentException>(() => BoltTcpServer.Create(options));
    }

    [Fact]
    public void NonPositiveTimeouts_AreRefusedAtConstruction()
    {
        var options = new BoltTcpServerOptions { MessageReadTimeout = TimeSpan.Zero };
        Assert.ThrowsAny<ArgumentException>(() => BoltTcpServer.Create(options));
    }

    [Fact]
    public async Task Diagnostics_ObserverExceptionsAreContained()
    {
        await using var server = await TransportTestSupport.StartServerAsync();
        server.Diagnostics.Event += _ => throw new InvalidOperationException("observer exploded");

        // A full conversation must still succeed while the observer throws on every event.
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsAssignableFrom<SuccessResponse>(client.ReadResponse());
    }
}
