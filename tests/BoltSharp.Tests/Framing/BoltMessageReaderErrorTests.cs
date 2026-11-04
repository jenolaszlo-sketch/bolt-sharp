using BoltSharp.Framing;

using Xunit;

namespace BoltSharp.Tests.Framing;

/// <summary>
/// Malformed-input behaviour for the chunked-message reader: truncation raises
/// <see cref="BoltFramingTruncatedException"/>, and an over-limit declared message raises
/// <see cref="BoltFramingFormatException"/> before any large allocation.
/// </summary>
public sealed class BoltMessageReaderErrorTests
{
    [Fact]
    public void TruncatedHeader_SingleByte_Throws()
    {
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00 }));

        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
    }

    [Fact]
    public void TruncatedHeader_AfterNoop_Throws()
    {
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00, 0x00, 0x00 }));

        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
    }

    [Fact]
    public void TruncatedPayload_Throws()
    {
        // Declares a 5-byte payload but only 2 bytes follow.
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00, 0x05, 0x01, 0x02 }));

        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
    }

    [Fact]
    public void MissingTerminator_Throws()
    {
        // A complete chunk payload but no 00 00 terminator before the end of stream.
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00, 0x01, 0xAB }));

        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
    }

    [Fact]
    public void OversizedSingleChunk_ThrowsFormatBeforeReadingPayload()
    {
        // The stream contains only the header. A correct implementation rejects the
        // over-limit declared size without attempting to read (or allocate) 65,535 bytes;
        // an implementation that read first would raise Truncated instead of Format.
        var limits = new BoltFramingLimits(maxMessageSize: 4);
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0xFF, 0xFF }), limits);

        Assert.Throws<BoltFramingFormatException>(() => reader.ReadMessage());
    }

    [Fact]
    public void OversizedAcrossChunks_ThrowsFormat()
    {
        var limits = new BoltFramingLimits(maxMessageSize: 4);
        var wire = new byte[]
        {
            0x00, 0x03, 0x01, 0x02, 0x03,
            0x00, 0x03, 0x04, 0x05, 0x06,
            0x00, 0x00,
        };
        var reader = new BoltMessageReader(new MemoryStream(wire), limits);

        Assert.Throws<BoltFramingFormatException>(() => reader.ReadMessage());
    }

    [Fact]
    public void MessageExactlyAtMaxSize_Succeeds()
    {
        var message = FramingTestHelpers.CreatePattern(5);
        var wire = FramingTestHelpers.Encode(message);
        var reader = new BoltMessageReader(new MemoryStream(wire), new BoltFramingLimits(maxMessageSize: 5));

        Assert.Equal(message, reader.ReadMessage());
    }

    [Fact]
    public void MessageOneOverMaxSize_Throws()
    {
        var message = FramingTestHelpers.CreatePattern(6);
        var wire = FramingTestHelpers.Encode(message);
        var reader = new BoltMessageReader(new MemoryStream(wire), new BoltFramingLimits(maxMessageSize: 5));

        Assert.Throws<BoltFramingFormatException>(() => reader.ReadMessage());
    }

    [Fact]
    public void Truncation_PositionReflectsBytesConsumed()
    {
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00, 0x05, 0x01, 0x02 }));

        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
        Assert.Equal(4, reader.Position); // 2-byte header + 2 payload bytes read before EOF
    }

    [Fact]
    public void NonPositiveMaxMessageSize_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltFramingLimits(maxMessageSize: 0));
    }

    [Fact]
    public async Task TruncatedPayload_Async_Throws()
    {
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00, 0x05, 0x01, 0x02 }));

        await Assert.ThrowsAsync<BoltFramingTruncatedException>(async () => await reader.ReadMessageAsync());
    }
}
