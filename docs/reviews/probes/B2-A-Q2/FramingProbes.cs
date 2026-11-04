using System.Buffers;
using BoltSharp.Framing;
using Xunit;

namespace B2AQ2;

// Reviewer-owned wire fixtures and transport; no producer/Q1 helpers or encoders.
public sealed class FramingProbes
{
    private static byte[] Hex(string text) => Convert.FromHexString(text.Replace(" ", ""));
    private static ValueTask<bool> Next(BoltMessageReader reader, IBufferWriter<byte> output, bool async,
        CancellationToken token = default) => async
        ? reader.TryReadMessageAsync(output, token)
        : ValueTask.FromResult(reader.TryReadMessage(output));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedApis_ResetMessageStateAndLimit_KeepDestination(bool async)
    {
        // Payload 00 00 must not end the first message. Extra zeros are boundary NOOPs.
        using var stream = new ProbeStream(Hex("0000 0002 0000 0000 0000 0001 FF 0000 0002 A1B2 0000 0000"));
        var reader = new BoltMessageReader(stream, new BoltFramingLimits(2));
        var output = new ObservedWriter(Hex("DEADBEEF")); // pre-existing content exceeds the per-message cap
        Assert.True(await Next(reader, output, async));
        Assert.Equal(Hex("DEADBEEF0000"), output.Bytes);
        Assert.Equal(8, reader.Position);
        var middle = async ? reader.ReadMessage() : await reader.ReadMessageAsync();
        Assert.Equal(Hex("FF"), middle);
        Assert.Equal(15, reader.Position);
        Assert.True(await Next(reader, output, !async));
        Assert.Equal(Hex("DEADBEEF0000A1B2"), output.Bytes);
        Assert.Equal(21, reader.Position);
        var requests = output.Hints.Count;
        Assert.False(await Next(reader, output, async));
        Assert.Null(reader.ReadMessage());
        Assert.Null(await reader.ReadMessageAsync());
        Assert.Equal(23, reader.Position);
        Assert.Equal(requests, output.Hints.Count);
        Assert.Equal(Hex("DEADBEEF0000A1B2"), output.Bytes);
        Assert.True(stream.SyncCalls > 0 && stream.AsyncCalls > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EverySecondMessagePrefix_PreservesEarlierAndPartialOutput(bool async)
    {
        var first = Hex("0001 71 0000");
        var second = Hex("0000 0002 A0B0 0001 C0 0000");
        for (var cut = 0; cut <= second.Length; cut++)
        {
            using var stream = new ProbeStream(first.Concat(second.Take(cut)).ToArray());
            var reader = new BoltMessageReader(stream, new BoltFramingLimits(3));
            var output = new ObservedWriter(Hex("DEAD"));
            Assert.True(await Next(reader, output, !async));
            if (cut is 0 or 2) Assert.False(await Next(reader, output, async));
            else if (cut == second.Length) Assert.True(await Next(reader, output, async));
            else await Assert.ThrowsAsync<BoltFramingTruncatedException>(async () => await Next(reader, output, async));
            var expected = new List<byte>(Hex("DEAD71"));
            if (cut > 4) expected.Add(0xA0);
            if (cut > 5) expected.Add(0xB0);
            if (cut > 8) expected.Add(0xC0);
            Assert.Equal(expected.ToArray(), output.Bytes);
            Assert.Equal(first.Length + cut, reader.Position);
            Assert.Equal(reader.Position, stream.Position);
            // Do not reuse a failed reader: discard it at each iteration.
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedHeaders_NeverRequestPayloadOrDestinationStorage(bool async)
    {
        foreach (var cumulative in new[] { false, true })
        {
            var wire = Hex(cumulative ? "0002 AABB FFFF 11" : "FFFF 11");
            var boundary = cumulative ? 6 : 2;
            using var stream = new ProbeStream(wire, boundary, new IOException("Payload read forbidden"));
            var reader = new BoltMessageReader(stream, new BoltFramingLimits(3));
            var output = new ObservedWriter(Hex("FEED"));
            await Assert.ThrowsAsync<BoltFramingFormatException>(async () => await Next(reader, output, async));
            Assert.Equal(boundary, reader.Position);
            Assert.Equal(boundary, stream.Position);
            Assert.Equal(cumulative ? Hex("FEEDAABB") : Hex("FEED"), output.Bytes);
            Assert.Equal(cumulative ? new[] { 2 } : Array.Empty<int>(), output.Hints.ToArray());
        }
        // The convenience API may allocate its small initial buffer, but must not read payload.
        using var allocatingStream = new ProbeStream(Hex("FFFF11"), 2, new IOException("Payload read forbidden"));
        var allocatingReader = new BoltMessageReader(allocatingStream, new BoltFramingLimits(3));
        await Assert.ThrowsAsync<BoltFramingFormatException>(async () =>
        {
            if (async) await allocatingReader.ReadMessageAsync();
            else allocatingReader.ReadMessage();
        });
        Assert.Equal(2, allocatingReader.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportExceptions_PropagateWithoutRollbackOrPositionFabrication(bool async)
    {
        foreach (var at in new[] { 0, 1, 2, 4, 5, 7, 8 })
        {
            var failure = new IOException($"reviewer transport failure at {at}");
            using var stream = new ProbeStream(Hex("0000 0003 AABBCC 0000"), at, failure);
            var reader = new BoltMessageReader(stream);
            var output = new ObservedWriter(Hex("DDEE"));
            var thrown = await Assert.ThrowsAsync<IOException>(async () => await Next(reader, output, async));
            Assert.Same(failure, thrown);
            Assert.Equal(at, reader.Position);
            Assert.Equal(Hex("DDEE").Concat(Hex("AABBCC").Take(Math.Clamp(at - 4, 0, 3))).ToArray(), output.Bytes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(8)]
    public async Task Cancellation_ReachesSuspendingTransport_PreservesCommittedPrefix(int at)
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ProbeStream(Hex("0000 0003 AABBCC 0000"), at, cancel: cancellation);
        var reader = new BoltMessageReader(stream);
        var output = new ObservedWriter(Hex("DDEE"));
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await reader.TryReadMessageAsync(output, cancellation.Token));
        Assert.Equal(cancellation.Token, thrown.CancellationToken);
        Assert.Equal(cancellation.Token, stream.LastToken);
        Assert.Equal(at, reader.Position);
        Assert.Equal(Hex("DDEE").Concat(Hex("AABBCC").Take(Math.Clamp(at - 4, 0, 3))).ToArray(), output.Bytes);
        Assert.Equal(0, stream.SyncCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TinyChunks_UseByteLimit_NotIndependentChunkCount(bool async)
    {
        var wire = Enumerable.Range(0, 257).SelectMany(i => new byte[] { 0, 1, (byte)i })
            .Concat(Hex("0000")).ToArray();
        foreach (var limit in new[] { 256, 257 })
        {
            using var stream = new ProbeStream(wire);
            var reader = new BoltMessageReader(stream, new BoltFramingLimits(limit));
            var output = new ObservedWriter(Hex("DD"));
            if (limit == 256)
                await Assert.ThrowsAsync<BoltFramingFormatException>(async () => await Next(reader, output, async));
            else Assert.True(await Next(reader, output, async));
            Assert.Equal(limit == 256 ? 770 : 773, reader.Position);
            Assert.Equal(new byte[] { 0xDD }.Concat(Enumerable.Range(0, limit).Select(i => (byte)i)).ToArray(), output.Bytes);
            Assert.Equal(limit, output.Hints.Count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FramingDoesNotValidateOrSplitPackedMessageStructures(bool async)
    {
        // Two zero-field PackStream structures inside one chunk: B2-A must remain opaque.
        using var stream = new ProbeStream(Hex("0004 B002B00F 0000"));
        var reader = new BoltMessageReader(stream);
        var output = new ObservedWriter(Hex("FF"));
        Assert.True(await Next(reader, output, async));
        Assert.Equal(Hex("FFB002B00F"), output.Bytes);
        Assert.False(await Next(reader, output, !async));
        Assert.Equal(8, reader.Position);
    }

    [Fact]
    public async Task WriterCanExceedReceiverLimit_ReceiverRejectsItsHeader()
    {
        var encoded = new ArrayBufferWriter<byte>();
        new BoltMessageWriter(encoded).WriteMessage(Hex("11223344"));
        Assert.Equal(Hex("0004 11223344 0000"), encoded.WrittenSpan.ToArray());
        using var stream = new ProbeStream(encoded.WrittenSpan.ToArray());
        var reader = new BoltMessageReader(stream, new BoltFramingLimits(3));
        var output = new ObservedWriter(Hex("FE"));
        await Assert.ThrowsAsync<BoltFramingFormatException>(async () => await reader.TryReadMessageAsync(output));
        Assert.Equal(2, reader.Position);
        Assert.Empty(output.Hints);
        Assert.Equal(Hex("FE"), output.Bytes);
    }

    private sealed class ObservedWriter : IBufferWriter<byte>
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        public ObservedWriter(byte[] prefix) => _buffer.Write(prefix);
        public List<int> Hints { get; } = new();
        public byte[] Bytes => _buffer.WrittenSpan.ToArray();
        public void Advance(int count) => _buffer.Advance(count);
        public Memory<byte> GetMemory(int sizeHint = 0) { Hints.Add(sizeHint); return _buffer.GetMemory(sizeHint); }
        public Span<byte> GetSpan(int sizeHint = 0) { Hints.Add(sizeHint); return _buffer.GetSpan(sizeHint); }
    }

    // Returns positive partial reads, and zero only at actual EOF. Async reads really suspend.
    // The optional fault boundary splits a read so the next call injects the failure exactly.
    private sealed class ProbeStream(byte[] wire, int boundary = int.MaxValue,
        Exception? failure = null, CancellationTokenSource? cancel = null) : Stream
    {
        private int _offset;
        private int _calls;
        public int SyncCalls { get; private set; }
        public int AsyncCalls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        private int ReadCore(Span<byte> destination, CancellationToken token)
        {
            Assert.False(destination.IsEmpty);
            if (_offset == boundary)
            {
                cancel?.Cancel();
                token.ThrowIfCancellationRequested();
                if (failure is not null) throw failure;
            }
            token.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(destination.Length, wire.Length - _offset),
                Math.Min(new[] { 1, 3, 2, 1 }[_calls++ % 4], boundary - _offset));
            wire.AsSpan(_offset, count).CopyTo(destination);
            _offset += count;
            return count;
        }
        public override int Read(Span<byte> buffer) { SyncCalls++; return ReadCore(buffer, default); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            AsyncCalls++;
            LastToken = cancellationToken;
            await Task.Yield();
            return ReadCore(buffer.Span, cancellationToken);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
