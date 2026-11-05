using System.Reflection;
using System.Text;
using BoltSharp.Handshake;
using Xunit;

namespace BoltSharp.Qualification.B2BQ2;

// Reviewer-owned B2-B-Q2 requalification probes. Fixtures are literal wire bytes and independent
// radix/byte arithmetic; production encoders are not used to build decoder inputs. Where a probe
// claims to detect B2BQ1-F01, a reference "pre-remediation" algorithm is implemented locally and
// the probe asserts that the old behaviour differs from the current behaviour, so the assertion
// demonstrably discriminates the defect rather than merely restating the implementation.
public sealed class QualificationProbes
{
    private const uint Marker = 0x0000_01FFu;

    private static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static BoltProtocolVersion V(int major, int minor) => new(major, minor);

    private static byte[] Preamble(params string[] proposals) =>
        Join(Hex("6060B017"),
            Enumerable.Range(0, 4).SelectMany(i => Hex(i < proposals.Length ? proposals[i] : "00000000")).ToArray());

    private static Task<BoltHandshakeResult> Run(
        BoltHandshakeNegotiator negotiator, ReviewStream stream, bool async,
        CancellationToken token = default) =>
        async ? negotiator.NegotiateAsync(stream, token).AsTask() : Task.FromResult(negotiator.Negotiate(stream));

    // ---- independent wire encoders (not the production writers) ---------------

    private static byte[] VarInt(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            bytes.Add(b);
        }
        while (value != 0);

