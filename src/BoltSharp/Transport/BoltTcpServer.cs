using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace BoltSharp.Transport;

/// <summary>
/// A bounded, asynchronous TCP/TLS listener that exposes the accepted Bolt 5.8 protocol engine
/// (B2 handshake and framing, B3-A codec, B3-B session, B3-C reference backend) over real
/// network sockets.
/// <para>
/// <b>Lifecycle.</b> An instance is single-use: Start binds and begins accepting; Stop (or
/// Dispose) stops accepting, signals active connections and awaits their tasks within a bounded
/// shutdown policy. Restart is not supported — create a new instance.
/// </para>
/// <para>
/// <b>Admission and isolation.</b> At most MaxConnections connections are active; excess
/// connections are closed immediately upon acceptance. Every connection owns its own B3-B
/// session, framing state and cancellation context. Failures in one connection never affect
/// the listener or other connections.
/// </para>
/// <para>
/// <b>Ownership.</b> An accepted socket is registered under server ownership before its
/// connection pipeline can run, and remains registered until its termination is established.
/// Shutdown therefore sees every connection the listener accepted, can terminate it gracefully
/// or forcibly, and reports incomplete termination instead of hiding it.
/// </para>
/// <para>
/// <b>Acceptance quiescence.</b> Abandoning an asynchronous accept — cancelling it or closing the
/// listening socket underneath it — can orphan a socket the kernel has already accepted: the peer
/// then stays ESTABLISHED with no FIN and no RST, forever. The accept loop therefore never runs a
/// cancellable accept. Shutdown releases the pending accept with a loopback wake connection and
/// closes the listening socket only after that accept has been retrieved and disposed. Connections
/// that arrive during the drain are closed by the loop, so acceptance can never be missed.
/// </para>
/// <para>
/// <b>Secure defaults.</b> A plaintext listener must bind to a loopback address (validated at
/// construction); TLS requires an explicitly configured certificate with a private key and never
/// downgrades to plaintext.
/// </para>
/// </summary>
public sealed class BoltTcpServer : IAsyncDisposable
{
    /// <summary>
    /// Bound for the loopback wake connect used to release a pending accept. The connect is local
    /// and completes immediately; the bound only guards an unreachable bind address.
    /// </summary>
    private static readonly TimeSpan WakeConnectBudget = TimeSpan.FromMilliseconds(250);

    /// <summary>Per-attempt bound for one loopback wake connect.</summary>
    private static readonly TimeSpan WakeAttemptBudget = TimeSpan.FromMilliseconds(50);

    /// <summary>Interval between wake attempts while the accept loop has not quiesced.</summary>
    private static readonly TimeSpan WakeRepeatInterval = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// The tail of the shutdown budget reserved for forced socket termination, expressed as a
    /// fraction. Graceful cancellation can never consume the whole budget and leave no time for
    /// the forced phase.
    /// </summary>
    private const int ForcedTerminationReserveDivisor = 5;

    private readonly BoltTransportDiagnostics _diagnostics = new();

    /// <summary>
    /// The single connection ownership registry: every accepted connection is added before its
    /// pipeline can start and removed only after its socket is closed and its task completed.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();

    /// <summary>
    /// Local ports of the loopback wake connections. An accept whose peer port is registered here
    /// is a shutdown sentinel, not a client, and terminates the drain.
    /// </summary>
    private readonly ConcurrentDictionary<int, byte> _wakePorts = new();

    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly CancellationToken _shutdownToken;
    private readonly object _shutdownGate = new();
    private readonly BoltPlaintextTerminalClose? _plaintextClose;

    private TcpListener? _listener;
    private Task? _acceptLoopTask;
    private Task? _shutdownTask;
    private int _nextConnectionId;
    private int _stopping;

    private BoltTcpServer(BoltTcpServerOptions options)
    {
        Options = options;
        Options.Validate();
        Diagnostics = _diagnostics;
        _shutdownToken = _shutdownCts.Token;
        _plaintextClose = options.Tls is null ? new BoltPlaintextTerminalClose(options.MaxConnections) : null;
    }

