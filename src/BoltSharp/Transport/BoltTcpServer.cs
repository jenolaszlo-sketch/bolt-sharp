using System.Collections.Concurrent;
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
/// session, framing state and cancellation context. Failures in one connection never affect the
/// listener or other connections.
/// </para>
/// <para>
/// <b>Secure defaults.</b> A plaintext listener must bind to a loopback address (validated at
/// construction); TLS requires an explicitly configured certificate with a private key and never
/// downgrades to plaintext.
/// </para>
/// </summary>
public sealed class BoltTcpServer : IAsyncDisposable
{
    private readonly BoltTransportDiagnostics _diagnostics = new();
    private readonly ConcurrentDictionary<int, Task> _connectionTasks = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    private TcpListener? _listener;
    private Task? _acceptLoopTask;
    private int _nextConnectionId;
    private int _stopping;

    private BoltTcpServer(BoltTcpServerOptions options)
    {
        Options = options;
        Options.Validate();
        Diagnostics = _diagnostics;
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
    /// Binds the configured endpoint and starts the accept loop. The actual bound endpoint (an
    /// ephemeral port when Port is 0) is reported via BoundEndPoint.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _stopping, 0, 0) != 0 || _listener is not null)
        {
            throw new InvalidOperationException(
                "The server has already been started; a BoltTcpServer instance is single-use.");
        }

        _listener = new TcpListener(Options.BindAddress, Options.Port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, false);
        _listener.Start(Options.Backlog);
        BoundEndPoint = (IPEndPoint)_listener.LocalEndpoint;

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_shutdownCts.Token));
        _diagnostics.Report(
            BoltTransportEventKind.ListenerStarted,
            "listener",
            $"{BoundEndPoint} tls={(Options.Tls is not null).ToString()}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops accepting connections, signals active connections and awaits their completion within
    /// the configured bounded shutdown policy. Idempotent.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        _shutdownCts.Cancel();
        _listener?.Stop();

        var tasks = _connectionTasks.Values.ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(Options.ShutdownTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Bounded shutdown: remaining connection tasks were signalled but did not
                // complete in time; their sockets were aborted by their own cancellation.
            }
        }

        foreach (var task in tasks)
        {
            _connectionTasks.TryRemove(task.Id, out _);
        }

        _diagnostics.Report(BoltTransportEventKind.ListenerStopped, "listener", $"activeRemaining={_connectionTasks.Count}");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener!;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // Listener stopped: a clean accept-loop exit (the shutdown path reports it).
                return;
            }
            catch (Exception ex)
            {
                // Unexpected listener fault must be observable; the loop then retries so a
                // transient fault cannot silently kill the server.
                _diagnostics.Report(
                    BoltTransportEventKind.ConnectionFailed, "listener", ex.GetType().Name);
                continue;
            }

            var connectionId = $"conn-{Interlocked.Increment(ref _nextConnectionId):0000}";
            if (_connectionTasks.Count >= Options.MaxConnections)
            {
                // Bounded admission: refuse safely by closing the accepted socket immediately.
                _diagnostics.Report(
                    BoltTransportEventKind.ConnectionRejectedCapacity, connectionId, "capacity reached");
                client.Dispose();
                continue;
            }

            _diagnostics.Report(BoltTransportEventKind.ConnectionAccepted, connectionId, $"{client.Client.RemoteEndPoint}");
            client.NoDelay = true;

            var connectionTask = Task.Run(
                () => BoltTcpConnection.RunAsync(client, Options, _diagnostics, connectionId, _shutdownCts.Token),
                CancellationToken.None);
            _connectionTasks[connectionTask.Id] = connectionTask;
            _ = connectionTask.ContinueWith(
                t => _connectionTasks.TryRemove(t.Id, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
