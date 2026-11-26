using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using BoltSharp.Transport;

namespace B5BQ1Probe;

/// <summary>
/// Independent adversarial control harness for the B5B-CI-RACE-REMED shutdown lifecycle.
/// Out-of-tree: references the built BoltSharp.dll and exercises only the public surface
/// (reflection is used solely for two controls that deliberately manipulate listener internals).
/// </summary>
internal static class Program
{
    private static X509Certificate2 _certificate = null!;

    private static async Task<int> Main(string[] args)
    {
        _certificate = CreateCertificate();
        var only = args.Length > 0 ? args[0] : null;

        await RunAsync(only, "ipv4-loopback-negotiation", () => NegotiationAsync(
            "ipv4-loopback-negotiation", IPAddress.Loopback, IPAddress.Loopback, tls: false, rounds: 10));
        await RunAsync(only, "ipv6-loopback-negotiation", () => NegotiationAsync(
            "ipv6-loopback-negotiation", IPAddress.IPv6Loopback, IPAddress.IPv6Loopback, tls: false, rounds: 10));
        await RunAsync(only, "ipv6any-tls", Ipv6AnyAsync);
        await RunAsync(only, "ipv4-wildcard-tls", () => NegotiationAsync(
            "ipv4-wildcard-tls", IPAddress.Any, IPAddress.Loopback, tls: true, rounds: 5));
        await RunAsync(only, "nic-address-tls", NicAddressAsync);
        await RunAsync(only, "short-budget-unaccepted", ShortBudgetAsync);
        await RunAsync(only, "race-delay", RaceDelayAsync);
        await RunAsync(only, "wake-failure", WakeFailureAsync);
        await RunAsync(only, "accept-reset-accounting", AcceptResetAccountingAsync);
        await RunAsync(only, "nic-short-budget", NicShortBudgetAsync);
        await RunAsync(only, "flood", () => FloodAsync());
        await RunAsync(only, "flood-aggressive", FloodAggressiveAsync);
        await RunAsync(only, "backlog-1-flood", () => FloodAsync(backlog: 1, workers: 8, attemptsPerWorker: 20, delayMs: 20));
        await RunAsync(only, "concurrent-dispose", ConcurrentDisposeAsync);
        await RunAsync(only, "external-listener-stop", ExternalListenerStopAsync);
        await RunAsync(only, "sentinel-port-ambiguity", SentinelAmbiguityAsync);
        await RunAsync(only, "tls-auth-cancel", TlsAuthCancelAsync);
        await RunAsync(only, "tls-abandoned-task", TlsAbandonedTaskAsync);
        await RunAsync(only, "tls-overloads", TlsOverloadsAsync);

        _certificate.Dispose();
        return 0;
    }