    /// <summary>Creates and validates a server configuration.</summary>
    public static BoltTcpServer Create(BoltTcpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BoltTcpServer(options);
    }

    /// <summary>The validated server options.</summary>
    public BoltTcpServerOptions Options { get; }

    /// <summary>The diagnostic surface (counters and optional event observer).</summary>
    public BoltTransportDiagnostics Diagnostics { get; }

    /// <summary>The actual bound endpoint; available after Start.</summary>
    public IPEndPoint? BoundEndPoint { get; private set; }

    /// <summary>Whether the listener is currently accepting connections.</summary>
    public bool IsRunning => Volatile.Read(ref _stopping) == 0 && _listener is not null;

    /// <summary>
    /// The outcome of the last shutdown request. <see cref="BoltShutdownOutcome.Incomplete"/>
    /// makes bounded-but-unfinished termination observable to callers instead of leaving it only
    /// in diagnostic logs.
    /// </summary>
    public BoltShutdownOutcome ShutdownOutcome { get; private set; } = BoltShutdownOutcome.NotRequested;

    /// <summary>
    /// Binds the configured endpoint and starts the accept loop. The actual bound endpoint (an
    /// ephemeral port when Port is 0) is reported via BoundEndPoint.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _stopping) != 0 || _listener is not null)
        {
            throw new InvalidOperationException(
                "The server has already been started; a BoltTcpServer instance is single-use.");
        }

        _listener = new TcpListener(Options.BindAddress, Options.Port);
        _listener.Server.LingerState = new LingerOption(false, 0);
        _listener.Start(Options.Backlog);
        BoundEndPoint = (IPEndPoint)_listener.LocalEndpoint;

        _acceptLoopTask = Task.Run(AcceptLoopAsync);
        _diagnostics.Report(
            BoltTransportEventKind.ListenerStarted,
            "listener",
            $"{BoundEndPoint} tls={(Options.Tls is not null).ToString()}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops accepting connections, signals active connections and awaits their completion within
    /// the configured bounded shutdown policy. Idempotent and safe under concurrent calls: every
    /// caller observes the same shutdown completion. A caller cancellation abandons only that
    /// caller's wait, never the shutdown itself.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var shutdown = GetOrStartShutdown();
        return cancellationToken.CanBeCanceled ? shutdown.WaitAsync(cancellationToken) : shutdown;
    }

    private Task GetOrStartShutdown()
    {
        lock (_shutdownGate)
        {
            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            // Observable synchronously: IsRunning and StartAsync must never race ahead of the
            // shutdown request even though the shutdown work itself is scheduled.
            Interlocked.Exchange(ref _stopping, 1);
            _shutdownTask = Task.Run(ShutdownAsync);
            return _shutdownTask;
        }
    }

    private async Task ShutdownAsync()
    {
        var started = Stopwatch.GetTimestamp();
        var acceptQuiesced = true;

        // 1. Graceful cancellation. The linked token reaches every negotiation, read, write and
        //    TLS authentication already in progress.
        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A previous DisposeAsync already released the source.
        }

        // 2. Quiesce acceptance before closing the listening socket. The accept loop never runs a
        //    cancellable accept; a loopback wake connection releases the pending accept and the
        //    loop drains anything that raced with shutdown.
        var acceptLoop = _acceptLoopTask;
        if (acceptLoop is not null)
        {
            await WakeAcceptLoopAsync(acceptLoop, Remaining(started)).ConfigureAwait(false);
            acceptQuiesced = await WaitForTasksAsync([acceptLoop], Remaining(started)).ConfigureAwait(false);
        }

        // Only reached with no accept outstanding (or, in the degraded wake-failure case, as the
        // last resort that unblocks the loop).
        _listener?.Stop();

        if (acceptLoop is not null && !acceptQuiesced)
        {
            acceptQuiesced = await WaitForTasksAsync([acceptLoop], Remaining(started)).ConfigureAwait(false);
        }

        if (acceptLoop is { IsFaulted: true })
        {
            // Observe an unexpected accept-loop fault so it can never surface as an unobserved
            // task exception; the shutdown outcome below still reports acceptance state.
            _ = acceptLoop.Exception;
        }

