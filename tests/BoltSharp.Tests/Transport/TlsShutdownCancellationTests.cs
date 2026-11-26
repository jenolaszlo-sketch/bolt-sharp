using System.Collections.Concurrent;

using BoltSharp.Handshake;
using BoltSharp.Tests.Sessions;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>
/// B5B-CI-RACE-REMED-01-R3/F-01: TLS authentication cancellation must reach the underlying
/// handshake, not merely stop waiting for it. The independent qualification found that
/// <c>AuthenticateAsServerAsync(...).WaitAsync(token)</c> abandoned the handshake task, whose
/// fault then surfaced as an unobserved task exception on every TLS shutdown (100/100 on both
/// the historical baseline and the R2 remediation).
/// <para>
/// These tests measure <see cref="TaskScheduler.UnobservedTaskException"/> after forced
/// collection, exactly like the independent control. They run in a non-parallel collection so a
/// concurrent test cannot contribute an unrelated unobserved fault.
/// </para>
/// </summary>
[Collection("NonParallelTransport")]
public sealed class TlsShutdownCancellationTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Listener shutdown during an incomplete TLS authentication must leave no abandoned
    /// handshake task. The control is capable of exposing the defect: the same test fails on the
    /// R2 subject (100/100 unobserved), and passes on the R3 successor (0 unobserved).
    /// </summary>
    [Fact]
    public async Task ShutdownDuringTlsAuthentication_LeavesNoUnobservedTaskFault()
    {
        using var certificate = TransportTestSupport.CreateSelfSignedCertificate();

        // Flush any faulted task left over from earlier tests before measuring.
        await DrainUnobservedTaskExceptionsAsync();

        var unobserved = new ConcurrentBag<AggregateException>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            unobserved.Add(args.Exception);
            args.SetObserved();
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            for (var round = 0; round < 20; round++)
            {
                var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
                {
                    CredentialVerifier = TestCredentialVerifier.AcceptAll(),
                    Tls = new BoltTlsServerOptions(certificate),
                    TlsHandshakeTimeout = TimeSpan.FromSeconds(30),
                    ShutdownTimeout = TimeSpan.FromSeconds(5),
                });

                using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
                {
                    Assert.True(TransportTestSupport.WaitUntil(
                        () => server.Diagnostics.ConnectionsActive == 1, Bounded));

                    var stop = server.StopAsync();
                    Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(Bounded)));
                    await stop;

                    Assert.Equal(BoltShutdownOutcome.Completed, server.ShutdownOutcome);
                    Assert.Equal(0, server.Diagnostics.TlsHandshakeFailures);
                }

                await server.DisposeAsync();
            }

            await ForceCollectionAsync();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }

        Assert.True(
            unobserved.IsEmpty,
            $"TLS shutdown abandoned {unobserved.Count} authentication task(s); first: " +
            $"{unobserved.FirstOrDefault()?.Flatten().InnerExceptions.FirstOrDefault()?.Message}");
    }

    /// <summary>
    /// The bounded TLS handshake timeout takes the same cancellation path. It must abort the
    /// handshake rather than abandon it, and a genuine timeout must still be classified as a TLS
    /// handshake failure (not as listener shutdown).
    /// </summary>
    [Fact]
    public async Task TlsHandshakeTimeout_LeavesNoUnobservedTaskFault()
    {
        using var certificate = TransportTestSupport.CreateSelfSignedCertificate();

        await DrainUnobservedTaskExceptionsAsync();

        var unobserved = new ConcurrentBag<AggregateException>();
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            unobserved.Add(args.Exception);
            args.SetObserved();
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            for (var round = 0; round < 10; round++)
            {
                var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
                {
                    CredentialVerifier = TestCredentialVerifier.AcceptAll(),
                    Tls = new BoltTlsServerOptions(certificate),
                    TlsHandshakeTimeout = TimeSpan.FromMilliseconds(200),
                    ShutdownTimeout = TimeSpan.FromSeconds(5),
                });

                using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
                {
                    Assert.Equal("closed", client.WaitForClose(10_000));
                }

                Assert.True(TransportTestSupport.WaitUntil(
                    () => server.Diagnostics.ConnectionsActive == 0, Bounded));
                Assert.True(server.Diagnostics.TlsHandshakeFailures >= 1,
                    "a genuine TLS handshake timeout must remain a TLS handshake failure");
                Assert.True(server.Diagnostics.ConnectionsFailed >= 1,
                    "a genuine TLS handshake timeout must remain a failed connection");

                await server.StopAsync();
                await server.DisposeAsync();
            }

            await ForceCollectionAsync();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }

        Assert.True(
            unobserved.IsEmpty,
            $"TLS handshake timeout abandoned {unobserved.Count} authentication task(s); first: " +
            $"{unobserved.FirstOrDefault()?.Flatten().InnerExceptions.FirstOrDefault()?.Message}");
    }

    /// <summary>
    /// A genuine authentication failure (plaintext bytes against a TLS listener) must still be
    /// distinguishable from listener shutdown: it is counted as a TLS handshake failure and the
    /// connection fails, while shutdown classification is never produced.
    /// </summary>
    [Fact]
    public async Task GenuineTlsAuthenticationFailure_IsNotClassifiedAsShutdown()
    {
        using var certificate = TransportTestSupport.CreateSelfSignedCertificate();
        var outcomes = new ConcurrentQueue<string>();
        var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            Tls = new BoltTlsServerOptions(certificate),
            TlsHandshakeTimeout = TimeSpan.FromSeconds(10),
        });
        server.Diagnostics.Event += item =>
        {
            if (item.Kind is BoltTransportEventKind.ConnectionCompleted or BoltTransportEventKind.ConnectionFailed)
            {
                outcomes.Enqueue($"{item.Kind}:{item.Detail}");
            }
        };

        using (var client = TestBoltClient.Connect(server.BoundEndPoint!))
        {
            // A plaintext Bolt handshake against a TLS-only listener is a genuine TLS failure.
            client.SendRaw(TransportTestSupport.HandshakeFromVersions(new BoltProtocolVersion(5, 8)));
            Assert.Equal("closed", client.WaitForClose(10_000));
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, Bounded));
        Assert.True(server.Diagnostics.TlsHandshakeFailures >= 1);
        Assert.Equal(0, server.Diagnostics.BoltHandshakeFailures);
        Assert.Equal(BoltShutdownOutcome.NotRequested, server.ShutdownOutcome);
        Assert.DoesNotContain(outcomes, outcome => outcome.Contains("listener-shutdown", StringComparison.Ordinal));

        await server.DisposeAsync();
    }

    private static async Task ForceCollectionAsync()
    {
        for (var i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(50);
        }
    }

    private static async Task DrainUnobservedTaskExceptionsAsync()
    {
        EventHandler<UnobservedTaskExceptionEventArgs> drain = (_, args) => args.SetObserved();
        TaskScheduler.UnobservedTaskException += drain;
        try
        {
            await ForceCollectionAsync();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= drain;
        }
    }
}
