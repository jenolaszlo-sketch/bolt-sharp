namespace BoltSharp.Tests.Harness;

/// <summary>
/// Deterministic, reusable in-memory ordered-byte-stream harness for B2-C protocol tests.
/// <para>
/// A <see cref="HarnessDuplex"/> wires two <see cref="HarnessStream"/> endpoints together:
/// bytes written to one endpoint are readable from the other, in order. Each endpoint has an
/// independent read-fragmentation policy (fixed or seeded pseudo-random), an optional write
/// fragmentation size, a bounded buffer (writes beyond the cap fail instead of accumulating
/// without bound), explicit write completion (EOF), deterministic cancellation, and injected
/// read/write/flush faults for negative tests. There are no timers or sleeps: asynchronous reads
/// wait on a signal that is completed by a write or by <see cref="HarnessStream.CompleteWrites"/>.
/// </para>
/// <para>
/// The harness is a plain <see cref="Stream"/>; it does not reimplement or duplicate the framing or
/// handshake logic. Production <c>BoltMessageReader</c>/<c>BoltMessageWriter</c> and
/// <c>BoltHandshakeNegotiator</c> are exercised through it unchanged.
/// </para>
/// </summary>
internal sealed class HarnessStreamOptions
{
    /// <summary>Largest single read returned to the reader (fixed fragmentation).</summary>
    internal int MaxReadFragment { get; init; } = int.MaxValue;

    /// <summary>Smallest single read when <see cref="ReadRandom"/> is set.</summary>
    internal int MinReadFragment { get; init; } = 1;

    /// <summary>When set, each read returns a seeded pseudo-random length in the fragment range.</summary>
    internal Random? ReadRandom { get; init; }

    /// <summary>Largest single append per <see cref="Stream.Write"/> call (partial-write shaping).</summary>
    internal int MaxWriteFragment { get; init; } = int.MaxValue;

    /// <summary>Maximum bytes that may be buffered for the peer before a write fails.</summary>
    internal int MaxBufferedBytes { get; init; } = 1 << 20;

    /// <summary>Throw a deterministic <see cref="IOException"/> once this many bytes were read.</summary>
    internal int? FailReadAfterBytes { get; init; }

    /// <summary>Write this many bytes of a write call, then throw a deterministic fault.</summary>
    internal int? FailWriteAfterBytes { get; init; }

    /// <summary>Throw a deterministic fault from <see cref="Stream.Flush"/>.</summary>
    internal bool FailFlush { get; init; }

    /// <summary>Cancel <see cref="Cancellation"/> once this many bytes were read (async path).</summary>
    internal int? CancelReadAtByte { get; init; }

    /// <summary>Cancellation source paired with <see cref="CancelReadAtByte"/>.</summary>
    internal CancellationTokenSource? Cancellation { get; init; }
}

/// <summary>A bounded, signal-backed byte queue connecting a writer to a reader.</summary>
internal sealed class HarnessChannel
{
    private readonly object _gate = new();
    private readonly List<byte> _buffer = new();
    private readonly int _maxBufferedBytes;
    private int _readIndex;
    private bool _completed;
    private bool _disposed;
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal HarnessChannel(int maxBufferedBytes) => _maxBufferedBytes = maxBufferedBytes;

    internal bool IsCompleted
    {
        get { lock (_gate) { return _completed; } }
    }

    internal bool IsDisposed
    {
        get { lock (_gate) { return _disposed; } }
    }

    /// <summary>
    /// Mark the channel disposed and wake any blocked reader (B2-C-R1, finding Q1-F02). A pending
    /// read then observes <see cref="ObjectDisposedException"/> rather than a clean EOF, and
    /// subsequent reads/writes fail consistently.
    /// </summary>
    internal void Dispose()
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            signal = _signal;
            _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    internal int Available
    {
        get { lock (_gate) { return _buffer.Count - _readIndex; } }
    }

    internal void Write(ReadOnlySpan<byte> data)
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HarnessStream));
            }

            if (_buffer.Count - _readIndex + data.Length > _maxBufferedBytes)
            {
                throw new InvalidOperationException(
                    $"Harness buffer cap of {_maxBufferedBytes} byte(s) would be exceeded; " +
                    "buffering is bounded by design.");
            }

            foreach (var b in data)
            {
                _buffer.Add(b);
            }

            signal = _signal;
            _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    internal void Complete()
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            _completed = true;
            signal = _signal;
            _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    internal int Read(Span<byte> destination, int fragment)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HarnessStream));
            }

            return ReadCore(destination, fragment);
        }
    }

    internal async ValueTask<int> ReadAsync(Memory<byte> destination, int fragment, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(HarnessStream));
                }

                if (_buffer.Count - _readIndex > 0 || _completed)
                {
                    return ReadCore(destination.Span, fragment);
                }

                wait = _signal.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private int ReadCore(Span<byte> destination, int fragment)
    {
        var available = _buffer.Count - _readIndex;
        if (available == 0 || destination.Length == 0)
        {
            return 0;
        }

        var toCopy = Math.Min(Math.Min(fragment, destination.Length), available);
        for (var i = 0; i < toCopy; i++)
        {
            destination[i] = _buffer[_readIndex + i];
        }

        _readIndex += toCopy;

        // Compact occasionally so the list does not grow without bound over long streams.
        if (_readIndex >= 4096 && _readIndex * 2 >= _buffer.Count)
        {
            _buffer.RemoveRange(0, _readIndex);
            _readIndex = 0;
        }

        return toCopy;
    }
}

