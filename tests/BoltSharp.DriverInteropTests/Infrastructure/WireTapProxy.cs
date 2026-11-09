using System.Net;
using System.Net.Sockets;

namespace BoltSharp.DriverInteropTests.Infrastructure;

internal enum TapDirection
{
    ClientToServer,
    ServerToClient,
}

internal sealed record TappedChunk(TapDirection Direction, byte[] Bytes, DateTimeOffset AtUtc);

internal sealed class TappedConnection
{
    internal required int Id { get; init; }

    internal List<TappedChunk> Chunks { get; } = new();

    internal string ClientSideClose { get; set; } = "open";

    internal string ServerSideClose { get; set; } = "open";
}

/// <summary>
/// A passive, byte-exact loopback observation point for the official-driver scenarios. It listens
/// on an ephemeral loopback port, accepts the driver's TCP/TLS connection, opens a second
/// connection to the genuine B5-A server, and forwards every received byte immediately in both
/// directions while recording a copy (direction, timestamp, bytes, read-chunk boundary).
/// <para>
/// <b>Neutrality.</b> The tap never rewrites, delays, coalesces, splits or drops bytes; it records
/// read boundaries only. It is a test-only diagnostic probe, not a transport replacement: the
/// pass/fail assertions of every scenario are made on the official driver's public API results and
/// on the genuine server's own diagnostics. Captured read chunks approximate sender write
/// boundaries on loopback and are treated as observations, never as protocol requirements.
/// </para>
/// </summary>
internal sealed class WireTapProxy : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IPEndPoint _target;
    private readonly List<TappedConnection> _connections = new();
    private readonly List<Task> _tasks = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;

    private WireTapProxy(TcpListener listener, IPEndPoint target)
    {
        _listener = listener;
        _target = target;
        BoundEndPoint = (IPEndPoint)listener.LocalEndpoint;
    }

    internal IPEndPoint BoundEndPoint { get; }

    internal string HostPort => $"localhost:{BoundEndPoint.Port}";

    internal static Task<WireTapProxy> StartAsync(IPEndPoint target)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var proxy = new WireTapProxy(listener, target);
        proxy._tasks.Add(Task.Run(() => proxy.AcceptLoopAsync()));
        return Task.FromResult(proxy);
    }

    /// <summary>A stable snapshot of the captured connections.</summary>
    internal IReadOnlyList<TappedConnection> Connections()
    {
        lock (_lock)
        {
            return _connections.ToArray();
        }
    }

    /// <summary>The single captured connection; fails when the capture does not contain exactly one.</summary>
    internal TappedConnection SingleConnection() => Connections().Single();

    /// <summary>Every captured connection paired with its assembled transcript.</summary>
    internal IReadOnlyList<(TappedConnection Connection, WireTranscript Transcript)> Analyze() =>
        Connections().Select(c => (c, WireTranscript.Build(c))).ToArray();

    /// <summary>The first captured connection whose transcript satisfies the predicate.</summary>
    internal (TappedConnection Connection, WireTranscript Transcript) FindConnection(Func<WireTranscript, bool> predicate) =>
        Analyze().First(pair => predicate(pair.Transcript));

    internal async Task<bool> WaitForClosedConnectionsAsync(int expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var connections = Connections();
            if (connections.Count >= expected
                && connections.Take(expected).All(c => c.ClientSideClose != "open" && c.ServerSideClose != "open"))
            {
                return true;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        return false;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            var connection = new TappedConnection { Id = Interlocked.Increment(ref _nextId) };
            lock (_lock)
            {
                _connections.Add(connection);
            }

            TcpClient upstream;
            try
            {
                upstream = new TcpClient();
                await upstream.ConnectAsync(_target.Address, _target.Port, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                connection.ClientSideClose = "upstream-connect-failed";
                client.Dispose();
                continue;
            }

            _tasks.Add(Task.Run(() => PumpAsync(
                client.GetStream(), upstream.GetStream(), connection, TapDirection.ClientToServer, clientSide: true)));
            _tasks.Add(Task.Run(() => PumpAsync(
                upstream.GetStream(), client.GetStream(), connection, TapDirection.ServerToClient, clientSide: false)));
        }
    }

    private async Task PumpAsync(
        NetworkStream from,
        NetworkStream to,
        TappedConnection connection,
        TapDirection direction,
        bool clientSide)
    {
        var buffer = new byte[64 * 1024];
        var closeKind = "eof";
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await from.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    closeKind = "reset";
                    break;
                }
                catch (SocketException)
                {
                    closeKind = "reset";
                    break;
                }
                catch (OperationCanceledException)
                {
                    closeKind = "tap-disposed";
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                var copy = new byte[read];
                Array.Copy(buffer, copy, read);
                lock (_lock)
                {
                    connection.Chunks.Add(new TappedChunk(direction, copy, DateTimeOffset.UtcNow));
                }

                try
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), _cts.Token).ConfigureAwait(false);
                    await to.FlushAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    closeKind = "forward-failed";
                    break;
                }
            }
        }
        catch (Exception)
        {
            closeKind = "pump-fault";
        }

        if (clientSide)
        {
            connection.ClientSideClose = closeKind;
        }
        else
        {
            connection.ServerSideClose = closeKind;
        }

        try
        {
            to.Close();
        }
        catch (Exception)
        {
            // The peer socket is already gone; nothing to release.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        foreach (var task in _tasks.ToArray())
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Bounded probe teardown; a stuck pump is abandoned with the process-local socket.
            }
        }

        _cts.Dispose();
    }
}
