using BoltSharp.Handshake;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Deterministic malformed, truncated and over-budget handshake handling at the negotiator
/// level.
/// </summary>
public sealed class HandshakeErrorTests
{
    private static BoltHandshakeOptions Supports58() =>
        new(new[] { new BoltProtocolVersion(5, 8) }, manifestV1Enabled: false);

    private static BoltHandshakeResult Negotiate(BoltHandshakeOptions options, byte[] input, BoltHandshakeLimits? limits = null)
    {
        var stream = new HandshakeDuplexStream(input);
        return new BoltHandshakeNegotiator(options, limits).Negotiate(stream);
    }

    [Fact]
    public void WrongMagic_ThrowsFormat()
    {
        var input = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        Assert.Throws<BoltHandshakeFormatException>(() => Negotiate(Supports58(), input));
    }

    [Fact]
    public void TruncatedMagic_ThrowsTruncated()
    {
        Assert.Throws<BoltHandshakeTruncatedException>(
            () => Negotiate(Supports58(), new byte[] { 0x60, 0x60, 0xB0 }));
    }

    [Fact]
    public void EmptyStream_ThrowsTruncated()
    {
        Assert.Throws<BoltHandshakeTruncatedException>(() => Negotiate(Supports58(), Array.Empty<byte>()));
    }

    [Fact]
    public void TruncatedProposalList_ThrowsTruncated()
    {
        var bytes = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.Magic,
            new byte[] { 0x00, 0x00, 0x08, 0x05, 0x00, 0x00 });

        Assert.Throws<BoltHandshakeTruncatedException>(() => Negotiate(Supports58(), bytes));
    }

    [Fact]
    public void TruncatedManifestSelection_ThrowsTruncated()
    {
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0);
        var bytes = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            new byte[] { 0x00, 0x00 }); // half a selection

        Assert.Throws<BoltHandshakeTruncatedException>(() => Negotiate(options, bytes));
    }

    [Fact]
    public void HandshakeExceedingByteBudget_ThrowsFormat()
    {
        var limits = new BoltHandshakeLimits(maxHandshakeBytes: 8);
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8));

        Assert.Throws<BoltHandshakeFormatException>(() => Negotiate(Supports58(), input, limits));
    }

    [Fact]
    public void MalformedMagic_DoesNotWriteAResponse()
    {
        var stream = new HandshakeDuplexStream(new byte[] { 0x01, 0x02, 0x03, 0x04 });

        Assert.Throws<BoltHandshakeFormatException>(
            () => new BoltHandshakeNegotiator(Supports58()).Negotiate(stream));
        Assert.Empty(stream.WrittenBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limits_RejectNonPositiveValues(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltHandshakeLimits(maxManifestVersions: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltHandshakeLimits(maxHandshakeBytes: value));
    }

    [Fact]
    public void Negotiator_RequiresOptions()
    {
        Assert.Throws<ArgumentNullException>(() => new BoltHandshakeNegotiator(null!));
    }

    [Fact]
    public void Negotiate_RequiresStream()
    {
        var negotiator = new BoltHandshakeNegotiator(Supports58());
        Assert.Throws<ArgumentNullException>(() => negotiator.Negotiate(null!));
    }
}