        // 3. Graceful join. The tail of the budget stays reserved so forced termination always has
        //    time to run inside the configured contract.
        var remaining = Remaining(started);
        var graceful = await WaitForConnectionsAsync(
                OutstandingConnections(), remaining - ForcedTerminationReserve(remaining))
            .ConfigureAwait(false);

        // 4. Forced socket termination for whatever graceful cancellation could not release.
        if (!graceful)
        {
            foreach (var entry in _connections.Values)
            {
                entry.Terminate();
            }

            await WaitForConnectionsAsync(OutstandingConnections(), Remaining(started)).ConfigureAwait(false);
        }

        var outstanding = OutstandingConnections();
        ShutdownOutcome = outstanding.Length == 0 && acceptQuiesced
            ? BoltShutdownOutcome.Completed
            : BoltShutdownOutcome.Incomplete;

        _diagnostics.Report(
            BoltTransportEventKind.ListenerStopped,
            "listener",
            $"activeRemaining={outstanding.Length}" +
            (acceptQuiesced ? "" : " accept=outstanding") +
            (outstanding.Length == 0 ? "" : " shutdown=incomplete"));
    }

    private TimeSpan Remaining(long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started);
        return elapsed >= Options.ShutdownTimeout ? TimeSpan.Zero : Options.ShutdownTimeout - elapsed;
    }

    private static TimeSpan ForcedTerminationReserve(TimeSpan remaining) =>
        TimeSpan.FromTicks(remaining.Ticks / ForcedTerminationReserveDivisor);

    private ConnectionEntry[] OutstandingConnections() =>
        _connections.Values.Where(entry => !entry.Task.IsCompleted).ToArray();

    private static Task<bool> WaitForConnectionsAsync(ConnectionEntry[] entries, TimeSpan timeout) =>
        WaitForTasksAsync(entries.Select(entry => entry.Task).ToArray(), timeout);

    private static async Task<bool> WaitForTasksAsync(Task[] tasks, TimeSpan timeout)
    {
        if (tasks.Length == 0 || tasks.All(task => task.IsCompleted))
        {
            return true;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception)
        {
            // Task.WhenAll completes only after every task completed; a fault is therefore
            // termination. The fault itself is observed by the retirement continuation.
            return true;
        }
    }

    /// <summary>
    /// Releases a pending accept by connecting a sentinel to the listener. The accept loop
    /// disposes the sentinel and exits, so shutdown never closes the listening socket with an
    /// accept outstanding. Best effort and bounded: a failed wake is reported as unquiesced
    /// acceptance rather than silently assumed successful.
    /// <para>
    /// The sentinel is bound to an ephemeral port and that port is recorded <i>before</i> the
    /// connect, because the accept loop can observe the connection before this side's connect
    /// completes. The wake is repeated at a short interval until the accept loop exits, so a
    /// sentinel that loses a scheduling race cannot leave shutdown waiting for the full budget.
    /// </para>
    /// </summary>
    private async Task WakeAcceptLoopAsync(Task? acceptLoop, TimeSpan budget)
    {
        if (acceptLoop is null || acceptLoop.IsCompleted)
        {
            return;
        }

        var endpoints = WakeEndPoints();
        if (endpoints.Length == 0)
        {
            return;
        }

        var wakeBudget = budget < WakeConnectBudget ? budget : WakeConnectBudget;
        if (wakeBudget <= TimeSpan.Zero)
        {
            wakeBudget = TimeSpan.FromMilliseconds(1);
        }

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            foreach (var endpoint in endpoints)
            {
                if (acceptLoop.IsCompleted)
                {
                    return;
                }

                await TryConnectAsync(endpoint, WakeAttemptBudget).ConfigureAwait(false);
            }

            if (acceptLoop.IsCompleted)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= wakeBudget)
            {
                return;
            }

            var pause = wakeBudget - elapsed;
            await Task.Delay(pause < WakeRepeatInterval ? pause : WakeRepeatInterval).ConfigureAwait(false);
        }
    }

    private async Task TryConnectAsync(IPEndPoint endpoint, TimeSpan timeout)
    {
        Socket? socket = null;
        int? port = null;
        try
        {
            socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(
                endpoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
                0));
            if (socket.LocalEndPoint is IPEndPoint local)
            {
                // Recorded before the handshake so the accept loop always recognises the sentinel.
                port = local.Port;
                _wakePorts[local.Port] = 0;
            }

            using var connect = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(endpoint, connect.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: shutdown proceeds and reports whether acceptance quiesced.
            if (port is { } failed)
            {
                _wakePorts.TryRemove(failed, out _);
            }
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private IPEndPoint[] WakeEndPoints()
    {
        if (BoundEndPoint is null)
        {
            return [];
        }

        var address = BoundEndPoint.Address;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            address = LoopbackFor(address.AddressFamily);
        }

        var primary = new IPEndPoint(address, BoundEndPoint.Port);
        var loopback = LoopbackFor(address.AddressFamily);
        if (address.Equals(loopback))
        {
            return [primary];
        }

        // A bind address that is not reachable from this host must not leave the accept pending;
        // loopback of the same family is the portable fallback.
        return [primary, new IPEndPoint(loopback, BoundEndPoint.Port)];
    }

    private static IPAddress LoopbackFor(AddressFamily family) =>
        family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;

    internal Task<bool> CancelPlaintextCloseAsync(string connectionId)
        => _plaintextClose?.CancelConnectionAsync(connectionId) ?? Task.FromResult(false);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (ShutdownOutcome == BoltShutdownOutcome.Completed)
        {
            // Only safe once no connection can still link to this source's token.
            _shutdownCts.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        var token = _shutdownToken;
        while (true)
        {
            TcpClient client;
            try
            {
                // Deliberately not cancellable. Abandoning an asynchronous accept (cancelling it
                // or closing the listener underneath it) can orphan the socket the kernel has
                // already accepted, leaving the peer ESTABLISHED forever.
                client = await listener.AcceptTcpClientAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex) when (IsListenerStopped(ex))
            {
                return;
            }
            catch (Exception ex)
            {
                // A transient accept fault must not silently kill the listener. It is a
                // listener-level event, not a connection outcome: reporting it as a connection
                // failure would decrement active accounting for a connection that was never
                // accepted (B5B-CI-RACE-REMED-01-R2-Q1/F-02).
                _diagnostics.Report(BoltTransportEventKind.ListenerFault, "listener", Describe(ex));
                await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            if (Volatile.Read(ref _stopping) != 0)
            {
                // Accepted during shutdown: the peer is closed here rather than registered, so
                // acceptance can never be missed. The loop keeps draining until the sentinel that
                // released the final pending accept has been consumed.
                var sentinel = IsSentinel(client);
                DisposeQuietly(client);
                if (!sentinel)
                {
                    continue;
                }

                DrainQueuedConnections(listener);
                return;
            }

            var connectionId = $"conn-{Interlocked.Increment(ref _nextConnectionId):0000}";
            if (_connections.Count >= Options.MaxConnections)
            {
                // Bounded admission: refuse safely by closing the accepted socket immediately.
                _diagnostics.Report(
                    BoltTransportEventKind.ConnectionRejectedCapacity, connectionId, "capacity reached");
                DisposeQuietly(client);
                continue;
            }

            _diagnostics.Report(
                BoltTransportEventKind.ConnectionAccepted, connectionId, $"{client.Client.RemoteEndPoint}");

            try
            {
                client.NoDelay = true;
                Register(client, connectionId, token);
            }
            catch (Exception ex)
            {
                // Ownership failed: close the accepted socket immediately and account for it.
                _diagnostics.Report(BoltTransportEventKind.ConnectionFailed, connectionId, Describe(ex));
                DisposeQuietly(client);
            }
        }
    }

    /// <summary>
    /// Disposes connections that are already queued at the listening socket. No accept is left
    /// outstanding afterwards, so the subsequent listener close can only reset connections that
    /// arrive later instead of orphaning an accepted socket.
    /// </summary>
    private void DrainQueuedConnections(TcpListener listener)
    {
        while (true)
        {
            bool pending;
            try
            {
                pending = listener.Pending();
            }
            catch (Exception)
            {
                return;
            }

            if (!pending)
            {
                return;
            }

            TcpClient queued;
            try
            {
                // Pending() reported a connection, so this accept completes without waiting.
                queued = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            DisposeQuietly(queued);
        }
    }

    private bool IsSentinel(TcpClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint is IPEndPoint remote
                && _wakePorts.TryRemove(remote.Port, out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void DisposeQuietly(TcpClient client)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception)
        {
            // A socket already closed elsewhere is a successful outcome.
        }
    }

    /// <summary>
    /// Publishes the connection under server ownership and only then lets its pipeline run. The
    /// pipeline task exists before the registry entry is visible, so shutdown can always await a
    /// registered connection even when it was terminated before startup.
    /// </summary>
    private void Register(TcpClient client, string connectionId, CancellationToken cancellationToken)
    {
        var entry = new ConnectionEntry(connectionId, client);
        entry.Task = Task.Run(async () =>
        {
            await entry.WaitForStartAsync().ConfigureAwait(false);
            if (entry.IsTerminated)
            {
                // Ownership terminated the socket before the pipeline started; keep the
                // connection accounting balanced because ConnectionAccepted was reported.
                _diagnostics.Report(
                    BoltTransportEventKind.ConnectionCompleted, connectionId, "listener-shutdown-before-start");
                return;
            }

            await BoltTcpConnection.RunAsync(
                    client, Options, _diagnostics, connectionId, _plaintextClose, cancellationToken)
                .ConfigureAwait(false);
        });

        if (!_connections.TryAdd(connectionId, entry))
        {
            entry.Terminate();
            return;
        }

        _ = entry.Task.ContinueWith(
            task => RetireConnection(entry, task),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        entry.Start();
    }

    /// <summary>
    /// Retires a connection: the socket is closed before the entry leaves the registry, so
    /// ownership is retained until termination is established. A fault that escaped the pipeline
    /// is observed here rather than silently dropped.
    /// </summary>
    private void RetireConnection(ConnectionEntry entry, Task task)
    {
        entry.Terminate();
        _connections.TryRemove(entry.Id, out _);

        if (task.IsFaulted)
        {
            _diagnostics.Report(
                BoltTransportEventKind.ConnectionFailed,
                entry.Id,
                task.Exception?.GetBaseException().GetType().Name ?? "faulted");
        }
    }

    private static bool IsListenerStopped(SocketException exception) =>
        exception.SocketErrorCode is SocketError.OperationAborted
            or SocketError.Interrupted
            or SocketError.NotSocket
            or SocketError.InvalidArgument;

    private static string Describe(Exception exception) =>
        exception is SocketException socket ? socket.SocketErrorCode.ToString() : exception.GetType().Name;

    /// <summary>
    /// One accepted connection under server ownership: the socket, the startup gate and the
    /// pipeline task. Termination is idempotent so the graceful, forced and retirement paths can
    /// all attempt it without coordination.
    /// </summary>
    private sealed class ConnectionEntry
    {
        private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TcpClient _client;
        private int _terminated;

        internal ConnectionEntry(string id, TcpClient client)
        {
            Id = id;
            _client = client;
        }

        internal string Id { get; }

        internal Task Task { get; set; } = Task.CompletedTask;

        internal bool IsTerminated => Volatile.Read(ref _terminated) != 0;

        internal Task WaitForStartAsync() => _start.Task;

        internal void Start() => _start.TrySetResult();

        /// <summary>
        /// Closes the owned socket exactly once and releases a pipeline that has not started yet.
        /// Disposing the socket aborts any pending I/O, which is what terminates a connection that
        /// does not cooperate with graceful cancellation.
        /// </summary>
        internal void Terminate()
        {
            if (Interlocked.Exchange(ref _terminated, 1) == 0)
            {
                DisposeQuietly(_client);
            }

            _start.TrySetResult();
        }
    }
}
