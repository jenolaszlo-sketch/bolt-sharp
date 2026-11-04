using System.Buffers;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using Xunit;
using Xunit.Abstractions;

namespace BoltSharp.Qualification.B2BQ1;

// Expectations come from the specification's field layouts and literal vectors.
// No producer fixture helpers or encoders construct decoder inputs.
public sealed class QualificationProbes(ITestOutputHelper output)
{
    private static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Preamble(params string[] proposals) => Join(Hex("6060B017"),
        Enumerable.Range(0, 4).SelectMany(i => Hex(i < proposals.Length ? proposals[i] : "00000000")).ToArray());
    private static BoltProtocolVersion V(int major, int minor) => new(major, minor);
    private static BoltHandshakeOptions Options(bool manifest = true, ulong caps = 0) =>
        new(new[] { V(5, 6), V(5, 8), V(4, 0), V(4, 2), V(5, 7) }, manifest, caps);
    private static Task<BoltHandshakeResult> Run(BoltHandshakeNegotiator n, GuardStream s, bool async,
        CancellationToken token = default) => async ? n.NegotiateAsync(s, token).AsTask() : Task.FromResult(n.Negotiate(s));

    public static IEnumerable<object[]> LegacyCases()
    {
        // A server-preference loop, range off-by-one, or major-blind match fails these rows.
        var rows = new[] {
            new[] { "00000004", "00000805", "00000000", "00000000", "00000004" },
            new[] { "00000000", "00020705", "00000805", "00000705", "00000705" },
            new[] { "00020204", "00000805", "00000000", "00000000", "00000204" },
            new[] { "00000000", "00000000", "00000000", "00000605", "00000605" },
            new[] { "00000806", "00000505", "00000000", "00000000", "00000000" },
            new[] { "00000705", "00000705", "00020805", "00000000", "00000705" },
            new[] { "000001FF", "00000004", "00000805", "00000000", "00000004" }
        };
        foreach (var row in rows) foreach (var async in new[] { false, true }) yield return new object[] { row, async };
    }

