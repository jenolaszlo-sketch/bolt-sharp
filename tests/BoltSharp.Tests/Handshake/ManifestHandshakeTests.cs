using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Manifest v1 negotiation: the specification example, offer generation, selection validation,
/// capability subsets, and interaction with legacy preference ordering.
/// </summary>
public sealed class ManifestHandshakeTests
{
    private static (BoltHandshakeResult Result, byte[] Written) Negotiate(
        BoltHandshakeOptions options,
        byte[] input,
        int maxReadSize = int.MaxValue)
    {
        var stream = new HandshakeDuplexStream(input, maxReadSize);
        var result = new BoltHandshakeNegotiator(options).Negotiate(stream);
        return (result, stream.WrittenBytes);
    }

    private static BoltHandshakeOptions SpecExampleOptions() =>
        BoltHandshakeOptions.Create(
            new[]
            {
                new BoltProtocolVersion(5, 6),
                new BoltProtocolVersion(5, 7),
                new BoltProtocolVersion(5, 8),
                new BoltProtocolVersion(4, 0),
                new BoltProtocolVersion(4, 1),
                new BoltProtocolVersion(4, 2),
                new BoltProtocolVersion(4, 3),
                new BoltProtocolVersion(4, 4),
            },
            manifestV1Enabled: true,
            offeredCapabilities: 0x09);

    [Fact]
    public void SpecExample_ManifestOfferAndSelection()
    {
        // C: magic, 00 00 01 FF, 00 00 04 04, 00 00 00 03, 00 00 00 02
        // C: 00 00 07 05 (5.7), 08 (caps 0000 1000)
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(
                BoltManifestV1.Marker,
                HandshakeTestHelpers.Version(4, 4),
                HandshakeTestHelpers.Version(3, 0),
                HandshakeTestHelpers.Version(2, 0)),
            HandshakeTestHelpers.Selection(5, 7, 0x08));

        var (result, written) = Negotiate(SpecExampleOptions(), input);

        // S: 00 00 01 FF, 02, 00 02 08 05, 00 04 04 04, 09
        var expected = HandshakeTestHelpers.Offer(new uint[] { 0x00020805u, 0x00040404u }, 0x09);
        Assert.Equal(expected, written);

        Assert.True(result.Accepted);
        Assert.Equal(BoltHandshakeMode.ManifestV1, result.Mode);
        Assert.Equal(new BoltProtocolVersion(5, 7), result.SelectedVersion);
        Assert.Equal(0x08UL, result.NegotiatedCapabilities);
        Assert.False(result.ShouldClose);
    }

    [Fact]
    public void SingleSupportedVersion_OffersSingleRange()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0));

        var (result, written) = Negotiate(options, input);

        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0), written);
        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
    }

    [Fact]
    public void CapabilitySubset_IsAccepted()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0x0D);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0x09));

        var (result, _) = Negotiate(options, input);

        Assert.Equal(0x09UL, result.NegotiatedCapabilities);
    }

    [Fact]
    public void CapabilitySuperset_ThrowsProtocol()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0x09);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0x0F));

        Assert.Throws<BoltHandshakeProtocolException>(() => Negotiate(options, input));
    }

    [Fact]
    public void UnofferedVersion_ThrowsProtocol()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 7, 0));

        Assert.Throws<BoltHandshakeProtocolException>(() => Negotiate(options, input));
    }

    [Fact]
    public void SelectionWithRangeEncoding_ThrowsFormat()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            new byte[] { 0x00, 0x01, 0x07, 0x05, 0x00 });

        Assert.Throws<BoltHandshakeFormatException>(() => Negotiate(options, input));
    }

    [Fact]
    public void ManifestEnabledButNoVersions_FallsBackAndRejects()
    {
        var options = new BoltHandshakeOptions(Array.Empty<BoltProtocolVersion>(), manifestV1Enabled: true);
        var input = HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker);

        var (result, written) = Negotiate(options, input);

        Assert.False(result.Accepted);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
    }

    [Fact]
    public void MatchingLegacyProposalBeforeManifestMarker_WinsByPreference()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0);
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(5, 8),
            BoltManifestV1.Marker);

        var (result, written) = Negotiate(options, input);

        Assert.Equal(BoltHandshakeMode.Legacy, result.Mode);
        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x05 }, written);
    }

    [Fact]
    public void ManifestMarkerFirst_SelectsManifest()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker, HandshakeTestHelpers.Version(5, 8)),
            HandshakeTestHelpers.Selection(5, 8, 0));

        var (result, _) = Negotiate(options, input);

        Assert.Equal(BoltHandshakeMode.ManifestV1, result.Mode);
    }

    [Fact]
    public void OfferRanges_ExposeConfiguredVersions()
    {
        var negotiator = new BoltHandshakeNegotiator(SpecExampleOptions());

        Assert.Equal(
            new[] { new BoltVersionRange(5, 8, 2), new BoltVersionRange(4, 4, 4) },
            negotiator.OfferRanges);
    }

    [Fact]
    public void ManifestInsideRange_IsAccepted()
    {
        // Offer 5.8-5.6; the client picks 5.6, which is covered by the range.
        var options = BoltHandshakeOptions.Create(
            new[]
            {
                new BoltProtocolVersion(5, 6),
                new BoltProtocolVersion(5, 7),
                new BoltProtocolVersion(5, 8),
            },
            true,
            0);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 6, 0));

        var (result, written) = Negotiate(options, input);

        Assert.Equal(new BoltProtocolVersion(5, 6), result.SelectedVersion);
        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00020805u }, 0), written);
    }

    [Fact]
    public void MultiByteCapabilities_SplitAcrossReads_AreDecoded()
    {
        // Offered capabilities 0xFF encodes as FF 01 and selected 0x80 encodes as 80 01; a
        // one-byte read size splits both VarInts across reads.
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0xFF);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0x80));

        var (result, written) = Negotiate(options, input, maxReadSize: 1);

        Assert.Equal(0x80UL, result.NegotiatedCapabilities);
        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0xFF), written);
    }

    [Fact]
    public void FragmentInput_ManifestNegotiationStillWorks()
    {
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0));

        var (result, written) = Negotiate(
            BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0),
            input,
            maxReadSize: 1);

        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
        Assert.Equal(HandshakeTestHelpers.Offer(new uint[] { 0x00000805u }, 0), written);
    }
}
