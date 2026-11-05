using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BoltSharp.Handshake;
using Xunit;

namespace BoltSharp.DriverIntegrationTests.HandshakeTrace;

/// <summary>
/// B0-A-TRACE-R1 regression coverage for TRACEQ1-F01: the loopback handshake listener's shutdown
/// must be deterministic and must never fault because a client socket was already closed.
/// <para>
/// The historical reviewer probe (docs/reviews/probes/B0-A-TRACE-Q1) is not modified. These tests
/// exercise the same defect through the producer's own listener, including two deterministic
/// interleavings produced by handing the real handler/helper an already-closed socket.
/// </para>
/// </summary>
[Collection("B0-A-TRACE-01")]
public sealed class ListenerShutdownRegressionTests
{
    private static LoopbackListenerLimits Limits(
        int maxConnections = 1,
        bool observe = false,
        TimeSpan? connectionLifetime = null) =>
        new()
        {
            MaxConnections = maxConnections,
            ObserveFirstMessage = observe,
            ConnectionLifetime = connectionLifetime ?? TimeSpan.FromSeconds(5),
            HandshakeTimeout = TimeSpan.FromSeconds(5),
            FirstMessageObservationTimeout = TimeSpan.FromSeconds(1),
            PostMessageObservationWindow = TimeSpan.FromMilliseconds(100),
            MaxHandshakeCaptureBytes = 4_096,
        };