    private static async Task RunAsync(string? only, string name, Func<Task<string>> scenario)
    {
        if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var detail = await scenario();
            Console.WriteLine($"SCENARIO {name} RESULT {detail}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SCENARIO {name} EXCEPTION {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<string> NegotiationAsync(
        string name, IPAddress bind, IPAddress connectTo, bool tls, int rounds)
    {
        var results = new List<string>();
        for (var round = 0; round < rounds; round++)
        {
            var server = await StartAsync(bind, tls);
            try
            {
                using var client = Connect(new IPEndPoint(connectTo, server.BoundEndPoint!.Port));
                var (bounded, ms, error) = await StopBoundedAsync(server);
                var peer = WaitForClose(client, 5_000);
                results.Add(Describe(round, bounded, ms, error, peer, server));
                await server.DisposeAsync();
            }
            catch (Exception ex)
            {
                results.Add($"round={round} EX={ex.GetType().Name}:{ex.Message}");
                await SafeDisposeAsync(server);
            }
        }

        return string.Join(" ; ", results);
    }

    private static async Task<string> Ipv6AnyAsync()
    {
        var results = new List<string>();
        for (var round = 0; round < 5; round++)
        {
            var server = await StartAsync(IPAddress.IPv6Any, tls: true);
            try
            {
                var port = server.BoundEndPoint!.Port;
                using var v6 = Connect(new IPEndPoint(IPAddress.IPv6Loopback, port));
                TcpClient? v4 = null;
                string v4Connect;
                try
                {
                    v4 = Connect(new IPEndPoint(IPAddress.Loopback, port));
                    v4Connect = "accepted-by-listener";
                }
                catch (Exception ex)
                {
                    v4Connect = $"refused:{ex.GetType().Name}";
                }

                var (bounded, ms, error) = await StopBoundedAsync(server);
                var peer6 = WaitForClose(v6, 5_000);
                var peer4 = v4 is null ? "n/a" : WaitForClose(v4, 5_000);
                v4?.Dispose();
                results.Add(
                    $"round={round} dualStack={v4Connect} bounded={bounded} ms={ms} error={error ?? "none"} " +
                    $"peer6={peer6} peer4={peer4} {Tail(server)}");
                await server.DisposeAsync();
            }
            catch (Exception ex)
            {
                results.Add($"round={round} EX={ex.GetType().Name}:{ex.Message}");
                await SafeDisposeAsync(server);
            }
        }

        return string.Join(" ; ", results);
    }

    private static async Task<string> NicAddressAsync()
    {
        var address = FindNicAddress(AddressFamily.InterNetwork)
            ?? FindNicAddress(AddressFamily.InterNetworkV6);
        if (address is null)
        {
            return "SKIPPED no operational non-loopback unicast address";
        }

        var results = new List<string> { $"address={address}" };
        for (var round = 0; round < 3; round++)
        {
            var server = await StartAsync(address, tls: true);
            try
            {
                using var client = Connect(new IPEndPoint(address, server.BoundEndPoint!.Port));
                var (bounded, ms, error) = await StopBoundedAsync(server);
                var peer = WaitForClose(client, 5_000);
                results.Add(Describe(round, bounded, ms, error, peer, server));
                await server.DisposeAsync();
            }
            catch (Exception ex)
            {
                results.Add($"round={round} EX={ex.GetType().Name}:{ex.Message}");
                await SafeDisposeAsync(server);
            }
        }

        return string.Join(" ; ", results);
    }

    /// <summary>
    /// The peer is connected but the accept loop may not have accepted it yet when shutdown
    /// starts. With a budget far shorter than any scheduling guarantee this is the adversarial
    /// case for the sentinel design.
    /// </summary>
    private static async Task<string> ShortBudgetAsync()
    {
        var results = new List<string>();
        foreach (var budgetMs in new[] { 1, 5, 10, 50 })
        {
            var peers = new List<string>();
            var outcomes = new List<string>();
            for (var round = 0; round < 10; round++)
            {
                var server = await StartAsync(
                    IPAddress.Loopback, tls: false, shutdown: TimeSpan.FromMilliseconds(budgetMs));
                try
                {
                    using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
                    var (bounded, ms, error) = await StopBoundedAsync(server);
                    peers.Add(WaitForClose(client, 5_000));
                    outcomes.Add(
                        $"r{round}:bounded={bounded},ms={ms},err={error ?? "none"},{Tail(server)}");
                    await server.DisposeAsync();
                }
                catch (Exception ex)
                {
                    outcomes.Add($"r{round}:EX={ex.GetType().Name}");
                    await SafeDisposeAsync(server);
                }
            }

            results.Add(
                $"budget={budgetMs}ms peerClosed={peers.Count(p => p == "closed")}/{peers.Count} " +
                $"peerOpen={peers.Count(p => p == "open")} | {string.Join(" | ", outcomes)}");
        }

        // Also exercise an accepted connection with the same tiny budget (graceful phase only).
        foreach (var budgetMs in new[] { 10, 50 })
        {
            var outcomes = new List<string>();
            for (var round = 0; round < 5; round++)
            {
                var server = await StartAsync(
                    IPAddress.Loopback, tls: false, shutdown: TimeSpan.FromMilliseconds(budgetMs));
                try
                {
                    using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
                    await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 1, 2_000);
                    var (bounded, ms, error) = await StopBoundedAsync(server);
                    outcomes.Add(
                        $"r{round}:bounded={bounded},ms={ms},err={error ?? "none"},peer={WaitForClose(client, 5_000)},{Tail(server)}");
                    await server.DisposeAsync();
                }
                catch (Exception ex)
                {
                    outcomes.Add($"r{round}:EX={ex.GetType().Name}");
                    await SafeDisposeAsync(server);
                }
            }

            results.Add($"accepted budget={budgetMs}ms | {string.Join(" | ", outcomes)}");
        }

        return string.Join(" || ", results);
    }

