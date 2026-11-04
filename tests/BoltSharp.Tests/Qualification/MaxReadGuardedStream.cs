namespace BoltSharp.Tests.Qualification;

/// <summary>
/// A read-only <see cref="Stream"/> that records the largest single read ever requested and
/// throws if any read asks for more than a fixed guard. Used to prove that the framing codec
/// rejects an over-limit declared chunk <em>before</em> attempting to read (or allocate) its
/// payload.
/// </summary>
internal sealed class MaxReadGuardedStream : Stream
{
    private readonly byte[] _data;
    private readonly int _maxAllowedRead;
    private int _position;

    internal MaxReadGuardedStream(byte[] data, int maxAllowedRead)
    {
        _data = data;
        _maxAllowedRead = maxAllowedRead;
    }

    internal int LargestReadRequested { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _data.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        LargestReadRequested = Math.Max(LargestReadRequested, buffer.Length);
        if (buffer.Length > _maxAllowedRead)
        {
            throw new InvalidOperationException(
                $"A single read of {buffer.Length} byte(s) was requested, exceeding the guard of {_maxAllowedRead}.");
        }

        var remaining = _data.Length - _position;
        if (remaining <= 0)
        {
            return 0;
        }

        var toCopy = Math.Min(buffer.Length, remaining);
        _data.AsSpan(_position, toCopy).CopyTo(buffer);
        _position += toCopy;
        return toCopy;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
