using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>
/// B5B-CI-RACE-REMED: listener shutdown ownership and bounded termination.
/// <para>
/// These tests pin the shutdown contract, not an implementation detail: acceptance always quiesces
/// without orphaning an accepted socket, every accepted connection is either registered or closed,
/// shutdown is bounded, forced termination releases a connection that ignores cancellation, an
/// incomplete shutdown is observable to the caller, and repeated or concurrent shutdown calls
/// observe one outcome.
/// </para>
/// </summary>
public sealed class ListenerShutdownLifecycleTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The frozen scenario, repeated: a peer that has completed the TCP handshake but has not sent
    /// a single protocol byte must observe closure, and shutdown must report completion. Repetition
    /// turns the former race (which failed roughly one run in four) into a deterministic guard.
    /// </summary>
    [Fact]
    public async Task ShutdownDuringNegotiation_ClosesPeerAndCompletes_Repeatedly()
    {
        for (var round = 0; round < 20; round++)
        {
            var server = await TransportTestSupport.StartServerAsync();
            using var client = TestBoltClient.Connect(server.BoundEndPoint!);

            var stop = server.StopAsync();
            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(Bounded)));
            await stop;

            Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
            Assert.Equal(0, server.Diagnostics.ConnectionsActive);
            Assert.Equal("closed", client.WaitForClose(5_000));
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// TLS authentication must observe the shutdown token rather than wait out its own handshake
    /// bound. The decisive assertion is the classification: "listener-shutdown" can only come from
    /// the cooperative cancellation path, never from forced socket termination.
    /// </summary>
    [Fact]
    public async Task ShutdownDuringTlsAuthentication_CancelsNegotiation()
    {
        var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        using var cert = certificate;
        var outcomes = new ConcurrentQueue<string>();
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            Tls = new BoltTlsServerOptions(certificate),
            TlsHandshakeTimeout = TimeSpan.FromSeconds(30),
            ShutdownTimeout = TimeSpan.FromSeconds(10),
        });
        server.Diagnostics.Event += item =>
        {
            if (item.Kind == BoltTransportEventKind.ConnectionCompleted)
            {
                outcomes.Enqueue(item.Detail ?? "");
            }
        };

        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 1, Bounded));

        var elapsed = Stopwatch.StartNew();
        await server.StopAsync();
        elapsed.Stop();

        Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2),
            $"TLS authentication did not observe shutdown cancellation ({elapsed.ElapsedMilliseconds} ms)");
        Assert.Contains("listener-shutdown", outcomes);
        Assert.Equal(0, server.Diagnostics.TlsHandshakeFailures);
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
    }

    /// <summary>
    /// Acceptance racing shutdown: every peer whose TCP connect succeeded is closed, and every
    /// accepted connection produces exactly one terminal accounting event. Nothing is abandoned
    /// and nothing is counted twice.
    /// </summary>
    [Fact]
    public async Task ConcurrentAcceptsDuringShutdown_AreAllClosedAndAccounted()
    {
        for (var round = 0; round < 5; round++)
        {
            var server = await TransportTestSupport.StartServerAsync();
            var clients = new ConcurrentBag<TestBoltClient>();
            var connects = Enumerable.Range(0, 8).Select(async _ =>
            {
                try
                {
                    clients.Add(TestBoltClient.Connect(server.BoundEndPoint!));
                }
                catch (SocketException)
                {
                    // The listener closed before this connect: a refused connect is a closed peer.
                }

                await Task.Yield();
            }).ToArray();

            var stop = Task.Run(() => server.StopAsync());
            await Task.WhenAll(connects);
            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(Bounded)));
            await stop;

            Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
            Assert.Equal(0, server.Diagnostics.ConnectionsActive);
            Assert.Equal(
                server.Diagnostics.ConnectionsAccepted,
                server.Diagnostics.ConnectionsCompleted + server.Diagnostics.ConnectionsFailed);

            foreach (var client in clients)
            {
                using (client)
                {
                    Assert.Equal("closed", client.WaitForClose(5_000));
                }
            }

            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// A connection blocked inside the operation provider cannot be released by cooperative
    /// cancellation. Shutdown must still return inside its budget by forcing the socket, and it
    /// must report that termination was incomplete instead of claiming a clean stop. The pipeline
    /// task is not abandoned: once it resumes it retires and the accounting balances.
    /// </summary>
    [Fact]
    public async Task BlockedConnection_IsForced_Bounded_AndReportsIncomplete()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var provider = new GatedOperationProvider(gate, entered);
        var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
            ShutdownTimeout = TimeSpan.FromMilliseconds(250),
        });
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 1 AS x"));
        Assert.True(entered.Wait(Bounded), "the connection never reached the blocking provider");

        var elapsed = Stopwatch.StartNew();
        await server.StopAsync();
        elapsed.Stop();

        Assert.Equal(BoltShutdownOutcome.Incomplete, server.ShutdownOutcome);
        Assert.InRange(elapsed.ElapsedMilliseconds, 150, 3_000);
        Assert.Equal("closed", client.WaitForClose(5_000));

        // The abandoned pipeline is still owned: releasing it retires the connection and the
        // accounting balances exactly once.
        gate.Set();
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, Bounded));
        Assert.Equal(
            server.Diagnostics.ConnectionsAccepted,
            server.Diagnostics.ConnectionsCompleted + server.Diagnostics.ConnectionsFailed);
        await server.DisposeAsync();
    }

    /// <summary>
    /// Caller cancellation abandons only that caller's wait. The shutdown itself keeps running and
    /// a later call observes its real outcome.
    /// </summary>
    [Fact]
    public async Task CallerCancellation_DoesNotAbandonShutdown()
    {
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var provider = new GatedOperationProvider(gate, entered);
        var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
        });
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);
        client.Handshake(new BoltProtocolVersion(5, 8));
        client.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Logon());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 1 AS x"));
        Assert.True(entered.Wait(Bounded), "the connection never reached the blocking provider");

        using var caller = new CancellationTokenSource();
        var waiting = server.StopAsync(caller.Token);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // The listener is stopping, but the shutdown has not been abandoned or finished.
        Assert.False(server.IsRunning);
        Assert.Equal(BoltShutdownOutcome.NotRequested, server.ShutdownOutcome);

        gate.Set();
        await server.StopAsync();
        Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, Bounded));
        await server.DisposeAsync();
    }

    /// <summary>
    /// Repeated and concurrent shutdown calls are idempotent: they all complete, none throws, and
    /// they observe one outcome.
    /// </summary>
    [Fact]
    public async Task RepeatedAndConcurrentStop_ShareOneOutcome()
    {
        var server = await TransportTestSupport.StartServerAsync();
        using var client = TestBoltClient.Connect(server.BoundEndPoint!);

        var stops = Enumerable.Range(0, 8).Select(_ => server.StopAsync()).ToArray();
        Assert.Same(stops[0], await Task.WhenAny(stops[0], Task.Delay(Bounded)));
        await Task.WhenAll(stops);

        await server.StopAsync();
        Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
        Assert.Equal("closed", client.WaitForClose(5_000));
        await server.DisposeAsync();
    }

    /// <summary>
    /// Graceful shutdown preserves a session that already completed its own lifecycle: the
    /// listener reports completion, the client observed its own closure, and no fault is invented.
    /// </summary>
    [Fact]
    public async Task GracefulShutdown_PreservesCompletedSession()
    {
        var server = await TransportTestSupport.StartServerAsync();
        using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
        {
            client.Handshake(new BoltProtocolVersion(5, 8));
            client.SendRequest(SessionHarness.Hello());
            Assert.IsType<SuccessResponse>(client.ReadResponse());
            client.SendRequest(SessionHarness.Logon());
            Assert.IsType<SuccessResponse>(client.ReadResponse());
            client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
            Assert.IsType<SuccessResponse>(client.ReadResponse());
            client.SendRequest(SessionHarness.Pull(1000));
            var responses = client.ReadResult();
            Assert.Equal(2, responses.Length);
            client.SendRequest(SessionHarness.Goodbye());
            Assert.Equal("closed", client.WaitForClose(5_000));
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, Bounded));
        await server.StopAsync();

        Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
        Assert.Equal(1, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
        await server.DisposeAsync();
    }

    /// <summary>A provider whose <c>Prepare</c> blocks until the test releases it.</summary>
    private sealed class GatedOperationProvider(ManualResetEventSlim gate, ManualResetEventSlim entered)
        : IBoltOperationProvider
    {
        public IBoltOperation? Prepare(RunRequest run)
        {
            entered.Set();
            gate.Wait();
            return new ScriptedOperation(
                ["x"], new PackStreamValue?[] { new PackStreamInteger(1) });
        }
    }
}