    /// <summary>
    /// Sweeps the scheduling window between the TCP connect and the shutdown request. The
    /// historical defect depended on whether the accept completion was already in flight when
    /// the listening socket closed, so every delay/budget cell is sampled repeatedly.
    /// </summary>
    private static async Task<string> RaceDelayAsync()
    {
        var results = new List<string>();
        foreach (var delayMs in new[] { 0, 1, 2, 5, 10, 20 })
        {
            foreach (var budgetMs in new[] { 1, 5, 10, 100 })
            {
                var open = 0;
                var bounded = 0;
                var incomplete = 0;
                var total = 10;
                for (var round = 0; round < total; round++)
                {
                    var server = await StartAsync(
                        IPAddress.Loopback, tls: false, shutdown: TimeSpan.FromMilliseconds(budgetMs));
                    try
                    {
                        using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
                        if (delayMs > 0)
                        {
                            await Task.Delay(delayMs);
                        }

                        var (ok, _, _) = await StopBoundedAsync(server);
                        if (ok)
                        {
                            bounded++;
                        }

                        if (server.ShutdownOutcome == BoltShutdownOutcome.Incomplete)
                        {
                            incomplete++;
                        }

                        if (WaitForClose(client, 5_000) == "open")
                        {
                            open++;
                        }

                        await server.DisposeAsync();
                    }
                    catch (Exception)
                    {
                        await SafeDisposeAsync(server);
                    }
                }

                results.Add(
                    $"delay={delayMs}ms budget={budgetMs}ms bounded={bounded}/{total} " +
                    $"incomplete={incomplete} peerOpen={open}/{total}");
            }
        }

        return string.Join(" ; ", results);
    }

