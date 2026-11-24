using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace R1Harness;

/// <summary>
/// E1-faithful candidate TLS terminal-close drain (harness-owned, explicitly candidate-only;
/// pinned production has no TLS drain). Measures ACTUAL ciphertext bytes at the transport
/// boundary beneath SslStream: the counting wrapper sits between SslStream and the socket's
/// NetworkStream, so every byte SslStream pushes down (encrypted outbound records) or pulls up
/// (encrypted inbound records) is counted exactly as it crosses the wire boundary.
/// Direction and accounting boundaries mirror Q-B5B-09-TLS-E1 (T5/T6 + NC-1 evidence):
/// the encrypted cap binds INBOUND encrypted bytes processed during the drain phase
/// (post-handshake application-data phase only); the plaintext cap is enforced exactly by
/// clipping each read to the remaining plaintext budget. The encrypted cap is enforced
/// strictly at the same transport boundary: inbound reads are clipped to the remaining
/// encrypted budget, so the drain can never consume more than the declared cap. This is the
/// original E1 contract (T5 WireBytes == 32768; NC-1 WireBytes == 131072), not a one-record
/// overshoot allowance.
/// </summary>
internal static class CandidateDrain
{
    /// <summary>The exact measurement boundary string recorded in every attempt journal.</summary>
    internal const string MeasurementBoundary = "beneath-sslstream-transport-boundary";

    internal const string CiphertextSource = "wire-counting-inner-stream (bytes SslStream pulls from the socket = actual TLS record ciphertext)";

    internal sealed record WireCounters(long EncryptedInbound, long EncryptedOutbound);

    /// <summary>Raised by the wire boundary when the strict inbound ciphertext budget is spent.</summary>
    internal sealed class CiphertextCapReachedException : IOException
    {
        internal CiphertextCapReachedException(long cap)
            : base($"encrypted inbound byte cap {cap} reached at the transport boundary")
        {
        }
    }

    /// <summary>
    /// A Stream wrapper that counts bytes at the TLS transport boundary. Bytes written through
    /// this stream are the encrypted records SslStream emits; bytes read from it are the
    /// encrypted records SslStream consumes. Both are exact wire-phase ciphertext counts.
    /// An optional inbound limit is armed after the TLS handshake; from then on every read is
    /// clipped to the remaining budget and a read beyond the budget fails closed.
    /// </summary>
    internal sealed class WireCountingStream : Stream
    {
        private readonly Stream _inner;
        private long _encryptedInbound;
        private long _encryptedOutbound;
        private long _inboundLimit = -1;
        private long _inboundSinceLimit;