/// <summary>
/// One duplex endpoint of the in-memory harness. Disposing the endpoint promptly releases a
/// pending asynchronous read with <see cref="ObjectDisposedException"/> and makes further
/// reads/writes fail consistently; repeated disposal is safe (B2-C-R1, finding Q1-F02).
/// </summary>
internal sealed class HarnessStream : Stream
{
    private readonly HarnessChannel _read;
    private readonly HarnessChannel _write;
    private readonly HarnessStreamOptions _options;
    private int _readBytes;
    private int _writtenBytes;

    internal HarnessStream(HarnessChannel read, HarnessChannel write, HarnessStreamOptions options)
    {
        _read = read;
        _write = write;
        _options = options;
    }

    internal int ReadCalls { get; private set; }

    internal int WriteCalls { get; private set; }

    internal int Consumed => _readBytes;

    internal int Written => _writtenBytes;

    internal bool Disposed { get; private set; }

    internal bool ReadCompleted => _read.IsCompleted;

    /// <summary>Bytes this endpoint has submitted toward the peer (not yet read by the peer).</summary>
    internal int BufferedForPeer => _write.Available;

    /// <summary>Bytes the peer has submitted that are available to read on this endpoint.</summary>
    internal int AvailableToRead => _read.Available;

    /// <summary>Signal end-of-stream to the peer (half-close of the write direction).</summary>
    internal void CompleteWrites() => _write.Complete();

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => 0;

    public override long Position
    {
        get => _readBytes;
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        ReadCalls++;
        if (_options.FailReadAfterBytes is int failAt && _readBytes >= failAt)
        {
            throw new IOException($"harness-injected read failure after {failAt} byte(s)");
        }

        var read = _read.Read(buffer, NextFragment());
        _readBytes += read;
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ReadCalls++;
        if (_options.CancelReadAtByte is int cancelAt && _readBytes >= cancelAt)
        {
            _options.Cancellation!.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_options.FailReadAfterBytes is int failAt && _readBytes >= failAt)
        {
            throw new IOException($"harness-injected read failure after {failAt} byte(s)");
        }

        var read = await _read.ReadAsync(buffer, NextFragment(), cancellationToken).ConfigureAwait(false);
        _readBytes += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ThrowIfDisposed();
        WriteCalls++;
        var offset = 0;
        var fragment = Math.Max(1, _options.MaxWriteFragment);

        while (offset < buffer.Length)
        {
            var take = Math.Min(fragment, buffer.Length - offset);

            if (_options.FailWriteAfterBytes is int failAt && _writtenBytes >= failAt)
            {
                throw new IOException($"harness-injected write failure after {failAt} byte(s)");
            }

            if (_options.FailWriteAfterBytes is int cap && _writtenBytes + take > cap)
            {
                take = cap - _writtenBytes;
                if (take <= 0)
                {
                    throw new IOException($"harness-injected write failure after {cap} byte(s)");
                }

                _write.Write(buffer.Slice(offset, take));
                _writtenBytes += take;
                throw new IOException($"harness-injected partial write failure after {cap} byte(s)");
            }

            _write.Write(buffer.Slice(offset, take));
            _writtenBytes += take;
            offset += take;
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
        if (_options.FailFlush)
        {
            throw new IOException("harness-injected flush failure");
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flush();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !Disposed)
        {
            Disposed = true;

            // Waking the read channel releases any pending read with ObjectDisposedException
            // (B2-C-R1, finding Q1-F02). Repeated disposal is a no-op.
            _read.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ThrowIfDisposed()
    {
        if (Disposed)
        {
            throw new ObjectDisposedException(nameof(HarnessStream));
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private int NextFragment()
    {
        if (_options.ReadRandom is Random random)
        {
            var min = Math.Max(1, _options.MinReadFragment);
            var max = Math.Max(min, _options.MaxReadFragment == int.MaxValue ? min : _options.MaxReadFragment);
            return random.Next(min, max + 1);
        }

        return Math.Max(1, _options.MaxReadFragment);
    }
}

/// <summary>Factory for connected harness endpoint pairs.</summary>
internal static class HarnessDuplex
{
    /// <summary>
    /// Create a client/server endpoint pair. Bytes written on the client are read on the server and
    /// vice versa. Each direction has an independent bounded buffer.
    /// </summary>
    internal static (HarnessStream Client, HarnessStream Server) Create(
        HarnessStreamOptions? clientOptions = null,
        HarnessStreamOptions? serverOptions = null)
    {
        var client = clientOptions ?? new HarnessStreamOptions();
        var server = serverOptions ?? new HarnessStreamOptions();
        var buffer = Math.Min(client.MaxBufferedBytes, server.MaxBufferedBytes);

        var clientToServer = new HarnessChannel(buffer);
        var serverToClient = new HarnessChannel(buffer);

        return (
            new HarnessStream(serverToClient, clientToServer, client),
            new HarnessStream(clientToServer, serverToClient, server));
    }
}