    /// <summary>
    /// Simulates the bound address becoming unreachable (NIC down): the wake connect cannot reach
    /// the listener from either the primary or the loopback fallback. This is the degraded path
    /// where the implementation must still be bounded and must not claim a clean stop.
    /// </summary>
    private static async Task<string> WakeFailureAsync()
    {
        var address = FindNicAddress(AddressFamily.InterNetwork);
        if (address is null)
        {
            return "SKIPPED no operational non-loopback IPv4 address";
        }

        var results = new List<string> { $"bind={address}" };
        foreach (var delayMs in new[] { 0, 1, 2, 5, 10, 20 })
        {
            var open = 0;
            var bounded = 0;
            var incomplete = 0;
            const int total = 5;
            for (var round = 0; round < total; round++)
            {
                var server = await StartAsync(address, tls: true, shutdown: TimeSpan.FromSeconds(1));
                TcpClient? pending = null;
                try
                {
                    using var accepted = Connect(new IPEndPoint(address, server.BoundEndPoint!.Port));
                    await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 1, 2_000);

                    var port = server.BoundEndPoint!.Port;
                    typeof(BoltTcpServer)
                        .GetProperty("BoundEndPoint", BindingFlags.Public | BindingFlags.Instance)!
                        .SetValue(server, new IPEndPoint(IPAddress.Parse("192.0.2.1"), port));

                    pending = Connect(new IPEndPoint(address, port));
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs);
                    }

                    var (ok, _, _) = await StopBoundedAsync(server, 5_000);
                    if (ok)
                    {
                        bounded++;
                    }

                    if (server.ShutdownOutcome == BoltShutdownOutcome.Incomplete)
                    {
                        incomplete++;
                    }

                    if (WaitForClose(pending, 5_000) == "open")
                    {
                        open++;
                    }

                    await server.DisposeAsync();
                }
                catch (Exception)
                {
                    await SafeDisposeAsync(server);
                }
                finally
                {
                    pending?.Dispose();
                }
            }

            results.Add(
                $"delay={delayMs}ms bounded={bounded}/{total} incomplete={incomplete} " +
                $"pendingPeerOpen={open}/{total}");
        }

        return string.Join(" ; ", results);
    }

    /// <summary>
    /// A client that resets before the accept completes can make AcceptTcpClientAsync throw.
    /// The accept loop reports that fault as ConnectionFailed, which decrements ConnectionsActive
    /// even though no ConnectionAccepted was ever reported. This control looks for the resulting
    /// negative active count / unbalanced accounting.
    /// </summary>
    private static async Task<string> AcceptResetAccountingAsync()
    {
        var results = new List<string>();
        for (var round = 0; round < 10; round++)
        {
            var server = await StartAsync(IPAddress.Loopback, tls: false);
            var port = server.BoundEndPoint!.Port;
            for (var i = 0; i < 100; i++)
            {
                try
                {
                    var client = new TcpClient(AddressFamily.InterNetwork);
                    client.Connect(new IPEndPoint(IPAddress.Loopback, port));
                    client.Client.LingerState = new LingerOption(true, 0);
                    client.Client.Close();
                    client.Dispose();
                }
                catch (Exception)
                {
                    // A refused connect after a race is not interesting here.
                }

                if (i % 10 == 0)
                {
                    await Task.Delay(1);
                }
            }

            await Task.Delay(100);
            var accepted = server.Diagnostics.ConnectionsAccepted;
            var completed = server.Diagnostics.ConnectionsCompleted;
            var failed = server.Diagnostics.ConnectionsFailed;
            var active = server.Diagnostics.ConnectionsActive;
            var (bounded, ms, error) = await StopBoundedAsync(server);
            results.Add(
                $"round={round} accepted={accepted} completed={completed} failed={failed} " +
                $"active={active} balanced={accepted == completed + failed} " +
                $"bounded={bounded} ms={ms} error={error ?? "none"} outcome={server.ShutdownOutcome}");
            await server.DisposeAsync();
        }

        return string.Join(" ; ", results);
    }

    private static async Task<string> NicShortBudgetAsync()
    {
        var address = FindNicAddress(AddressFamily.InterNetwork);
        if (address is null)
        {
            return "SKIPPED no operational non-loopback IPv4 address";
        }

        var results = new List<string> { $"bind={address}" };
        foreach (var delayMs in new[] { 0, 2 })
        {
            foreach (var budgetMs in new[] { 50, 100, 250, 500 })
            {
                var open = 0;
                var bounded = 0;
                var incomplete = 0;
                const int total = 10;
                for (var round = 0; round < total; round++)
                {
                    var server = await StartAsync(
                        address, tls: true, shutdown: TimeSpan.FromMilliseconds(budgetMs));
                    try
                    {
                        using var client = Connect(new IPEndPoint(address, server.BoundEndPoint!.Port));
                        if (delayMs > 0)
                        {
                            await Task.Delay(delayMs);
                        }

                        var (ok, _, _) = await StopBoundedAsync(server, 5_000);
                        if (ok)
                        {
                            bounded++;
                        }

                        if (server.ShutdownOutcome == BoltShutdownOutcome.Incomplete)
                        {
                            incomplete++;
                        }

                        if (WaitForClose(client, 5_000) == "open")
                        {
                            open++;
                        }

                        await server.DisposeAsync();
                    }
                    catch (Exception)
                    {
                        await SafeDisposeAsync(server);
                    }
                }

                results.Add(
                    $"delay={delayMs}ms budget={budgetMs}ms bounded={bounded}/{total} " +
                    $"incomplete={incomplete} peerOpen={open}/{total}");
            }
        }

        return string.Join(" ; ", results);
    }

    private static async Task<string> FloodAggressiveAsync()
    {
        var server = await StartAsync(
            IPAddress.Loopback, tls: false, shutdown: TimeSpan.FromSeconds(5), backlog: 128);
        var port = server.BoundEndPoint!.Port;
        var clients = new ConcurrentBag<TcpClient>();
        var stop = new CancellationTokenSource();
        var attempts = 0;

        var tasks = Enumerable.Range(0, 32).Select(async _ =>
        {
            while (!stop.IsCancellationRequested && Volatile.Read(ref attempts) < 2_000)
            {
                Interlocked.Increment(ref attempts);
                var client = await TryConnectAsync(new IPEndPoint(IPAddress.Loopback, port), 500);
                if (client is not null)
                {
                    clients.Add(client);
                }
            }
        }).ToArray();

        await Task.Delay(200);
        var (bounded, ms, error) = await StopBoundedAsync(server, 10_000);
        stop.Cancel();
        await Task.WhenAll(tasks);

        var states = clients.Select(client =>
        {
            var state = WaitForClose(client, 5_000);
            client.Dispose();
            return state;
        }).ToArray();

        var result =
            $"attempts={attempts} connected={clients.Count} bounded={bounded} ms={ms} " +
            $"error={error ?? "none"} peersClosed={states.Count(s => s == "closed")} " +
            $"peersOpen={states.Count(s => s == "open")} {Tail(server)}";
        await server.DisposeAsync();
        return result;
    }

    private static async Task<string> FloodAsync(
        int backlog = 128, int workers = 8, int attemptsPerWorker = 40, int delayMs = 30)
    {
        var server = await StartAsync(
            IPAddress.Loopback, tls: false, shutdown: TimeSpan.FromSeconds(5), backlog: backlog);
        var clients = new ConcurrentBag<TcpClient>();
        var connectErrors = new ConcurrentBag<string>();
        var stop = new CancellationTokenSource();
        var total = 0;
        var port = server.BoundEndPoint!.Port;

        var tasks = Enumerable.Range(0, workers).Select(async _ =>
        {
            for (var i = 0; i < attemptsPerWorker && !stop.IsCancellationRequested; i++)
            {
                if (Interlocked.Increment(ref total) > workers * attemptsPerWorker)
                {
                    break;
                }

                var client = await TryConnectAsync(new IPEndPoint(IPAddress.Loopback, port), 1_000);
                if (client is null)
                {
                    connectErrors.Add("connect-failed");
                }
                else
                {
                    clients.Add(client);
                }

                await Task.Delay(1);
            }
        }).ToArray();

        await Task.Delay(delayMs);
        var (bounded, ms, error) = await StopBoundedAsync(server, 10_000);
        stop.Cancel();
        await Task.WhenAll(tasks);

        var peerStates = clients.Select(client =>
        {
            var state = WaitForClose(client, 5_000);
            client.Dispose();
            return state;
        }).ToArray();

        var result =
            $"backlog={backlog} workers={workers} attempts={clients.Count} connectFailures={connectErrors.Count} " +
            $"bounded={bounded} ms={ms} error={error ?? "none"} peersClosed={peerStates.Count(s => s == "closed")} " +
            $"peersOpen={peerStates.Count(s => s == "open")} {Tail(server)}";
        await server.DisposeAsync();
        return result;
    }

    private static async Task<string> ConcurrentDisposeAsync()
    {
        var results = new List<string>();
        for (var round = 0; round < 5; round++)
        {
            var server = await StartAsync(IPAddress.Loopback, tls: false);
            using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
            await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 1, 2_000);

            var tasks = new List<Task>();
            for (var i = 0; i < 8; i++)
            {
                tasks.Add(Task.Run(async () => await server.StopAsync()));
                tasks.Add(Task.Run(async () => await server.DisposeAsync()));
            }

            var all = Task.WhenAll(tasks);
            var done = await Task.WhenAny(all, Task.Delay(10_000));
            var error = done == all ? null : "timeout";
            if (done == all)
            {
                try
                {
                    await all;
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ":" + ex.Message;
                }
            }

            results.Add(
                $"round={round} bounded={done == all} error={error ?? "none"} " +
                $"peer={WaitForClose(client, 5_000)} {Tail(server)}");
        }

        return string.Join(" ; ", results);
    }

    /// <summary>
    /// Simulates the accept loop losing its listener outside shutdown (the closest externally
    /// inducible accept-loop fault). Shutdown must still be bounded, non-throwing and own the
    /// already-accepted connection.
    /// </summary>
    private static async Task<string> ExternalListenerStopAsync()
    {
        var results = new List<string>();
        for (var round = 0; round < 3; round++)
        {
            var server = await StartAsync(IPAddress.Loopback, tls: false);
            using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
            await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 1, 2_000);

            var listener = (TcpListener)typeof(BoltTcpServer)
                .GetField("_listener", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(server)!;
            listener.Stop();
            await Task.Delay(50);

            var (bounded, ms, error) = await StopBoundedAsync(server);
            results.Add(
                $"round={round} bounded={bounded} ms={ms} error={error ?? "none"} " +
                $"peer={WaitForClose(client, 5_000)} {Tail(server)}");
            await server.DisposeAsync();
        }

        return string.Join(" ; ", results);
    }

    /// <summary>
    /// A real client whose ephemeral source port collides with a registered wake port is
    /// indistinguishable from the sentinel by port alone. This control injects that collision to
    /// establish that the ambiguity cannot hang shutdown or leak the client.
    /// </summary>
    private static async Task<string> SentinelAmbiguityAsync()
    {
        var results = new List<string>();
        for (var round = 0; round < 3; round++)
        {
            var server = await StartAsync(IPAddress.Loopback, tls: false);
            var client = new TcpClient(AddressFamily.InterNetwork);
            client.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var sourcePort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

            var wakePorts = (ConcurrentDictionary<int, byte>)typeof(BoltTcpServer)
                .GetField("_wakePorts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(server)!;
            wakePorts[sourcePort] = 0;

            // Make the accept loop observe the stopping branch without starting real shutdown.
            typeof(BoltTcpServer)
                .GetField("_stopping", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(server, 1);

            try
            {
                client.Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
                await Task.Delay(50);
                var (bounded, ms, error) = await StopBoundedAsync(server);
                results.Add(
                    $"round={round} port={sourcePort} bounded={bounded} ms={ms} error={error ?? "none"} " +
                    $"peer={WaitForClose(client, 5_000)} {Tail(server)}");
            }
            catch (Exception ex)
            {
                results.Add($"round={round} EX={ex.GetType().Name}:{ex.Message}");
            }
            finally
            {
                client.Dispose();
                await SafeDisposeAsync(server);
            }
        }

        return string.Join(" ; ", results);
    }

    private static async Task<string> TlsAuthCancelAsync()
    {
        var results = new List<string>();
        for (var round = 0; round < 5; round++)
        {
            var server = await StartAsync(IPAddress.Loopback, tls: true);
            try
            {
                using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
                await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 1, 2_000);
                var (bounded, ms, error) = await StopBoundedAsync(server);
                results.Add(
                    $"round={round} bounded={bounded} ms={ms} error={error ?? "none"} " +
                    $"peer={WaitForClose(client, 5_000)} tlsFailures={server.Diagnostics.TlsHandshakeFailures} " +
                    $"{Tail(server)}");
                await server.DisposeAsync();
            }
            catch (Exception ex)
            {
                results.Add($"round={round} EX={ex.GetType().Name}:{ex.Message}");
                await SafeDisposeAsync(server);
            }
        }

        return string.Join(" ; ", results);
    }

    /// <summary>
    /// The TLS shutdown path awaits AuthenticateAsServerAsync(...).WaitAsync(token). WaitAsync
    /// stops the wait, not the underlying handshake; the handshake task is abandoned and only
    /// aborted by disposing the stream. This control counts unobserved task faults raised after
    /// forced collection.
    /// </summary>
    private static async Task<string> TlsAbandonedTaskAsync()
    {
        var unobserved = new ConcurrentBag<AggregateException>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            unobserved.Add(args.Exception);
            args.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += Handler;
        var runs = 0;
        try
        {
            for (var round = 0; round < 100; round++)
            {
                var server = await StartAsync(IPAddress.Loopback, tls: true);
                try
                {
                    using var client = Connect(new IPEndPoint(IPAddress.Loopback, server.BoundEndPoint!.Port));
                    await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 1, 2_000);
                    var (bounded, _, _) = await StopBoundedAsync(server);
                    if (bounded)
                    {
                        runs++;
                    }

                    await server.DisposeAsync();
                }
                catch (Exception)
                {
                    await SafeDisposeAsync(server);
                }
            }

            for (var i = 0; i < 5; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        var flattened = unobserved.SelectMany(exception => exception.Flatten().InnerExceptions).ToArray();
        var types = flattened
            .GroupBy(exception => exception.GetType().Name)
            .Select(group => $"{group.Key}:{group.Count()}");
        var samples = flattened
            .Take(3)
            .Select(exception =>
                $"{exception.GetType().Name}:{exception.Message} @ " +
                $"{exception.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
        return $"runs={runs}/100 unobserved={unobserved.Count} types=[{string.Join(",", types)}] " +
               $"samples=[{string.Join(" || ", samples)}]";
    }

    private static Task<string> TlsOverloadsAsync()
    {
        var overloads = typeof(System.Net.Security.SslStream)
            .GetMethods()
            .Where(method => method.Name == "AuthenticateAsServerAsync")
            .Select(method =>
                $"({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})")
            .Distinct()
            .ToArray();
        return Task.FromResult(string.Join(" ", overloads));
    }

    private static async Task<BoltTcpServer> StartAsync(
        IPAddress bind,
        bool tls,
        TimeSpan? shutdown = null,
        int backlog = 128,
        int maxConnections = 100)
    {
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = bind,
            Backlog = backlog,
            MaxConnections = maxConnections,
            ShutdownTimeout = shutdown ?? TimeSpan.FromSeconds(5),
            Tls = tls ? new BoltTlsServerOptions(_certificate) : null,
            TlsHandshakeTimeout = TimeSpan.FromSeconds(30),
            HandshakeTimeout = TimeSpan.FromSeconds(30),
        });
        await server.StartAsync();
        return server;
    }

    private static TcpClient Connect(IPEndPoint endpoint)
    {
        var client = new TcpClient(endpoint.AddressFamily);
        client.Connect(endpoint);
        return client;
    }

    private static async Task<TcpClient?> TryConnectAsync(IPEndPoint endpoint, int timeoutMs)
    {
        var client = new TcpClient(endpoint.AddressFamily);
        try
        {
            var connect = client.ConnectAsync(endpoint);
            var done = await Task.WhenAny(connect, Task.Delay(timeoutMs));
            if (done != connect)
            {
                client.Dispose();
                return null;
            }

            await connect;
            return client;
        }
        catch (Exception)
        {
            client.Dispose();
            return null;
        }
    }

    private static async Task<(bool Bounded, long Ms, string? Error)> StopBoundedAsync(
        BoltTcpServer server, int timeoutMs = 10_000)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var stop = server.StopAsync();
            var done = await Task.WhenAny(stop, Task.Delay(timeoutMs));
            stopwatch.Stop();
            if (done != stop)
            {
                return (false, stopwatch.ElapsedMilliseconds, "timeout");
            }

            await stop;
            return (true, stopwatch.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return (false, stopwatch.ElapsedMilliseconds, $"{ex.GetType().Name}:{ex.Message}");
        }
    }

    /// <summary>Mirrors the repository's WaitForClose: poll, then read one byte.</summary>
    private static string WaitForClose(TcpClient client, int timeoutMs)
    {
        try
        {
            if (!client.Client.Poll(timeoutMs * 1_000, SelectMode.SelectRead))
            {
                return "open";
            }

            var buffer = new byte[1];
            return client.Client.Receive(buffer, 0, 1, SocketFlags.None) == 0 ? "closed" : "open";
        }
        catch (Exception)
        {
            return "closed";
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(5);
        }

        return condition();
    }

    private static async Task SafeDisposeAsync(BoltTcpServer server)
    {
        try
        {
            await server.DisposeAsync();
        }
        catch (Exception)
        {
            // Best effort for the probe.
        }
    }

    private static string Describe(
        int round, bool bounded, long ms, string? error, string peer, BoltTcpServer server) =>
        $"round={round} bounded={bounded} ms={ms} error={error ?? "none"} peer={peer} {Tail(server)}";

    private static string Tail(BoltTcpServer server) =>
        $"outcome={server.ShutdownOutcome} active={server.Diagnostics.ConnectionsActive} " +
        $"accepted={server.Diagnostics.ConnectionsAccepted} " +
        $"terminal={server.Diagnostics.ConnectionsCompleted + server.Diagnostics.ConnectionsFailed}";

    private static IPAddress? FindNicAddress(AddressFamily family)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != family || IPAddress.IsLoopback(address))
                {
                    continue;
                }

                if (family == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal)
                {
                    continue;
                }

                return address;
            }
        }

        return null;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }
}