    private static BoltHandshakeOptions Options() =>
        new(new[] { new BoltProtocolVersion(5, 8) }, manifestV1Enabled: false);

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0)
            {
                throw new IOException("unexpected end of stream");
            }

            offset += read;
        }

        return buffer;
    }

    /// <summary>Deterministic interleaving: the endpoint helper must tolerate a closed socket.</summary>
    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public void R1_02_EndpointCapture_WithClosedSocket_IsUnknown_NotNre()
    {
        var method = typeof(LoopbackHandshakeListener).GetMethod(
            "SafeRemoteEndPoint", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SafeRemoteEndPoint not found");

        var closedClient = new TcpClient();
        closedClient.Client.LingerState = new LingerOption(true, 0);
        closedClient.Close();

        var exception = Record.Exception(() => method.Invoke(null, new object[] { closedClient }));
        Assert.Null(exception);
        Assert.Equal("UNKNOWN", method.Invoke(null, new object[] { closedClient }));
    }

    /// <summary>
    /// Deterministic interleaving: an already-closed client handed to the real connection handler
    /// must complete (recording a CLOSED trace) instead of faulting the handler task — which is
    /// what propagated through DisposeAsync in TRACEQ1-F01.
    /// </summary>
    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_10_Handler_WithAlreadyClosedClient_DoesNotFault()
    {
        await using var listener = LoopbackHandshakeListener.Start(Options(), Limits());

        var closedClient = new TcpClient();
        closedClient.Client.LingerState = new LingerOption(true, 0);
        closedClient.Close();

        var fault = await InvokeHandlerAsync(listener, 1, closedClient);
        Assert.Null(fault);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_01_ClientClosesBeforeListenerDisposal_ShutdownIsClean()
    {
        var listener = LoopbackHandshakeListener.Start(Options(), Limits());

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port);
        await client.Client.SendAsync(new byte[] { 0x60, 0x60, 0xB0, 0x17 });
        client.Close();

        var stopwatch = Stopwatch.StartNew();
        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Single(listener.Traces);
        Assert.NotEqual(true, listener.Traces[0].Accepted);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_03_DisposalDuringActiveConnection_IsClean()
    {
        var listener = LoopbackHandshakeListener.Start(Options(), Limits());

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port);
        await client.Client.SendAsync(new byte[] { 0x60, 0x60, 0xB0, 0x17, 0x00 });

        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(listener.Traces);
        var outcome = listener.Traces[0].Outcome;
        Assert.True(
            outcome is "CANCELLED" or "TRUNCATED" or "CLOSED" or "IO_CLOSED",
            $"unexpected outcome '{outcome}'");
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_04_CancellationConcurrentWithClientClosure_IsClean()
    {
        var listener = LoopbackHandshakeListener.Start(Options(), Limits());

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port);
        await client.Client.SendAsync(new byte[] { 0x60, 0x60, 0xB0, 0x17 });

        // Cancel and close at the same time to race cancellation against socket closure. Whether
        // the accept loop wins the race determines whether a connection is traced at all, so the
        // guarantee here is bounded, exception-free disposal with at most one traced connection.
        listener.Cancel();
        client.Close();

        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(
            listener.Traces.Count <= 1,
            $"expected at most one traced connection, observed {listener.Traces.Count}");
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_05_RepeatedDisposal_IsClean()
    {
        var listener = LoopbackHandshakeListener.Start(Options(), Limits());
        await listener.DisposeAsync();
        await listener.DisposeAsync();
        await listener.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_06_ClientAbort_DuringShutdown_IsClean()
    {
        var listener = LoopbackHandshakeListener.Start(Options(), Limits());

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port);
        await client.Client.SendAsync(new byte[] { 0x60, 0x60, 0xB0, 0x17 });
        // Abortive close: RST rather than FIN.
        client.Client.LingerState = new LingerOption(true, 0);
        client.Close();

        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(listener.Traces);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_07_ShutdownWithPendingRead_IsClean()
    {
        var listener = LoopbackHandshakeListener.Start(
            Options(),
            Limits(connectionLifetime: TimeSpan.FromSeconds(30)));

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port);
        // Partial handshake only: the handler is blocked in a read when disposal begins.
        await client.Client.SendAsync(new byte[] { 0x60, 0x60 });

        var stopwatch = Stopwatch.StartNew();
        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Single(listener.Traces);
    }

    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_08_AfterDisposal_PortIsClosed_AndNoPendingTasks()
    {
        var listener = LoopbackHandshakeListener.Start(Options(), Limits());
        var port = listener.Port;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await client.Client.SendAsync(new byte[] { 0x60, 0x60, 0xB0, 0x17 });

        await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        // The listening socket is closed: a new connection is refused.
        using var probe = new TcpClient();
        var refused = false;
        try
        {
            await probe.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (SocketException)
        {
            refused = true;
        }
        catch (TimeoutException)
        {
            refused = true;
        }

        Assert.True(refused, "the listener socket accepted a connection after disposal");
        Assert.Single(listener.Traces);
    }

    /// <summary>
    /// Accept/dispose race across many iterations. Repetition is used only to widen coverage of the
    /// interleaving; the deterministic cases R1_02/R1_10 pin the mechanism itself.
    /// </summary>
    [Fact]
    [Trait("Category", "B0-A-TRACE-R1")]
    public async Task R1_09_AcceptDisposeRace_Repeated_NoUnhandledFailure()
    {
        const int iterations = 200;
        for (var i = 0; i < iterations; i++)
        {
            var listener = LoopbackHandshakeListener.Start(Options(), Limits());
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, listener.Port);
            }
            catch (SocketException)
            {
                // The listener may already have stopped listening at the connection cap; that is
                // bounded behaviour, not a failure of this test.
            }

            await listener.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(listener.Traces.Count <= 1);
        }
    }

    // ---- infrastructure --------------------------------------------------------

    private static async Task<Exception?> InvokeHandlerAsync(
        LoopbackHandshakeListener listener,
        int index,
        TcpClient client)
    {
        var method = typeof(LoopbackHandshakeListener).GetMethod(
            "HandleConnectionAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("HandleConnectionAsync not found");

        object?[] arguments = method.GetParameters()
            .Select(parameter =>
                parameter.ParameterType == typeof(TcpClient) ? client :
                parameter.ParameterType == typeof(int) ? index :
                (object)"UNKNOWN")
            .ToArray();

        var task = (Task)method.Invoke(listener, arguments)!;
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
