using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Legacy four-proposal handshake: specification examples, preference ordering, version ranges,
/// explicit-support selection, zero-version rejection, and manifest fallback.
/// </summary>
public sealed class LegacyHandshakeTests
{
    private static BoltHandshakeOptions Supports(params BoltProtocolVersion[] versions) =>
        new(versions, manifestV1Enabled: false);

    private static (BoltHandshakeResult Result, byte[] Written) Negotiate(
        BoltHandshakeOptions options,
        byte[] input,
        int maxReadSize = int.MaxValue)
    {
        var stream = new HandshakeDuplexStream(input, maxReadSize);
        var result = new BoltHandshakeNegotiator(options).Negotiate(stream);
        return (result, stream.WrittenBytes);
    }

    [Fact]
    public void SpecExample_ClientKnowsVersion1_ServerSelects1()
    {
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(1, 0));
        var (result, written) = Negotiate(Supports(new BoltProtocolVersion(1, 0)), input);

        Assert.True(result.Accepted);
        Assert.Equal(BoltHandshakeMode.Legacy, result.Mode);
        Assert.Equal(new BoltProtocolVersion(1, 0), result.SelectedVersion);
        Assert.False(result.ShouldClose);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01 }, written);
    }

    [Fact]
    public void SpecExample_ClientKnowsVersions1And2_ServerSelectsPreferred2()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(2, 0),
            HandshakeTestHelpers.Version(1, 0));
        var (result, written) = Negotiate(
            Supports(new BoltProtocolVersion(1, 0), new BoltProtocolVersion(2, 0)),
            input);

        Assert.Equal(new BoltProtocolVersion(2, 0), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x02 }, written);
    }

    [Fact]
    public void SpecExample_ClientKnowsVersions1To3_ServerSelects2()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(3, 0),
            HandshakeTestHelpers.Version(2, 0),
            HandshakeTestHelpers.Version(1, 0));
        var (result, written) = Negotiate(
            Supports(new BoltProtocolVersion(1, 0), new BoltProtocolVersion(2, 0)),
            input);

        Assert.Equal(new BoltProtocolVersion(2, 0), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x02 }, written);
    }

    [Fact]
    public void SpecExample_UnsupportedVersion_ReturnsZeroAndCloses()
    {
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(3, 0));
        var (result, written) = Negotiate(Supports(), input);

        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Null(result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
    }

    [Fact]
    public void SpecExample_RangeProposal_SelectsExplicitVersion()
    {
        // Client aware of 3, 4.0, 4.1, 4.2 and 4.3; server supports 4.1.
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Range(4, 3, 3),
            HandshakeTestHelpers.Version(4, 1),
            HandshakeTestHelpers.Version(4, 0),
            HandshakeTestHelpers.Version(3, 0));
        var (result, written) = Negotiate(Supports(new BoltProtocolVersion(4, 1)), input);

        Assert.Equal(new BoltProtocolVersion(4, 1), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x04 }, written);
    }

    [Fact]
    public void RangeProposal_SelectsHighestSupportedWithinRange()
    {
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Range(4, 3, 3));
        var (result, _) = Negotiate(
            Supports(new BoltProtocolVersion(4, 0), new BoltProtocolVersion(4, 2)),
            input);

        Assert.Equal(new BoltProtocolVersion(4, 2), result.SelectedVersion);
    }

    [Fact]
    public void RangeProposal_NotSpanningMajor_IsRejectedAsFormat()
    {
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Range(4, 1, 2));
        Assert.Throws<BoltHandshakeFormatException>(() => Negotiate(Supports(new BoltProtocolVersion(4, 1)), input));
    }

    [Fact]
    public void PreferenceOrdering_HigherPreferenceWins()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(5, 8),
            HandshakeTestHelpers.Version(5, 7));
        var (result, _) = Negotiate(
            Supports(new BoltProtocolVersion(5, 7), new BoltProtocolVersion(5, 8)),
            input);

        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
    }

    [Fact]
    public void ZeroPlaceholders_AreSkipped()
    {
        var input = HandshakeTestHelpers.ClientBytes(0u, 0u, HandshakeTestHelpers.Version(5, 8));
        var (result, _) = Negotiate(Supports(new BoltProtocolVersion(5, 8)), input);

        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
    }

    [Fact]
    public void UnsupportedOfferedVersion_IsNotSelected()
    {
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8));
        var (result, written) = Negotiate(Supports(new BoltProtocolVersion(5, 7)), input);

        Assert.False(result.Accepted);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
    }

    [Fact]
    public void DefaultOptions_RejectEveryHandshake()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(5, 8),
            BoltManifestV1.Marker);
        var (result, written) = Negotiate(BoltHandshakeOptions.Default, input);

        Assert.False(result.Accepted);
        Assert.True(result.ShouldClose);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
    }

    [Fact]
    public void ManifestMarker_WithManifestDisabled_FallsBackToLegacy()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            BoltManifestV1.Marker,
            HandshakeTestHelpers.Version(5, 8));
        var (result, written) = Negotiate(Supports(new BoltProtocolVersion(5, 8)), input);

        Assert.True(result.Accepted);
        Assert.Equal(BoltHandshakeMode.Legacy, result.Mode);
        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x08, 0x05 }, written);
    }

    [Fact]
    public void ManifestMarker_WithManifestDisabled_AndNoOtherMatch_Rejects()
    {
        var input = HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker);
        var (result, written) = Negotiate(Supports(new BoltProtocolVersion(5, 8)), input);

        Assert.False(result.Accepted);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, written);
    }

    [Fact]
    public void MalformedProposalBeforeAMatch_ThrowsFormat()
    {
        var input = HandshakeTestHelpers.ClientBytes(0x01020304u, HandshakeTestHelpers.Version(5, 8));
        Assert.Throws<BoltHandshakeFormatException>(
            () => Negotiate(Supports(new BoltProtocolVersion(5, 8)), input));
    }

    [Fact]
    public void MalformedProposalAfterAMatch_IsNotReached()
    {
        // Preference ordering short-circuits on the first matching proposal.
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8), 0x01020304u);
        var (result, _) = Negotiate(Supports(new BoltProtocolVersion(5, 8)), input);

        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
    }

    [Fact]
    public void FragmentedInput_IsReconstructed()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(5, 7),
            HandshakeTestHelpers.Version(5, 8),
            HandshakeTestHelpers.Version(5, 6));
        var (result, written) = Negotiate(
            Supports(new BoltProtocolVersion(5, 6), new BoltProtocolVersion(5, 7), new BoltProtocolVersion(5, 8)),
            input,
            maxReadSize: 1);

        Assert.Equal(new BoltProtocolVersion(5, 7), result.SelectedVersion);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x07, 0x05 }, written);
    }
}
