using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

namespace BoltSharp.Transport;

/// <summary>Owns bounded drain admission for a listener and the terminal I/O for each admitted plaintext socket.</summary>
internal sealed class BoltPlaintextTerminalClose
{
    // Conservative candidate policy for the authenticated loopback listener. No new public options.
    private static readonly TimeSpan TotalBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DrainBudget = TimeSpan.FromMilliseconds(75);
    private const int DrainByteLimit = 8 * 1024;
    private const int ScratchSize = 2 * 1024;
    private readonly SemaphoreSlim _permits;
    private readonly ConcurrentDictionary<string, TerminalOwner> _owners = new();

    internal BoltPlaintextTerminalClose(int maxConnections)
    {
        _permits = new SemaphoreSlim(Math.Min(1, maxConnections - 1), 1);
    }

    internal async Task<bool> CancelConnectionAsync(string connectionId)
    {
        if (!_owners.TryGetValue(connectionId, out var owner))
            return false;

        try
        {
            await owner.Cancellation.CancelAsync().ConfigureAwait(false);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false; // The connection completed concurrently.
        }
    }

    internal async Task<(bool Failed, string Detail)> RunAsync(
        TcpClient client,
        NetworkStream stream,
        string connectionId,
        TimeSpan writeTimeout,
        Func<CancellationToken, Action, Task> writeResponse,
        Action messageProcessed,
        CancellationToken listener)
    {
        var started = Stopwatch.GetTimestamp();
        using var owner = new TerminalOwner(client);
        _owners.TryAdd(connectionId, owner);
        var phase = "write";
        var reason = "transport-error";
        var bytes = 0;
        var admitted = false;
        string? writeInterruption = null;

        try
        {
            using var total = new CancellationTokenSource(TotalBudget);
            using var drain = new CancellationTokenSource();
            using var onTotal = total.Token.UnsafeRegister(_ => owner.Interrupt("total-deadline"), null);
            using var onListener = listener.UnsafeRegister(_ => owner.Interrupt("listener-shutdown"), null);
            using var onOwner = owner.Cancellation.Token.UnsafeRegister(_ => owner.Interrupt("owner-cancelled"), null);
            using var onDrain = drain.Token.UnsafeRegister(_ => owner.Interrupt("drain-deadline"), null);

            {
                using var write = new CancellationTokenSource();
                using var writeTimer = new CancellationTokenSource();
                using var onWriteOwner = owner.Token.UnsafeRegister(_ => InterruptWrite(owner.Interruption!), null);
                using var onWriteTimer = writeTimer.Token.UnsafeRegister(_ => InterruptWrite("write-deadline"), null);
                ArmPhaseTimer(writeTimer, writeTimeout, started, owner);
                Report("write-start");
                write.Token.ThrowIfCancellationRequested();
                await writeResponse(write.Token, () => Report("write-pending")).ConfigureAwait(false);
                phase = "post-write";

                void InterruptWrite(string cause)
                {
                    if (Interlocked.CompareExchange(ref writeInterruption, cause, null) is null)
                        write.Cancel();
                }
            }

            // A successful flush ends the write phase. The shorter timer only cancels the
            // awaited write; even a racing callback cannot interrupt post-write processing.
            Report("write-complete");
            messageProcessed();
            if (Remaining(started) <= TimeSpan.Zero)
                owner.Interrupt("total-deadline");
            owner.Token.ThrowIfCancellationRequested();

            phase = "admission";
            admitted = _permits.Wait(0);
            if (!admitted)
            {
                reason = "capacity";
                return (false, "closed");
            }

            phase = "shutdown";
            ArmPhaseTimer(drain, DrainBudget, started, owner);
            Report("drain-admitted");
            owner.Token.ThrowIfCancellationRequested();
            client.Client.Shutdown(SocketShutdown.Send);
            Report("send-shutdown");

            phase = "drain";
            Report("drain-start");
            var scratch = new byte[ScratchSize];
            while (bytes < DrainByteLimit)
            {
                owner.Token.ThrowIfCancellationRequested();
                var limit = Math.Min(scratch.Length, DrainByteLimit - bytes);
                var read = await stream.ReadAsync(scratch.AsMemory(0, limit), owner.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    reason = "peer-eof";
                    return (false, "closed");
                }

                bytes += read;
            }

            reason = "byte-limit";
            return (false, "closed");
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            reason = (phase == "write" && error is OperationCanceledException ? writeInterruption : null)
                ?? owner.Interruption ?? (PeerDisconnected(error) ? "peer-disconnect" : "transport-error");
            return reason switch
            {
                "listener-shutdown" => (false, "listener-shutdown"),
                "owner-cancelled" => (false, "owner-cancelled"),
                "peer-disconnect" => (false, "client-disconnect"),
                "total-deadline" or "write-deadline" when phase == "write" => (true, "timeout"),
                "total-deadline" or "drain-deadline" => (false, "closed"),
                _ => (true, "transport-error")
            };
        }
        finally
        {
            var ended = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            owner.CloseSocket();
            var disposed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (admitted)
                _permits.Release();
            _owners.TryRemove(connectionId, out _);

            if (phase == "write")
                BoltPlaintextTerminalCloseDiagnostics.Report(connectionId, "write-failed", phase,
                    reason, bytes, admitted, ended);
            if (phase == "drain")
                BoltPlaintextTerminalCloseDiagnostics.Report(connectionId, "drain-end", phase,
                    reason, bytes, admitted, ended);
            BoltPlaintextTerminalCloseDiagnostics.Report(connectionId, "disposed", phase,
                reason, bytes, admitted, disposed);
        }

        void Report(string name) => BoltPlaintextTerminalCloseDiagnostics.Report(
            connectionId, name, phase, null, bytes, admitted, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static void ArmPhaseTimer(CancellationTokenSource phase, TimeSpan budget, long started, TerminalOwner owner)
    {
        var remaining = Remaining(started);
        if (remaining <= TimeSpan.Zero)
            owner.Interrupt("total-deadline");
        else if (budget < remaining)
            phase.CancelAfter(budget);
    }

    private static TimeSpan Remaining(long started) => TotalBudget - Stopwatch.GetElapsedTime(started);

    private static bool PeerDisconnected(Exception error)
    {
        var socket = error as SocketException ?? error.InnerException as SocketException;
        return socket?.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted
            or SocketError.Shutdown or SocketError.NotConnected;
    }

    private sealed class TerminalOwner(TcpClient client) : IDisposable
    {
        private readonly CancellationTokenSource _interrupted = new();
        private string? _interruption;
        private int _closed;

        internal TcpClient Client { get; } = client;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal CancellationToken Token => _interrupted.Token;
        internal string? Interruption => Volatile.Read(ref _interruption);

        internal void Interrupt(string reason)
        {
            if (Interlocked.CompareExchange(ref _interruption, reason, null) is not null)
                return;

            try
            {
                _interrupted.Cancel();
            }
            finally
            {
                CloseSocket();
            }
        }

        internal void CloseSocket()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
                Client.Dispose();
        }

        public void Dispose()
        {
            Cancellation.Dispose();
            _interrupted.Dispose();
        }
    }
}
