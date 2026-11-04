namespace BoltSharp.Tests.Handshake;

/// <summary>
/// A duplex in-memory stream for handshake tests: a fixed input buffer that can be fragmented
/// into arbitrary partial reads, plus a separate captured output buffer. Read and write
/// positions are independent so the server can write its response between reading the client
/// preamble and reading the client's manifest selection, as it does on a real socket.
/// </summary>
internal sealed class HandshakeDuplexStream : Stream
{
    private readonly byte[] _input;
    private readonly int _maxReadSize;
    private readonly List<byte> _output = new();
    private int _readPosition;

    internal HandshakeDuplexStream(byte[] input, int maxReadSize = int.MaxValue)
    {
        _input = input;
        _maxReadSize = maxReadSize;
    }

    internal byte[] WrittenBytes => _output.ToArray();

    /// <summary>Bytes still available to be read.</summary>
    internal int RemainingInput => _input.Length - _readPosition;

    /// <summary>Whether this stream has been disposed.</summary>
    internal bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _input.Length;

    public override long Position
    {
        get => _readPosition;
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _input.Length - _readPosition;
        if (remaining <= 0 || buffer.Length == 0)
        {
            return 0;
        }

        var toCopy = Math.Min(Math.Min(buffer.Length, _maxReadSize), remaining);
        _input.AsSpan(_readPosition, toCopy).CopyTo(buffer);
        _readPosition += toCopy;
        return toCopy;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        foreach (var b in buffer)
        {
            _output.Add(b);
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
