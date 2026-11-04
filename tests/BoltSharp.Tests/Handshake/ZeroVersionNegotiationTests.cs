using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// B2-B-R1 regression coverage for finding B2BQ1-F01 (<c>docs/reviews/B2-B-Q1-independent-qualification.md</c>):
/// protocol zero must never be configured, offered, selected or published as accepted state,
/// while a zero-filled client proposal slot remains a valid placeholder. Covers the legacy and
/// Manifest v1 schemes on both the synchronous and asynchronous negotiation paths.
/// </summary>
public sealed class ZeroVersionNegotiationTests
{
    private static BoltProtocolVersion V(int major, int minor) => new(major, minor);

    private static async Task<(BoltHandshakeResult Result, byte[] Written, HandshakeDuplexStream Stream)> Run(
        BoltHandshakeOptions options,
        byte[] input,
        bool async)
    {
        var stream = new HandshakeDuplexStream(input);
        var negotiator = new BoltHandshakeNegotiator(options);
        var result = async ? await negotiator.NegotiateAsync(stream) : negotiator.Negotiate(stream);
        return (result, stream.WrittenBytes, stream);
    }

    // ---- 1. explicit 0.0 server configuration -------------------------------

    [Fact]
    public void Configuration_WithOnlyZero_ExcludesItFromSupportedVersions()
    {
        foreach (var options in new[]
        {
            new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: false),
            new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: true),
            BoltHandshakeOptions.Create(new[] { V(0, 0) }),
        })
        {
            Assert.Empty(options.SupportedVersions);
            Assert.DoesNotContain(V(0, 0), options.SupportedVersions);
        }
    }

    [Fact]
    public void Configuration_WithZeroAndValidVersions_KeepsOnlyTheValidVersions()
    {
        var options = new BoltHandshakeOptions(new[] { V(0, 0), V(5, 8), V(4, 2), V(0, 0) }, manifestV1Enabled: false);

        Assert.Equal(new[] { V(5, 8), V(4, 2) }, options.SupportedVersions);
    }

    [Fact]
    public void Negotiator_FromZeroOnlyConfiguration_OffersNoRanges()
    {
        var negotiator = new BoltHandshakeNegotiator(
            new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: true));

        Assert.Empty(negotiator.OfferRanges);
    }

    // ---- 2. legacy sync and async -------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_ZeroOnlyConfiguration_RangeContainingZero_RejectsAndCloses(bool async)
    {
        // Client proposes major 0, minors 1 down through 0 (00 01 01 00); the range includes 0.0.
        var options = new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: false);
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Range(0, 1, 1));

        var (result, written, stream) = await Run(options, input, async);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_ZeroOnlyConfiguration_ConcreteZeroProposal_IsPlaceholderAndRejects(bool async)
    {
        // A concrete 0.0 proposal is the wire zero placeholder, not a negotiable version.
        var options = new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: false);
        var input = HandshakeTestHelpers.ClientBytes(0u);

        var (result, written, _) = await Run(options, input, async);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
    }

    // ---- 3. manifest sync and async -----------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_ZeroOnlyConfiguration_MarkerAndZeroSelection_RejectsAndCloses(bool async)
    {
        // No supported version survives construction, so the marker is an unsupported proposal
        // and the negotiation is a plain legacy rejection with no manifest offer.
        var options = new BoltHandshakeOptions(new[] { V(0, 0) }, manifestV1Enabled: true);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(0, 0, 0));

        var (result, written, stream) = await Run(options, input, async);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
        Assert.Equal(BoltHandshakeMode.Legacy, result.Mode);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.False(stream.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_ValidOffer_ZeroSelection_ThrowsProtocolWithoutPublishing(bool async)
    {
        var options = BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(0, 0, 0));
        var stream = new HandshakeDuplexStream(input);
        var negotiator = new BoltHandshakeNegotiator(options);
        BoltHandshakeResult? published = null;

        await Assert.ThrowsAsync<BoltHandshakeProtocolException>(async () =>
            published = async ? await negotiator.NegotiateAsync(stream) : negotiator.Negotiate(stream));

        Assert.Null(published);
        // The server still advertised its legitimate nonzero offer before rejecting the selection.
        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0), stream.WrittenBytes);
        Assert.False(stream.Disposed);
    }

    // ---- 4. zero placeholders alongside valid client proposals ---------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_ZeroPlaceholders_BeforeValidProposal_StillNegotiate(bool async)
    {
        var options = new BoltHandshakeOptions(new[] { V(5, 8) }, manifestV1Enabled: false);
        var input = HandshakeTestHelpers.ClientBytes(0u, 0u, HandshakeTestHelpers.Version(5, 8));

        var (result, written, _) = await Run(options, input, async);

        Assert.True(result.Accepted);
        Assert.False(result.ShouldClose);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x05 }, written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_RangeSpanningZero_SelectsHighestSupportedNonZero(bool async)
    {
        // Range 0.1 down through 0.0 with the server supporting 0.1: the zero entry must be
        // skipped, and the valid entry selected.
        var options = new BoltHandshakeOptions(new[] { V(0, 1) }, manifestV1Enabled: false);
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Range(0, 1, 1));

        var (result, written, _) = await Run(options, input, async);

        Assert.True(result.Accepted);
        Assert.Equal(V(0, 1), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x00 }, written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_ZeroPlaceholdersAlongsideMarker_StillNegotiate(bool async)
    {
        var options = BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker, 0u, 0u, 0u),
            HandshakeTestHelpers.Selection(5, 8, 0));

        var (result, written, _) = await Run(options, input, async);

        Assert.True(result.Accepted);
        Assert.Equal(BoltHandshakeMode.ManifestV1, result.Mode);
        Assert.Equal(V(5, 8), result.SelectedVersion);
        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0), written);
    }

    // ---- 5. no-match response bytes and closure -----------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_NoMatch_WritesExactlyFourZeroBytesAndCloses(bool async)
    {
        var options = new BoltHandshakeOptions(new[] { V(5, 8) }, manifestV1Enabled: false);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 7)),
            new byte[] { 0xDE, 0xAD });

        var (result, written, stream) = await Run(options, input, async);

        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.Equal(0UL, result.NegotiatedCapabilities);
        Assert.Equal(20, input.Length - stream.RemainingInput); // exactly the preamble, no read-ahead
        Assert.Equal(new byte[] { 0xDE, 0xAD }, input[(input.Length - stream.RemainingInput)..]);
        Assert.False(stream.Disposed);
    }

    // ---- 6. successful negotiation of legitimate versions --------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_ValidVersion_StillNegotiates(bool async)
    {
        var options = new BoltHandshakeOptions(new[] { V(5, 7), V(5, 8) }, manifestV1Enabled: false);
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 7));

        var (result, written, _) = await Run(options, input, async);

        Assert.True(result.Accepted);
        Assert.Equal(V(5, 7), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x07, 0x05 }, written);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manifest_ValidVersion_StillNegotiates(bool async)
    {
        var options = BoltHandshakeOptions.Create(new[] { V(5, 7), V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0x09);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 7, 0x08));

        var (result, written, _) = await Run(options, input, async);

        Assert.True(result.Accepted);
        Assert.Equal(V(5, 7), result.SelectedVersion);
        Assert.Equal(0x08UL, result.NegotiatedCapabilities);
        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00010805u }, 0x09), written);
    }

    // ---- 7. repeated attempts and acceptance-flag consistency ----------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedLegacyNegotiations_ZeroAttemptsNeverLeakAcceptedState(bool async)
    {
        var options = new BoltHandshakeOptions(new[] { V(0, 0), V(5, 8) }, manifestV1Enabled: false);
        var negotiator = new BoltHandshakeNegotiator(options);

        for (var i = 0; i < 5; i++)
        {
            var zeroStream = new HandshakeDuplexStream(
                HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Range(0, 1, 1)));
            var zeroResult = async ? await negotiator.NegotiateAsync(zeroStream) : negotiator.Negotiate(zeroStream);
            Assert.False(zeroResult.Accepted);
            Assert.True(zeroResult.ShouldClose);
            Assert.Null(zeroResult.SelectedVersion);
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, zeroStream.WrittenBytes);

            var okStream = new HandshakeDuplexStream(
                HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8)));
            var okResult = async ? await negotiator.NegotiateAsync(okStream) : negotiator.Negotiate(okStream);
            Assert.True(okResult.Accepted);
            Assert.False(okResult.ShouldClose);
            Assert.Equal(V(5, 8), okResult.SelectedVersion);
            Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x05 }, okStream.WrittenBytes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedManifestNegotiations_ZeroSelectionAlwaysThrowsAndValidSelectionSucceeds(bool async)
    {
        var options = BoltHandshakeOptions.Create(new[] { V(5, 8) }, manifestV1Enabled: true, offeredCapabilities: 0);
        var negotiator = new BoltHandshakeNegotiator(options);

        for (var i = 0; i < 3; i++)
        {
            var zeroStream = new HandshakeDuplexStream(HandshakeTestHelpers.Concat(
                HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
                HandshakeTestHelpers.Selection(0, 0, 0)));
            BoltHandshakeResult? published = null;
            await Assert.ThrowsAsync<BoltHandshakeProtocolException>(async () =>
                published = async ? await negotiator.NegotiateAsync(zeroStream) : negotiator.Negotiate(zeroStream));
            Assert.Null(published);

            var okStream = new HandshakeDuplexStream(HandshakeTestHelpers.Concat(
                HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
                HandshakeTestHelpers.Selection(5, 8, 0)));
            var okResult = async ? await negotiator.NegotiateAsync(okStream) : negotiator.Negotiate(okStream);
            Assert.True(okResult.Accepted);
            Assert.False(okResult.ShouldClose);
            Assert.Equal(V(5, 8), okResult.SelectedVersion);
        }
    }
}