        internal WireCountingStream(Stream inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        internal WireCounters Snapshot => new(Interlocked.Read(ref _encryptedInbound), Interlocked.Read(ref _encryptedOutbound));

        /// <summary>Arm the strict post-handshake inbound ciphertext budget.</summary>
        internal void ArmInboundLimit(long limit)
        {
            Interlocked.Exchange(ref _inboundSinceLimit, 0);
            Interlocked.Exchange(ref _inboundLimit, limit);
        }

        internal long InboundSinceLimit => Interlocked.Read(ref _inboundSinceLimit);

        internal long InboundLimit => Interlocked.Read(ref _inboundLimit);

        public override bool CanRead => _inner.CanRead;
        public override bool CanWrite => _inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        /// <summary>Clip a read request to the remaining strict inbound budget (fail closed at the cap).</summary>
        private Memory<byte> ClipInbound(Memory<byte> buffer)
        {
            var limit = Interlocked.Read(ref _inboundLimit);
            if (limit < 0) return buffer;
            var remaining = limit - Interlocked.Read(ref _inboundSinceLimit);
            if (remaining <= 0)
                throw new CandidateDrain.CiphertextCapReachedException(limit);
            return buffer.Length <= remaining ? buffer : buffer[..(int)remaining];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var clipped = ClipInbound(buffer.AsMemory(offset, count));
            var read = _inner.Read(clipped.Span);
            if (read > 0)
            {
                Interlocked.Add(ref _encryptedInbound, read);
                if (Interlocked.Read(ref _inboundLimit) >= 0)
                    Interlocked.Add(ref _inboundSinceLimit, read);
            }
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var clipped = ClipInbound(buffer);
            var read = await _inner.ReadAsync(clipped, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                Interlocked.Add(ref _encryptedInbound, read);
                if (Interlocked.Read(ref _inboundLimit) >= 0)
                    Interlocked.Add(ref _inboundSinceLimit, read);
            }
            return read;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            Interlocked.Add(ref _encryptedOutbound, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _encryptedOutbound, buffer.Length);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _encryptedOutbound, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed record DrainResult(
        string Outcome,
        long EncryptedWireBytes,
        long PlaintextBytes,
        long EncryptedOutboundWireBytes,
        bool CloseNotifyCompleted,
        Dictionary<string, object?> Accounting,
        double ElapsedMs,
        string? Error,
        long InboundWireLimit);

    /// <summary>
    /// Runs the candidate drain against an already-accepted TLS client stream pair. The drain
    /// terminates at whichever registered cap binds first (matching E1 T5/T6 semantics):
    /// "cipher-cap" when the strict inbound encrypted wire budget is spent (exactly the cap),
    /// "plain-cap" when the decrypted plaintext counter reaches the plaintext cap,
    /// "peer-eof" when the peer disconnects before either cap.
    /// The encrypted cap is enforced strictly at the transport boundary: the wire wrapper
    /// clips every post-handshake inbound read to the remaining budget, so the consumed
    /// ciphertext can never exceed the declared cap (original E1 contract).
    /// </summary>
    internal static async Task<DrainResult> RunAsync(
        TcpClient client,
        WireCountingStream wire,
        X509Certificate2 certificate,
        long ciphertextCap,
        long plaintextCap,
        bool closeNotify,
        CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var plaintextDrained = 0L;
        var closeNotifyCompleted = false;
        SslStream? tls = null;
        var wireBefore = new CandidateDrain.WireCounters(0, 0);
        try
        {
            tls = new SslStream(wire, leaveInnerStreamOpen: true);
            await tls.AuthenticateAsServerAsync(certificate).WaitAsync(TimeSpan.FromSeconds(5), token)
                .ConfigureAwait(false);

            // Accounting boundary: the caps bind the post-handshake application-data phase only.
            // Arming the strict inbound budget here also guarantees the drain can never consume
            // more than the declared ciphertext cap, no matter what SslStream buffers.
            wireBefore = wire.Snapshot;
            wire.ArmInboundLimit(ciphertextCap);
            var scratch = new byte[2048]; // E1 scratch size
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (plaintextDrained >= plaintextCap)
                {
                    return Finish("plain-cap", null);
                }

                if (wire.InboundSinceLimit >= ciphertextCap)
                {
                    return Finish("cipher-cap", null);
                }

                var limit = (int)Math.Min((long)scratch.Length, plaintextCap - plaintextDrained);
                var read = await tls.ReadAsync(scratch.AsMemory(0, limit), token).ConfigureAwait(false);
                if (read == 0)
                {
                    return Finish("peer-eof", null);
                }

                plaintextDrained += read;
            }
        }
        catch (Exception ex)
        {
            // The strict budget can be spent inside a single SslStream record pull; that is a
            // cap bound, not a fault. A transport fault is reported with the counters measured
            // so far; the caller decides the oracle. The decrypted count is exact.
            var spentBudget = wire.InboundSinceLimit >= ciphertextCap;
            var outcome = spentBudget ? "cipher-cap"
                : plaintextDrained >= plaintextCap ? "plain-cap"
                : ex is OperationCanceledException ? "watchdog" : "transport-error";
            return Finish(outcome, spentBudget ? null : ex.GetType().Name + ":" + ex.Message);
        }
        finally
        {
            try { tls?.Dispose(); } catch (Exception) { }
        }

        CandidateDrain.DrainResult Finish(string outcome, string? error)
        {
            var after = wire.Snapshot;
            if (closeNotify)
            {
                try
                {
                    tls!.ShutdownAsync().Wait(TimeSpan.FromMilliseconds(200));
                    closeNotifyCompleted = true;
                }
                catch (Exception)
                {
                    // Bounded candidate close; a failed close is recorded, not fatal to the cap oracle.
                }
            }

            var final = wire.Snapshot;
            return new CandidateDrain.DrainResult(outcome,
                wire.InboundSinceLimit,
                plaintextDrained,
                final.EncryptedOutbound - wireBefore.EncryptedOutbound,
                closeNotifyCompleted,
                new Dictionary<string, object?>(),
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3),
                error,
                wire.InboundLimit);
        }
    }
}

/// <summary>
/// Harness-owned candidate TLS drain listener. Accepts exactly one TLS connection, runs the
/// E1-faithful capped drain, and reports the measured wire counters. Candidate-only evidence.
/// </summary>
internal sealed class CandidateDrainListener : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    private readonly long _ciphertextCap;
    private readonly long _plaintextCap;
    private readonly bool _closeNotify;
    private readonly CancellationToken _token;
    private TaskCompletionSource<CandidateDrain.DrainResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CandidateDrainListener(X509Certificate2 certificate,
        long ciphertextCap, long plaintextCap, bool closeNotify, CancellationToken token)
    {
        _certificate = certificate;
        _ciphertextCap = ciphertextCap;
        _plaintextCap = plaintextCap;
        _closeNotify = closeNotify;
        _token = token;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Server.LingerState = new LingerOption(false, 0);
    }

    internal IPEndPoint Start()
    {
        _listener.Start(1);
        _ = Task.Run(() => AcceptLoopAsync(_token));
        return (IPEndPoint)_listener.LocalEndpoint!;
    }

    internal Task<CandidateDrain.DrainResult> DrainTask => _completion.Task;

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        TcpClient client;
        try
        {
            client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _completion.TrySetResult(new CandidateDrain.DrainResult("listener-closed", 0, 0, 0, false,
                new Dictionary<string, object?>(), 0, "listener closed before accept", 0));
            return;
        }

        try
        {
            var wire = new CandidateDrain.WireCountingStream(client.GetStream());
            var result = await CandidateDrain.RunAsync(client, wire, _certificate,
                _ciphertextCap, _plaintextCap, _closeNotify, token).ConfigureAwait(false);
            var final = result with { Accounting = new Dictionary<string, object?>
            {
                ["wire_encrypted_inbound_total"] = wire.Snapshot.EncryptedInbound,
                ["wire_encrypted_outbound_total"] = wire.Snapshot.EncryptedOutbound,
                ["wire_inbound_since_limit"] = wire.InboundSinceLimit,
                ["wire_inbound_limit"] = wire.InboundLimit
            } };
            _completion.TrySetResult(final);
        }
        catch (Exception ex)
        {
            _completion.TrySetResult(new CandidateDrain.DrainResult("transport-error", 0, 0, 0, false,
                new Dictionary<string, object?>(), 0, ex.GetType().Name + ":" + ex.Message, 0));
        }
        finally
        {
            try { client.Dispose(); } catch (Exception) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _listener.Stop(); } catch (Exception) { }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
