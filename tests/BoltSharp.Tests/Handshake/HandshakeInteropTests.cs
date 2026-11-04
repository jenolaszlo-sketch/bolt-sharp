using BoltSharp.Framing;
using BoltSharp.Handshake;

using BoltSharp.Tests.Framing;

using Xunit;

namespace BoltSharp.Tests.Handshake;

/// <summary>
/// Cross-cutting handshake behaviour: arbitrary fragmentation, preservation of a pipelined first
/// message, synchronous/asynchronous equivalence, and stream ownership.
/// </summary>
public sealed class HandshakeInteropTests
{
    private static BoltHandshakeOptions Supports58(bool manifest = false) =>
        new(new[] { new BoltProtocolVersion(5, 8) }, manifest);

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
    public void Legacy_PipelinedFirstMessage_BytesRemainInStream()
    {
        var message = HandshakeTestHelpers.Pattern(64);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8)),
            FramingTestHelpers.Encode(message));

        var stream = new HandshakeDuplexStream(input);
        var result = new BoltHandshakeNegotiator(Supports58()).Negotiate(stream);

        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);
        Assert.Equal(FramingTestHelpers.Encode(message).Length, stream.RemainingInput);

        var reader = new BoltMessageReader(stream);
        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void Manifest_PipelinedFirstMessage_BytesRemainInStream()
    {
        var message = HandshakeTestHelpers.Pattern(100);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0),
            FramingTestHelpers.Encode(message));

        var stream = new HandshakeDuplexStream(input);
        var result = new BoltHandshakeNegotiator(Supports58(manifest: true)).Negotiate(stream);

        Assert.Equal(BoltHandshakeMode.ManifestV1, result.Mode);
        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);

        var reader = new BoltMessageReader(stream);
        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public async Task Manifest_PipelinedFirstMessage_PreservedAcrossFragmentation()
    {
        var message = HandshakeTestHelpers.Pattern(150);
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0x00),
            FramingTestHelpers.Encode(message));

        var stream = new HandshakeDuplexStream(input, maxReadSize: 1);
        var result = await new BoltHandshakeNegotiator(Supports58(manifest: true)).NegotiateAsync(stream);

        Assert.Equal(new BoltProtocolVersion(5, 8), result.SelectedVersion);

        var reader = new BoltMessageReader(stream);
        Assert.Equal(message, await reader.ReadMessageAsync());
        Assert.Null(await reader.ReadMessageAsync());
    }

    [Fact]
    public async Task SyncAndAsync_ProduceTheSameLegacyResultAndBytes()
    {
        var input = HandshakeTestHelpers.ClientBytes(
            HandshakeTestHelpers.Version(5, 7),
            HandshakeTestHelpers.Version(5, 8));

        var syncStream = new HandshakeDuplexStream(input);
        var syncResult = new BoltHandshakeNegotiator(Supports58()).Negotiate(syncStream);

        var asyncStream = new HandshakeDuplexStream(input);
        var asyncResult = await new BoltHandshakeNegotiator(Supports58()).NegotiateAsync(asyncStream);

        Assert.Equal(syncResult.Accepted, asyncResult.Accepted);
        Assert.Equal(syncResult.SelectedVersion, asyncResult.SelectedVersion);
        Assert.Equal(syncResult.Mode, asyncResult.Mode);
        Assert.Equal(syncStream.WrittenBytes, asyncStream.WrittenBytes);
    }

    [Fact]
    public async Task SyncAndAsync_ProduceTheSameManifestResultAndBytes()
    {
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.ClientBytes(BoltManifestV1.Marker),
            HandshakeTestHelpers.Selection(5, 8, 0x01));
        var options = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }, true, 0x03);

        var syncStream = new HandshakeDuplexStream(input);
        var syncResult = new BoltHandshakeNegotiator(options).Negotiate(syncStream);

        var asyncStream = new HandshakeDuplexStream(input);
        var asyncResult = await new BoltHandshakeNegotiator(options).NegotiateAsync(asyncStream);

        Assert.Equal(syncResult.Mode, asyncResult.Mode);
        Assert.Equal(syncResult.SelectedVersion, asyncResult.SelectedVersion);
        Assert.Equal(syncResult.NegotiatedCapabilities, asyncResult.NegotiatedCapabilities);
        Assert.Equal(syncStream.WrittenBytes, asyncStream.WrittenBytes);
    }

    [Fact]
    public async Task AsyncRejection_MatchesSyncRejection()
    {
        var input = HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 6));

        var syncStream = new HandshakeDuplexStream(input);
        var syncResult = new BoltHandshakeNegotiator(Supports58()).Negotiate(syncStream);

        var asyncStream = new HandshakeDuplexStream(input);
        var asyncResult = await new BoltHandshakeNegotiator(Supports58()).NegotiateAsync(asyncStream);

        Assert.False(syncResult.Accepted);
        Assert.False(asyncResult.Accepted);
        Assert.True(asyncResult.ShouldClose);
        Assert.Equal(syncStream.WrittenBytes, asyncStream.WrittenBytes);
    }

    [Fact]
    public void Negotiator_DoesNotDisposeTheStream()
    {
        var stream = new HandshakeDuplexStream(
            HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8)));

        var result = new BoltHandshakeNegotiator(Supports58()).Negotiate(stream);

        Assert.True(result.Accepted);
        Assert.False(stream.Disposed);
    }

    [Fact]
    public async Task NegotiatorAsync_DoesNotDisposeTheStream()
    {
        var stream = new HandshakeDuplexStream(
            HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 8)));

        await new BoltHandshakeNegotiator(Supports58()).NegotiateAsync(stream);

        Assert.False(stream.Disposed);
    }

    [Fact]
    public void RejectedHandshake_RequestClosureAndLeavesStreamOpen()
    {
        var stream = new HandshakeDuplexStream(
            HandshakeTestHelpers.ClientBytes(HandshakeTestHelpers.Version(5, 6)));

        var result = new BoltHandshakeNegotiator(Supports58()).Negotiate(stream);

        Assert.True(result.ShouldClose);
        Assert.False(stream.Disposed);
    }

    [Fact]
    public void Negotiate_RequiresFourProposals_TruncatedAfterThree()
    {
        var input = HandshakeTestHelpers.Concat(
            HandshakeTestHelpers.Magic,
            new byte[] { 0x00, 0x00, 0x08, 0x05, 0x00, 0x00, 0x07, 0x05, 0x00, 0x00, 0x06, 0x05 });

        Assert.Throws<BoltHandshakeTruncatedException>(() => Negotiate(Supports58(), input));
    }
}
