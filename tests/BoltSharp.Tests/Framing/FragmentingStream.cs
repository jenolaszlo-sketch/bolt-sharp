namespace BoltSharp.Tests.Framing;

/// <summary>
/// A read-only <see cref="Stream"/> over a byte array that deliberately returns fewer bytes
/// than requested, so the framing codec can be exercised against arbitrary fragmentation of
/// the input. A fixed maximum read size (or a pseudo-random one) simulates a network or
/// WebSocket transport that does not preserve message boundaries.
/// </summary>
internal sealed class FragmentingStream : Stream
{
    private readonly byte[] _data;
    private readonly Func<int, int> _nextReadSize;
    private int _position;

    internal FragmentingStream(byte[] data, int maxReadSize)
        : this(data, _ => maxReadSize)
    {
    }

    internal FragmentingStream(byte[] data, Random random, int maxReadSize)
        : this(data, _ => random.Next(1, maxReadSize + 1))
    {
    }

    private FragmentingStream(byte[] data, Func<int, int> nextReadSize)
    {
        _data = data;
        _nextReadSize = nextReadSize;
    }

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
        var remaining = _data.Length - _position;
        if (remaining <= 0 || buffer.Length == 0)
        {
            return 0;
        }

        var toCopy = Math.Min(Math.Min(buffer.Length, _nextReadSize(remaining)), remaining);
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