    [Theory, MemberData(nameof(LegacyCases))]
    public async Task Legacy_preference_ranges_duplicates_and_major_isolation(string[] row, bool async)
    {
        var n = new BoltHandshakeNegotiator(Options(false));
        var s = new GuardStream(Join(Preamble(row[..4]), Hex("DEADBEEF")), 1);
        var r = await Run(n, s, async);
        Assert.Equal(Hex(row[4]), s.Written);
        Assert.Equal(row[4] != "00000000", r.Accepted);
        Assert.Equal(!r.Accepted, r.ShouldClose);
        Assert.Equal(20, s.Consumed);
        Assert.False(s.Disposed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Manifest_marker_at_each_position_respects_earlier_legacy_match(bool async)
    {
        for (var slot = 0; slot < 4; slot++)
        {
            var proposals = Enumerable.Repeat("00000009", 4).ToArray();
            proposals[slot] = "000001FF";
            var s = new GuardStream(Join(Preamble(proposals), Hex("0000070500CAFEBABE")), 1);
            var r = await Run(new(Options()), s, async);
            Assert.Equal(BoltHandshakeMode.ManifestV1, r.Mode);
            Assert.Equal(Hex("000001FF0300020805000002040000000400"), s.Written);
            Assert.Equal(25, s.Consumed);
            if (slot == 0) continue;
            proposals[slot - 1] = "00020204";
            s = new GuardStream(Join(Preamble(proposals), Hex("0000070500CAFEBABE")));
            r = await Run(new(Options()), s, async);
            Assert.Equal(BoltHandshakeMode.Legacy, r.Mode);
            Assert.Equal(Hex("00000204"), s.Written);
            Assert.Equal(20, s.Consumed);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Literal_spec_manifest_example_and_zero_capabilities(bool async)
    {
        var versions = Enumerable.Range(0, 5).Select(i => V(4, i)).Concat(Enumerable.Range(6, 3).Select(i => V(5, i)));
        foreach (var selectedCaps in new[] { "08", "00", "09" })
        {
            var s = new GuardStream(Join(Preamble("000001FF", "00000404", "00000003", "00000002"), Hex("00000705" + selectedCaps)), 1);
            var r = await Run(new(new BoltHandshakeOptions(versions, true, 9)), s, async);
            Assert.Equal(Hex("000001FF02000208050004040409"), s.Written);
            Assert.Equal(V(5, 7), r.SelectedVersion);
            Assert.Equal((ulong)Convert.ToByte(selectedCaps, 16), r.NegotiatedCapabilities);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Capability_subsets_use_all_64_bits_and_reject_unoffered_bits(bool async)
    {
        var all = Hex("FFFFFFFFFFFFFFFFFF01");
        var high = Hex("80808080808080808001");
        var n = new BoltHandshakeNegotiator(Options(caps: ulong.MaxValue));
        foreach (var cap in new[] { all, high, Hex("8001"), Hex("00") })
        {
            var s = new GuardStream(Join(Preamble("000001FF"), Hex("00000805"), cap, Hex("EE")), 1);
            var r = await Run(n, s, async);
            Assert.True(r.Accepted);
            Assert.Equal(24 + cap.Length, s.Consumed);
            Assert.Equal(0xEE, s.ReadByte());
        }
        var bad = new GuardStream(Join(Preamble("000001FF"), Hex("000008058001")), 1);
        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(() => Run(new(Options(caps: 127)), bad, async));
        Assert.False(bad.Disposed);
    }

    public static IEnumerable<object[]> Varints()
    {
        var rows = new (ulong, string)[] {
            (0, "00"), (1, "01"), (127, "7F"), (128, "8001"), (255, "FF01"), (256, "8002"),
            (16383, "FF7F"), (16384, "808001"), (4294967295, "FFFFFFFF0F"),
            (4294967296, "8080808010"), (ulong.MaxValue, "FFFFFFFFFFFFFFFFFF01"), (1851775, "FF8271")
        };
        foreach (var (value, hex) in rows) foreach (var async in new[] { false, true }) yield return new object[] { value, hex, async };
    }

    [Theory, MemberData(nameof(Varints))]
    public async Task Varint_literal_vectors_exact_consumption_and_every_truncated_prefix(ulong value, string hex, bool async)
    {
        // Radix-128 least-significant groups; the tenth group has only bit 63 available.
        var bytes = Hex(hex);
        var s = new GuardStream(Join(bytes, Hex("A5")), 1) { MaxRequest = 1 };
        var actual = async ? await BoltVarInt.ReadAsync(s) : BoltVarInt.Read(s);
        Assert.Equal(value, actual);
        Assert.Equal(bytes.Length, s.Consumed);
        Assert.True(BoltVarInt.TryRead(Join(bytes, Hex("A5")), out var decoded, out var consumed));
        Assert.Equal(value, decoded);
        Assert.Equal(bytes.Length, consumed);
        var writer = new ArrayBufferWriter<byte>();
        BoltVarInt.Write(writer, value);
        Assert.Equal(bytes, writer.WrittenSpan.ToArray());
        Assert.Equal(bytes.Length, BoltVarInt.GetEncodedLength(value));
        for (var length = 0; length < bytes.Length; length++)
        {
            s = new GuardStream(bytes[..length], 1);
            await Assert.ThrowsAsync<BoltHandshakeTruncatedException>(async () => {
                if (async) await BoltVarInt.ReadAsync(s); else BoltVarInt.Read(s);
            });
            Assert.False(BoltVarInt.TryRead(bytes[..length], out decoded, out consumed));
            Assert.Equal(0UL, decoded);
            Assert.Equal(0, consumed);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Varint_overflow_overlength_and_nonminimal_are_distinct(bool async)
    {
        foreach (var hex in new[] { "FFFFFFFFFFFFFFFFFF02", "8080808080808080808001", "FFFFFFFFFFFFFFFFFFFF00" })
        {
            var s = new GuardStream(Hex(hex), 1);
            await Assert.ThrowsAsync<BoltHandshakeFormatException>(async () => {
                if (async) await BoltVarInt.ReadAsync(s); else BoltVarInt.Read(s);
            });
            Assert.Equal(10, s.Consumed); // Must not ask for an eleventh byte.
            Assert.False(BoltVarInt.TryRead(Hex(hex), out _, out _));
        }
        foreach (var hex in new[] { "8000", "80808080808080808000" })
        {
            var s = new GuardStream(Join(Hex(hex), Hex("EE")), 1);
            Assert.Equal(0UL, async ? await BoltVarInt.ReadAsync(s) : BoltVarInt.Read(s));
            Assert.Equal(hex.Length / 2, s.Consumed);
        }
    }

    [Fact]
    public void Version_wire_space_and_range_boundaries_are_exhaustively_consistent()
    {
        // Independent byte indexing catches a swapped major/minor or incorrect endian write.
        for (var major = 0; major <= 255; major++) for (var minor = 0; minor <= 255; minor++)
        {
            uint raw = (uint)(minor * 256 + major);
            var v = BoltProtocolVersion.FromUInt32(raw);
            Assert.Equal(major, v.Major);
            Assert.Equal(minor, v.Minor);
            var bytes = new byte[4];
            v.WriteBytes(bytes);
            Assert.Equal(new byte[] { 0, 0, (byte)minor, (byte)major }, bytes);
            Assert.Equal(raw, v.ToUInt32());
        }
        foreach (var raw in new[] { 0x00FFFF05u, 0x00000005u, 0x00030304u })
        {
            var range = BoltVersionRange.FromUInt32(raw);
            Assert.Equal(range.Range + 1, range.Expand().Count);
            Assert.Equal(range.Min, range.Expand()[^1]);
            Assert.False(range.Contains(V(range.Major + 1, 0)));
        }
        foreach (var raw in new[] { 0x00010005u, 0x00020104u, 0x01000805u })
            Assert.Throws<BoltHandshakeFormatException>(() => BoltVersionRange.FromUInt32(raw));
        Assert.True(V(6, 0) > V(5, 255));
        Assert.Equal(V(5, 8).GetHashCode(), V(5, 8).GetHashCode());
        Assert.NotEqual(V(5, 8), V(8, 5));
    }

    [Fact]
    public void Offer_count_cap_and_byte_cap_reject_before_body_reads()
    {
        // The max count is untrusted: cap+1 and ulong.MaxValue must fail after just the count.
        foreach (var hex in new[] { "8102", "FFFFFFFFFFFFFFFFFF01" })
        {
            var s = new GuardStream(Join(Hex("000001FF"), Hex(hex))) { MaxRequest = 4 };
            var before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<BoltHandshakeFormatException>(() => BoltManifestV1.ReadOffer(s));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine($"Rejected count {hex}: read {s.Consumed}, allocated {allocated} bytes");
            Assert.InRange(allocated, 0, 1_000_000);
            Assert.Equal(4 + hex.Length / 2, s.Consumed);
            Assert.Equal(1, s.Requests[^1]);
        }
        foreach (var count in new[] { 0, 1, 255, 256, 257 })
        {
            var encoded = count < 128 ? new[] { (byte)count } : new[] { (byte)((count % 128) + 128), (byte)(count / 128) };
            var bytes = Join(Hex("000001FF"), encoded,
                Enumerable.Range(0, count).SelectMany(_ => Hex("00000805")).ToArray(), Hex("00EE"));
            var s = new GuardStream(bytes, 1) { MaxRequest = 4 };
            if (count == 257)
            {
                Assert.Throws<BoltHandshakeFormatException>(() => BoltManifestV1.ReadOffer(s));
                Assert.Equal(6, s.Consumed);
                continue;
            }
            var required = bytes.Length - 1;
            for (var delta = -1; delta <= 1; delta++)
            {
                s = new GuardStream(bytes, 1) { MaxRequest = 4 };
                var limits = new BoltHandshakeLimits(maxHandshakeBytes: required + delta);
                if (delta == -1) Assert.Throws<BoltHandshakeFormatException>(() => BoltManifestV1.ReadOffer(s, limits));
                else
                {
                    var offer = BoltManifestV1.ReadOffer(s, limits);
                    Assert.Equal(count, offer.Ranges.Count);
                    Assert.Equal(required, s.Consumed);
                    Assert.Equal(0xEE, s.ReadByte());
                }
                Assert.True(s.Consumed <= required + Math.Max(delta, 0) + 1);
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Negotiation_byte_cap_is_checked_before_each_field_read(bool async)
    {
        foreach (var manifest in new[] { false, true })
        {
            var bytes = manifest ? Join(Preamble("000001FF"), Hex("000008058001")) : Preamble("00000805");
            for (var delta = -1; delta <= 1; delta++)
            {
                var n = new BoltHandshakeNegotiator(Options(manifest, 128), new(maxHandshakeBytes: bytes.Length + delta));
                var s = new GuardStream(Join(bytes, Hex("FF")), 1);
                if (delta < 0)
                {
                    await Assert.ThrowsAsync<BoltHandshakeFormatException>(() => Run(n, s, async));
                    Assert.Equal(manifest ? 25 : 4, s.Consumed);
                }
                else
                {
                    Assert.True((await Run(n, s, async)).Accepted);
                    Assert.Equal(bytes.Length, s.Consumed);
                }
                Assert.False(s.Disposed);
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Disconnect_at_every_handshake_prefix_and_malformed_transitions(bool async)
    {
        var bytes = Join(Preamble("000001FF"), Hex("000008058001"));
        for (var length = 0; length < bytes.Length; length++)
        {
            var s = new GuardStream(bytes[..length], 1);
            await Assert.ThrowsAsync<BoltHandshakeTruncatedException>(() => Run(new(Options(caps: 128)), s, async));
            Assert.Equal(length, s.Consumed);
            Assert.False(s.Disposed);
            if (length < 20) Assert.Empty(s.Written);
        }
        foreach (var bad in new[] { "0100080500", "0001080500", "0000050500", "0000080501" })
        {
            var s = new GuardStream(Join(Preamble("000001FF"), Hex(bad)), 1);
            var ex = await Record.ExceptionAsync(() => Run(new(Options()), s, async));
            Assert.IsAssignableFrom<BoltHandshakeException>(ex);
            Assert.Equal(bad.StartsWith("01") || bad.StartsWith("0001") ? typeof(BoltHandshakeFormatException) : typeof(BoltHandshakeProtocolException), ex.GetType());
        }
        var wrongMagic = new GuardStream(Hex("6060B016"));
        await Assert.ThrowsAsync<BoltHandshakeFormatException>(() => Run(new(Options()), wrongMagic, async));
        Assert.Equal(4, wrongMagic.Consumed);
        Assert.Empty(wrongMagic.Written);
        // Documented short-circuit policy: an unreachable malformed proposal does not defeat a match.
        var reached = new GuardStream(Preamble("01000805", "00000805"));
        await Assert.ThrowsAsync<BoltHandshakeFormatException>(() => Run(new(Options()), reached, async));
        var unreachable = new GuardStream(Preamble("00000805", "01000805"));
        Assert.True((await Run(new(Options()), unreachable, async)).Accepted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pipelined_frames_and_partial_next_header_remain_exact(bool async)
    {
        foreach (var manifest in new[] { false, true }) foreach (var fragment in new[] { 1, int.MaxValue })
        foreach (var suffix in new[] { "00", "0002B00100000001A50000" })
        {
            var handshake = manifest ? Join(Preamble("000001FF"), Hex("000008058001")) : Preamble("00000805");
            var s = new GuardStream(Join(handshake, Hex(suffix)), fragment);
            var result = await Run(new(Options(manifest, 128)), s, async);
            Assert.True(result.Accepted);
            Assert.Equal(handshake.Length, s.Consumed);
            Assert.Equal(Hex(suffix), s.Remaining);
            Assert.False(s.Disposed);
            if (suffix == "00") continue;
            var reader = new BoltMessageReader(s);
            Assert.Equal(Hex("B001"), async ? await reader.ReadMessageAsync() : reader.ReadMessage());
            Assert.Equal(Hex("A5"), async ? await reader.ReadMessageAsync() : reader.ReadMessage());
            Assert.Null(async ? await reader.ReadMessageAsync() : reader.ReadMessage());
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Read_write_and_flush_failures_publish_no_result_and_preserve_ownership(bool async)
    {
        foreach (var manifest in new[] { false, true }) foreach (var failure in new[] { "read4", "write", "flush", "read24" })
        {
            if (!manifest && failure == "read24") continue;
            var bytes = manifest ? Join(Preamble("000001FF"), Hex("000008058001")) : Preamble("00000805");
            var s = new GuardStream(bytes, 1) {
                FailReadAt = failure == "read4" ? 4 : failure == "read24" ? 24 : null,
                FailWrite = failure == "write", FailFlush = failure == "flush"
            };
            BoltHandshakeResult? published = null;
            var ex = await Assert.ThrowsAsync<IOException>(async () => published = await Run(new(Options(manifest, 128)), s, async));
            Assert.Same(s.Failure, ex);
            Assert.Null(published);
            Assert.False(s.Disposed);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(20)] [InlineData(24)] [InlineData(25)]
    public async Task Cancellation_at_async_transitions_abandons_incomplete_state(int at)
    {
        using var cts = new CancellationTokenSource();
        var s = new GuardStream(Join(Preamble("000001FF"), Hex("000008058001")), 1) { CancelAt = at, Cancellation = cts };
        BoltHandshakeResult? published = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => published = await Run(new(Options(caps: 128)), s, true, cts.Token));
        Assert.Null(published);
        Assert.Equal(at, s.Consumed);
        Assert.False(s.Disposed);
        Assert.True((await Run(new(Options(false)), new(Preamble("00000805")), true)).Accepted);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Reused_configuration_has_no_cross_stream_state_and_default_is_honest(bool async)
    {
        var n = new BoltHandshakeNegotiator(Options());
        for (var i = 0; i < 5; i++)
        {
            var s = new GuardStream(Preamble("00000004"));
            Assert.Equal(V(4, 0), (await Run(n, s, async)).SelectedVersion);
            s = new GuardStream(Join(Preamble("000001FF"), Hex("0000060500")));
            Assert.Equal(V(5, 6), (await Run(n, s, async)).SelectedVersion);
        }
        Assert.Empty(BoltHandshakeOptions.Default.SupportedVersions);
        Assert.False(BoltHandshakeOptions.Default.ManifestV1Enabled);
        foreach (var o in new[] { BoltHandshakeOptions.Default, new BoltHandshakeOptions(Array.Empty<BoltProtocolVersion>(), true) })
        {
            var s = new GuardStream(Preamble("000001FF", "00000805"));
            var r = await Run(new(o), s, async);
            Assert.False(r.Accepted);
            Assert.Null(r.SelectedVersion);
            Assert.True(r.ShouldClose);
            Assert.Equal(Hex("00000000"), s.Written);
            await Assert.ThrowsAsync<BoltHandshakeFormatException>(() => Run(new(o), new(Preamble("01000805")), async));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Zero_version_must_never_be_a_successful_manifest_selection(bool async)
    {
        // SPEC: zero ALWAYS denotes no protocol. Explicit configuration cannot turn it into success.
        // A range proposal includes 0.0 too, so this is not merely a zero-placeholder parsing case.
        var n = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(0, 0) }, true));
        var s = new GuardStream(Join(Preamble("000001FF"), Hex("0000000000")));
        var r = await Run(n, s, async);
        output.WriteLine($"wire={Convert.ToHexString(s.Written)}; accepted={r.Accepted}; selected={r.SelectedVersion}; close={r.ShouldClose}");
        Assert.False(r.Accepted, "Protocol zero is no version; manifest must not publish Accepted=true for 0.0.");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Range_containing_zero_must_not_produce_accepted_legacy_zero_response(bool async)
    {
        var n = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(0, 0) }, false));
        var s = new GuardStream(Preamble("00010100")); // Major 0, minors 1 down through 0.
        var r = await Run(n, s, async);
        output.WriteLine($"wire={Convert.ToHexString(s.Written)}; accepted={r.Accepted}; selected={r.SelectedVersion}; close={r.ShouldClose}");
        Assert.Equal(Hex("00000000"), s.Written);
        Assert.False(r.Accepted, "A zero wire response must be a rejection, even with a configured 0.0.");
        Assert.True(r.ShouldClose);
        Assert.Null(r.SelectedVersion);
    }
}

// Reviewer-owned duplex stream. Positive-length zero means EOF. Reads never fabricate bytes.
// Cancellation is deterministic at a byte boundary; thrown I/O exceptions retain identity.
internal sealed class GuardStream(byte[] input, int fragment = int.MaxValue) : Stream
{
    private readonly MemoryStream written = new();
    internal int Consumed { get; private set; }
    internal byte[] Remaining => input[Consumed..];
    internal byte[] Written => written.ToArray();
    internal List<int> Requests { get; } = new();
    internal int MaxRequest { get; init; } = 16;
    internal bool Disposed { get; private set; }
    internal int? FailReadAt { get; init; }
    internal bool FailWrite { get; init; }
    internal bool FailFlush { get; init; }
    internal IOException Failure { get; } = new("reviewer-injected I/O failure");
    internal int? CancelAt { get; init; }
    internal CancellationTokenSource? Cancellation { get; init; }
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => input.Length;
    public override long Position { get => Consumed; set => throw new NotSupportedException(); }
    public override int Read(Span<byte> buffer)
    {
        Requests.Add(buffer.Length);
        Assert.True(buffer.Length <= MaxRequest, $"Unexpected read request: {buffer.Length}");
        if (FailReadAt is int at && Consumed >= at) throw Failure;
        int n = Math.Min(Math.Min(fragment, buffer.Length), input.Length - Consumed);
        input.AsSpan(Consumed, n).CopyTo(buffer);
        Consumed += n;
        return n;
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (CancelAt is int at && Consumed >= at) Cancellation!.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (FailWrite) { written.Write(buffer[..Math.Min(2, buffer.Length)]); throw Failure; }
        written.Write(buffer);
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override void Flush() { if (FailFlush) throw Failure; }
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flush();
        return Task.CompletedTask;
    }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