        return bytes.ToArray();
    }

    private static byte[] OfferBytes(uint[] ranges, ulong capabilities)
    {
        var bytes = new List<byte> { 0x00, 0x00, 0x01, 0xFF };
        bytes.AddRange(VarInt((ulong)ranges.Length));
        foreach (var range in ranges)
        {
            bytes.Add((byte)(range >> 24));
            bytes.Add((byte)(range >> 16));
            bytes.Add((byte)(range >> 8));
            bytes.Add((byte)range);
        }

        bytes.AddRange(VarInt(capabilities));
        return bytes.ToArray();
    }

    // Pre-remediation reference algorithms (B2BQ1-F01). Used only as negative controls.
    private static IEnumerable<uint> OldExpandLegacy(uint proposal)
    {
        var reserved = (proposal >> 24) & 0xFF;
        var range = (proposal >> 16) & 0xFF;
        var minor = (proposal >> 8) & 0xFF;
        var major = proposal & 0xFF;
        if (reserved != 0 || range > minor)
        {
            yield break;
        }

        for (var i = 0; i <= range; i++)
        {
            yield return (uint)(((minor - i) << 8) | major);
        }
    }

    private static bool OldLegacyWouldSelectZero(uint proposal, ISet<uint> configured)
    {
        if (proposal == 0 || proposal == Marker)
        {
            return false;
        }

        foreach (var candidate in OldExpandLegacy(proposal))
        {
            if (configured.Contains(candidate))
            {
                return candidate == 0;
            }
        }

        return false;
    }

    // ---- Q2-01 configuration filtering ---------------------------------------

    [Fact]
    public void Configuration_filters_protocol_zero_and_preserves_order()
    {
        Assert.Empty(new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: false).SupportedVersions);
        Assert.Empty(BoltHandshakeOptions.Create(new[] { V(0, 0) }).SupportedVersions);

        var mixed = new BoltHandshakeOptions(
            new[] { V(0, 0), V(5, 8), V(4, 2), V(5, 8), V(0, 0), V(5, 7) },
            manifestV1Enabled: false);
        Assert.Equal(new[] { V(5, 8), V(5, 7), V(4, 2) }, mixed.SupportedVersions);

        foreach (var options in new[]
        {
            new BoltHandshakeOptions(new[] { V(0, 0) }, false),
            mixed,
            BoltHandshakeOptions.Create(new[] { V(0, 0), V(0, 1) }),
        })
        {
            Assert.DoesNotContain(default(BoltProtocolVersion), options.SupportedVersions);
        }

        var created = BoltHandshakeOptions.Create(new[] { V(0, 0), V(5, 6), V(5, 7), V(5, 8) });
        Assert.True(created.ManifestV1Enabled);
        Assert.Equal(new[] { V(5, 8), V(5, 7), V(5, 6) }, created.SupportedVersions);
    }

    [Fact]
    public void Offer_ranges_never_cover_protocol_zero()
    {
        foreach (var versions in new[]
        {
            new[] { V(0, 0) },
            new[] { V(0, 0), V(0, 1) },
            new[] { V(0, 0), V(0, 1), V(0, 2) },
            new[] { V(0, 0), V(5, 8) },
            new[] { V(0, 0), V(1, 0) },
        })
        {
            var negotiator = new BoltHandshakeNegotiator(new BoltHandshakeOptions(versions, manifestV1Enabled: true));
            Assert.DoesNotContain(negotiator.OfferRanges, r => r.Contains(default(BoltProtocolVersion)));
            Assert.DoesNotContain(negotiator.OfferRanges, r => r.Major == 0 && r.MinMinor == 0);
        }

        // Negative control: the pre-remediation configuration {0.0} produced a range covering 0.0.
        var oldRange = new BoltVersionRange(0, 0, 0);
        Assert.True(oldRange.Contains(default(BoltProtocolVersion)));
    }

    // ---- Q2-02 legacy negotiation --------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_zero_only_config_rejects_range_spanning_zero(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: false));
        var stream = new ReviewStream(Preamble("00010100"));
        var result = await Run(negotiator, stream, async);

        Assert.Equal(Hex("00000000"), stream.Written);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.Equal(20, stream.Consumed);
        Assert.False(stream.Disposed);

        // Negative control: the pre-remediation algorithm selects 0.0 from the same proposal.
        Assert.True(OldLegacyWouldSelectZero(0x0001_0100u, new HashSet<uint> { 0u }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_zero_only_config_concrete_zero_placeholder_rejects(bool async)
    {
        foreach (var proposals in new[] { new[] { "00000000" }, new[] { "00000000", "00000000", "00000000", "00000000" } })
        {
            var negotiator = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: false));
            var stream = new ReviewStream(Preamble(proposals));
            var result = await Run(negotiator, stream, async);

            Assert.Equal(Hex("00000000"), stream.Written);
            Assert.False(result.Accepted);
            Assert.True(result.ShouldClose);
            Assert.Null(result.SelectedVersion);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_zero_placeholders_preceding_valid_proposal_negotiate(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, manifestV1Enabled: false));
        var stream = new ReviewStream(Join(Preamble("00000000", "00000000", "00000805"), Hex("CAFEBABE")));
        var result = await Run(negotiator, stream, async);

        Assert.True(result.Accepted);
        Assert.False(result.ShouldClose);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(Hex("00000805"), stream.Written);
        Assert.Equal(20, stream.Consumed);
        Assert.Equal(Hex("CAFEBABE"), stream.Remaining);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_preference_order_first_match_wins(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(
            new BoltHandshakeOptions(new[] { V(5, 7), V(5, 8) }, manifestV1Enabled: false));

        var first = new ReviewStream(Preamble("00000705", "00000805"));
        Assert.Equal(V(5, 7), (await Run(negotiator, first, async)).SelectedVersion);
        Assert.Equal(Hex("00000705"), first.Written);

        var second = new ReviewStream(Preamble("00000805", "00000705"));
        Assert.Equal(V(5, 8), (await Run(negotiator, second, async)).SelectedVersion);
        Assert.Equal(Hex("00000805"), second.Written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_range_selects_highest_supported_minor_and_skips_zero(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(
            new BoltHandshakeOptions(new[] { V(5, 6), V(5, 7), V(5, 8) }, manifestV1Enabled: false));
        var range = new ReviewStream(Preamble("00020805")); // 5.8 down to 5.6
        var result = await Run(negotiator, range, async);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(Hex("00000805"), range.Written);

        // Major 0 range 0.1..0.0 with only 0.1 supported must select 0.1, not 0.0.
        var zeroRange = new BoltHandshakeNegotiator(
            new BoltHandshakeOptions(new[] { V(0, 1) }, manifestV1Enabled: false));
        var stream = new ReviewStream(Preamble("00010100"));
        var zeroResult = await Run(zeroRange, stream, async);
        Assert.True(zeroResult.Accepted);
        Assert.Equal(V(0, 1), zeroResult.SelectedVersion);
        Assert.Equal(Hex("00000100"), stream.Written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_no_match_writes_four_zero_bytes_and_preserves_suffix(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, manifestV1Enabled: false));
        var stream = new ReviewStream(Join(Preamble("00000705"), Hex("DEADBEEF")));
        var result = await Run(negotiator, stream, async);

        Assert.Equal(Hex("00000000"), stream.Written);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.Equal(0UL, result.NegotiatedCapabilities);
        Assert.Equal(20, stream.Consumed);
        Assert.Equal(Hex("DEADBEEF"), stream.Remaining);
        Assert.False(stream.Disposed);
    }

    // ---- Q2-03 manifest negotiation ------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_zero_only_config_does_not_advertise_or_accept(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: true));
        var stream = new ReviewStream(Join(Preamble("000001FF"), Hex("0000000000")));
        var result = await Run(negotiator, stream, async);

        Assert.Equal(Hex("00000000"), stream.Written);
        Assert.Equal(BoltHandshakeMode.Legacy, result.Mode);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.False(stream.Disposed);

        // Negative control: the pre-remediation server offered a 0.0 range then accepted 0.0.
        var oldOffer = OfferBytes(new[] { 0x0000_0000u }, 0);
        Assert.Equal(Hex("000001FF010000000000"), oldOffer);
        Assert.NotEqual(oldOffer, stream.Written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_valid_offer_zero_selection_throws_protocol(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(
            BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0));
        var stream = new ReviewStream(Join(Preamble("000001FF"), Hex("0000000000")));
        BoltHandshakeResult? published = null;

        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(
            async () => published = await Run(negotiator, stream, async));

        Assert.Null(published);
        Assert.Equal(Hex("000001FF010000080500"), stream.Written);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_major_zero_offer_still_rejects_zero_selection(bool async)
    {
        // Offer 0.1 only; a client selecting 0.0 must be rejected, and the offer itself must not
        // include 0.0 even though it is a lower minor of the same major.
        var negotiator = new BoltHandshakeNegotiator(
            BoltHandshakeOptions.Create(new[] { V(0, 1) }, manifestV1Enabled: true, offeredCapabilities: 0));
        var stream = new ReviewStream(Join(Preamble("000001FF"), Hex("0000000000")));

        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(
            async () => { _ = await Run(negotiator, stream, async); });

        Assert.Equal(OfferBytes(new[] { 0x0000_0100u }, 0), stream.Written);
        Assert.DoesNotContain("00000000", SplitWrittenRanges(stream.Written).Select(Convert.ToHexString));
    }

    private static IEnumerable<byte[]> SplitWrittenRanges(byte[] written)
    {
        // written is marker + count + 4-byte range(s) + caps; decode only the range fields.
        var count = written[4];
        for (var i = 0; i < count; i++)
        {
            yield return written[(5 + i * 4)..(9 + i * 4)];
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_spec_style_offer_and_selection_bytes(bool async)
    {
        var versions = Enumerable.Range(0, 5).Select(i => V(4, i))
            .Concat(Enumerable.Range(6, 3).Select(i => V(5, i)));
        var negotiator = new BoltHandshakeNegotiator(
            new BoltHandshakeOptions(versions, manifestV1Enabled: true, offeredCapabilities: 9));
        var stream = new ReviewStream(Join(
            Preamble("000001FF", "00000404", "00000003", "00000002"),
            Hex("0000070508")));

        var result = await Run(negotiator, stream, async);

        Assert.Equal(Hex("000001FF02000208050004040409"), stream.Written);
        Assert.True(result.Accepted);
        Assert.Equal(BoltHandshakeMode.ManifestV1, result.Mode);
        Assert.Equal(V(5, 7), result.SelectedVersion);
        Assert.Equal(8UL, result.NegotiatedCapabilities);
        Assert.Equal(25, stream.Consumed);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_capability_superset_and_unoffered_version_stay_protocol(bool async)
    {
        var options = BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0x08);
        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(async () =>
            { _ = await Run(new BoltHandshakeNegotiator(options),
                new ReviewStream(Join(Preamble("000001FF"), Hex("0000080509"))), async); });
        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(async () =>
            { _ = await Run(new BoltHandshakeNegotiator(options),
                new ReviewStream(Join(Preamble("000001FF"), Hex("0000070500"))), async); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_zero_placeholders_around_marker_negotiate(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(
            BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0));
        var stream = new ReviewStream(Join(Preamble("000001FF", "00000000", "00000000"), Hex("0000080500")));
        var result = await Run(negotiator, stream, async);

        Assert.True(result.Accepted);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(OfferBytes(new[] { 0x0000_0805u }, 0), stream.Written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_mixed_config_offers_only_valid_versions(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(
            BoltHandshakeOptions.Create(new[] { V(0, 0), V(5, 7), V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0));
        var stream = new ReviewStream(Join(Preamble("000001FF"), Hex("0000070500")));
        var result = await Run(negotiator, stream, async);

        Assert.Equal(OfferBytes(new[] { 0x0001_0805u }, 0), stream.Written);
        Assert.True(result.Accepted);
        Assert.Equal(V(5, 7), result.SelectedVersion);
    }

    // ---- Q2-04 failure-state, parity, cancellation, truncation, pipelining ----

    [Fact]
    public async Task Sync_and_async_are_equivalent_on_every_scenario()
    {
        var scenarios = new (BoltHandshakeOptions Options, byte[] Input)[]
        {
            (new BoltHandshakeOptions(new[] { V(0, 0) }, false), Preamble("00010100")),
            (new BoltHandshakeOptions(new[] { V(5, 8) }, false), Preamble("00000805")),
            (new BoltHandshakeOptions(new[] { V(5, 8) }, false), Preamble("00000705")),
            (new BoltHandshakeOptions(new[] { V(0, 0) }, true), Join(Preamble("000001FF"), Hex("0000000000"))),
            (BoltHandshakeOptions.Create(new[] { V(5, 8) }, true, 0), Join(Preamble("000001FF"), Hex("0000080500"))),
        };

        foreach (var (options, input) in scenarios)
        {
            var syncStream = new ReviewStream(input);
            var sync = new BoltHandshakeNegotiator(options).Negotiate(syncStream);
            var asyncStream = new ReviewStream(input);
            var asyncResult = await new BoltHandshakeNegotiator(options).NegotiateAsync(asyncStream);

            Assert.Equal(sync.Accepted, asyncResult.Accepted);
            Assert.Equal(sync.ShouldClose, asyncResult.ShouldClose);
            Assert.Equal(sync.SelectedVersion, asyncResult.SelectedVersion);
            Assert.Equal(sync.NegotiatedCapabilities, asyncResult.NegotiatedCapabilities);
            Assert.Equal(sync.Mode, asyncResult.Mode);
            Assert.Equal(syncStream.Written, asyncStream.Written);
            Assert.Equal(syncStream.Consumed, asyncStream.Consumed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_paths_never_publish_accepted_state(bool async)
    {
        // Legacy rejection.
        var rejectStream = new ReviewStream(Preamble("00000705"));
        var reject = await Run(new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, false)), rejectStream, async);
        Assert.False(reject.Accepted);
        Assert.True(reject.ShouldClose);
        Assert.Null(reject.SelectedVersion);

        // Manifest zero selection: no result at all.
        BoltHandshakeResult? published = null;
        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(async () =>
            published = await Run(
                new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }, true, 0)),
                new ReviewStream(Join(Preamble("000001FF"), Hex("0000000000"))), async));
        Assert.Null(published);

        // Truncated preamble: no result, no response.
        var truncated = new ReviewStream(Preamble("000001FF")[..12]);
        await Assert.ThrowsAsync<BoltHandshakeTruncatedException>(async () =>
            { _ = await Run(new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, true)), truncated, async); });
        Assert.Empty(truncated.Written);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(25)]
    public async Task Cancellation_abandons_incomplete_state(int at)
    {
        using var cts = new CancellationTokenSource();
        var input = Join(Preamble("000001FF"), Hex("000008058001"));
        var stream = new ReviewStream(input) { CancelAt = at, Cancellation = cts };
        BoltHandshakeResult? published = null;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            published = await Run(
                new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }, true, 0x80)),
                stream, async: true, cts.Token));

        Assert.Null(published);
        Assert.Equal(at, stream.Consumed);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Truncated_prefixes_raise_truncated_without_early_response(bool async)
    {
        var bytes = Join(Preamble("000001FF"), Hex("0000080500"));
        for (var length = 0; length < bytes.Length; length++)
        {
            var stream = new ReviewStream(bytes[..length]);
            await Assert.ThrowsAsync<BoltHandshakeTruncatedException>(async () =>
                { _ = await Run(new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }, true, 0)), stream, async); });
            Assert.Equal(length, stream.Consumed);
            if (length < 20)
            {
                Assert.Empty(stream.Written);
            }

            Assert.False(stream.Disposed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_input_stays_typed(bool async)
    {
        await Assert.ThrowsAsync<BoltHandshakeFormatException>(async () =>
            { _ = await Run(new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, false)),
                new ReviewStream(Hex("6060B016")), async); });

        await Assert.ThrowsAsync<BoltHandshakeFormatException>(async () =>
            { _ = await Run(new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, false)),
                new ReviewStream(Preamble("01000805")), async); });

        await Assert.ThrowsAsync<BoltHandshakeFormatException>(async () =>
            { _ = await Run(new BoltHandshakeNegotiator(new BoltHandshakeOptions(new[] { V(5, 8) }, false)),
                new ReviewStream(Preamble("00020104")), async); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pipelined_suffix_preserved_after_manifest_success(bool async)
    {
        var input = Join(Preamble("000001FF"), Hex("0000080500"), Hex("000002B00100000001A50000"));
        var stream = new ReviewStream(input);
        var result = await Run(
            new BoltHandshakeNegotiator(BoltHandshakeOptions.Create(new[] { V(5, 8) }, true, 0)), stream, async);

        Assert.True(result.Accepted);
        Assert.Equal(25, stream.Consumed);
        Assert.Equal(Hex("000002B00100000001A50000"), stream.Remaining);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_negotiations_do_not_leak_state(bool async)
    {
        var negotiator = new BoltHandshakeNegotiator(
            new BoltHandshakeOptions(new[] { V(0, 0), V(5, 8) }, manifestV1Enabled: false));

        for (var i = 0; i < 5; i++)
        {
            var zero = new ReviewStream(Preamble("00010100"));
            var zeroResult = await Run(negotiator, zero, async);
            Assert.False(zeroResult.Accepted);
            Assert.True(zeroResult.ShouldClose);
            Assert.Null(zeroResult.SelectedVersion);
            Assert.Equal(Hex("00000000"), zero.Written);

            var ok = new ReviewStream(Preamble("00000805"));
            var okResult = await Run(negotiator, ok, async);
            Assert.True(okResult.Accepted);
            Assert.False(okResult.ShouldClose);
            Assert.Equal(V(5, 8), okResult.SelectedVersion);
            Assert.Equal(Hex("00000805"), ok.Written);
        }
    }

    [Fact]
    public void Accepted_result_factory_guard_rejects_zero_by_reflection()
    {
        var method = typeof(BoltHandshakeResult).GetMethod(
            "AcceptedLegacy",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(BoltProtocolVersion) },
            modifiers: null);
        Assert.NotNull(method);

        var thrown = Assert.Throws<TargetInvocationException>(
            () => method!.Invoke(null, new object[] { default(BoltProtocolVersion) }));
        Assert.IsType<ArgumentOutOfRangeException>(thrown.InnerException);
    }

    [Fact]
    public void Version_range_wire_space_boundaries_are_consistent()
    {
        // Independent byte indexing: reserved, range, minor, major.
        for (var major = 0; major <= 255; major += 17)
        {
            for (var minor = 0; minor <= 255; minor += 17)
            {
                var v = V(major, minor);
                var bytes = new byte[4];
                v.WriteBytes(bytes);
                Assert.Equal(new byte[] { 0, 0, (byte)minor, (byte)major }, bytes);
                Assert.Equal((uint)((minor << 8) | major), v.ToUInt32());
            }
        }

        Assert.Throws<BoltHandshakeFormatException>(() => BoltVersionRange.FromUInt32(0x0002_0104u)); // range spans major (00 02 01 04)
        Assert.Throws<BoltHandshakeFormatException>(() => BoltVersionRange.FromUInt32(0x0100_0805u)); // reserved byte
        var range = BoltVersionRange.FromUInt32(0x0003_0805u); // 5.8..5.5
        Assert.Equal(4, range.Expand().Count);
        Assert.Equal(V(5, 5), range.Expand()[^1]);
        Assert.Equal(V(5, 8), range.Max);
        Assert.Equal(V(5, 5), range.Min);
        Assert.False(range.Contains(V(5, 4)));
        Assert.False(range.Contains(V(6, 8)));
    }
}

// Reviewer-owned duplex stream. Positive-length zero means EOF. Reads never fabricate bytes.
// Cancellation is deterministic at a byte boundary; injected I/O failures retain identity.
internal sealed class ReviewStream(byte[] input, int fragment = int.MaxValue) : Stream
{
    private readonly MemoryStream _written = new();
    internal int Consumed { get; private set; }
    internal byte[] Written => _written.ToArray();
    internal byte[] Remaining => input[Consumed..];
    internal bool Disposed { get; private set; }
    internal int MaxRequest { get; init; } = 64;
    internal int? CancelAt { get; init; }
    internal CancellationTokenSource? Cancellation { get; init; }
    internal int? FailReadAt { get; init; }
    internal bool FailWrite { get; init; }
    internal bool FailFlush { get; init; }
    internal IOException Failure { get; } = new("q2-injected I/O failure");
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => input.Length;
    public override long Position { get => Consumed; set => throw new NotSupportedException(); }
    public override int Read(Span<byte> buffer)
    {
        Assert.True(buffer.Length <= MaxRequest, $"read request {buffer.Length} exceeds {MaxRequest}");
        if (FailReadAt is int at && Consumed >= at)
        {
            throw Failure;
        }

        var n = Math.Min(Math.Min(fragment, buffer.Length), input.Length - Consumed);
        input.AsSpan(Consumed, n).CopyTo(buffer);
        Consumed += n;
        return n;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (CancelAt is int at && Consumed >= at)
        {
            Cancellation!.Cancel();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (FailWrite)
        {
            _written.Write(buffer[..Math.Min(2, buffer.Length)]);
            throw Failure;
        }

        _written.Write(buffer);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
        if (FailFlush)
        {
            throw Failure;
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
        Disposed = true;
        base.Dispose(disposing);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
