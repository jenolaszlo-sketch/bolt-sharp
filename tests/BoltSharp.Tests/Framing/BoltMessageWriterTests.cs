using System.Buffers;

using BoltSharp.Framing;

using Xunit;

namespace BoltSharp.Tests.Framing;

/// <summary>
/// Golden-byte tests for the chunked-message writer. Expected bytes follow the Bolt protocol
/// message specification
/// (https://neo4j.com/docs/bolt/current/bolt/message/#chunking), independently of the codec
/// under test.
/// </summary>
public sealed class BoltMessageWriterTests
{
    private static readonly byte[] SixteenBytes =
    {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
    };

    [Fact]
    public void WriteMessage_OneByte_EmitsSingleChunkAndTerminator()
    {
        var expected = new byte[] { 0x00, 0x01, 0xAB, 0x00, 0x00 };

        Assert.Equal(expected, FramingTestHelpers.Encode(new byte[] { 0xAB }));
    }

    [Fact]
    public void WriteMessage_SpecificationSixteenByteExample()
    {
        // Bolt spec: "Example of a message in one chunk".
        var expected = FramingTestHelpers.Concat(
            new byte[] { 0x00, 0x10 },
            SixteenBytes,
            new byte[] { 0x00, 0x00 });

        Assert.Equal(expected, FramingTestHelpers.Encode(SixteenBytes));
    }

    [Fact]
    public void WriteMessage_Length256_WritesBigEndianHeader()
    {
        var message = new byte[256];
        Array.Fill(message, (byte)0x5A);

        var wire = FramingTestHelpers.Encode(message);

        Assert.Equal(256 + 4, wire.Length);
        Assert.Equal(0x01, wire[0]);
        Assert.Equal(0x00, wire[1]);
        Assert.Equal(message, wire[2..^2]);
        Assert.Equal(0x00, wire[^2]);
        Assert.Equal(0x00, wire[^1]);
    }

    [Fact]
    public void WriteMessage_Length65535_EmitsSingleMaximumChunk()
    {
        var message = FramingTestHelpers.CreatePattern(65_535);

        var wire = FramingTestHelpers.Encode(message);

        Assert.Equal(2 + 65_535 + 2, wire.Length);
        Assert.Equal(0xFF, wire[0]);
        Assert.Equal(0xFF, wire[1]);
        Assert.Equal(message, wire[2..(2 + 65_535)]);
        Assert.Equal(0x00, wire[^2]);
        Assert.Equal(0x00, wire[^1]);
    }

    [Fact]
    public void WriteMessage_Length65536_SplitsIntoTwoChunks()
    {
        var message = FramingTestHelpers.CreatePattern(65_536);

        var wire = FramingTestHelpers.Encode(message);

        Assert.Equal(2 + 65_535 + 2 + 1 + 2, wire.Length);
        Assert.Equal(0xFF, wire[0]);
        Assert.Equal(0xFF, wire[1]);
        Assert.Equal(message[..65_535], wire[2..(2 + 65_535)]);

        var secondHeader = 2 + 65_535;
        Assert.Equal(0x00, wire[secondHeader]);
        Assert.Equal(0x01, wire[secondHeader + 1]);
        Assert.Equal(message[65_535], wire[secondHeader + 2]);
        Assert.Equal(0x00, wire[^2]);
        Assert.Equal(0x00, wire[^1]);
    }

    [Fact]
    public void WriteMessage_Empty_ThrowsAndWritesNothing()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);

        Assert.Throws<ArgumentException>(() => writer.WriteMessage(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void WriteMessage_TwoMessages_AreConcatenated()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);

        writer.WriteMessage(new byte[] { 0x01 });
        writer.WriteMessage(new byte[] { 0x02, 0x03 });

        var expected = new byte[]
        {
            0x00, 0x01, 0x01, 0x00, 0x00,
            0x00, 0x02, 0x02, 0x03, 0x00, 0x00,
        };

        Assert.Equal(expected, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteNoop_WritesZeroLengthChunk()
    {
        var buffer = new ArrayBufferWriter<byte>();

        new BoltMessageWriter(buffer).WriteNoop();

        Assert.Equal(new byte[] { 0x00, 0x00 }, buffer.WrittenSpan.ToArray());
    }
}
