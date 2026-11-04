using System.Buffers;

using BoltSharp.Framing;

using Xunit;

namespace BoltSharp.Tests.Framing;

/// <summary>
/// Message reconstruction tests for the chunked-message reader: golden vectors, chunk
/// boundaries, pipelining, NOOP keep-alives, arbitrary fragmentation and round-trips.
/// </summary>
public sealed class BoltMessageReaderTests
{
    private static readonly byte[] SixteenBytes =
    {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
    };

    [Fact]
    public void ReadMessage_SpecificationSingleChunkExample()
    {
        var wire = FramingTestHelpers.Concat(new byte[] { 0x00, 0x10 }, SixteenBytes, new byte[] { 0x00, 0x00 });

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(SixteenBytes, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_SpecificationTwoChunkExample()
    {
        // A 20-byte message split as 16 + 4 bytes (Bolt spec "Example of a message split in
        // two chunks").
        var expected = FramingTestHelpers.Concat(SixteenBytes, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var wire = FramingTestHelpers.Concat(
            new byte[] { 0x00, 0x10 },
            SixteenBytes,
            new byte[] { 0x00, 0x04, 0x01, 0x02, 0x03, 0x04, 0x00, 0x00 });

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(expected, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_TwoMessages_Pipelined()
    {
        var first = new byte[] { 0x01 };
        var second = new byte[] { 0x02, 0x03 };
        var wire = FramingTestHelpers.Concat(
            FramingTestHelpers.Encode(first),
            FramingTestHelpers.Encode(second));

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(first, reader.ReadMessage());
        Assert.Equal(second, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_NoopBetweenMessages_IsSkipped()
    {
        var first = new byte[] { 0xAA };
        var second = new byte[] { 0xBB, 0xCC };
        var wire = FramingTestHelpers.Concat(
            FramingTestHelpers.Encode(first),
            new byte[] { 0x00, 0x00 },
            FramingTestHelpers.Encode(second));

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(first, reader.ReadMessage());
        Assert.Equal(second, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_LeadingNoops_AreSkipped()
    {
        var message = new byte[] { 0x42 };
        var wire = FramingTestHelpers.Concat(
            new byte[] { 0x00, 0x00, 0x00, 0x00 },
            FramingTestHelpers.Encode(message));

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_EmptyStream_ReturnsNull()
    {
        var reader = new BoltMessageReader(new MemoryStream(Array.Empty<byte>()));

        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_OnlyNoop_ReturnsNull()
    {
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x00, 0x00 }));

        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_TrailingNoop_ReturnsMessageThenNull()
    {
        var message = new byte[] { 0x7F };
        var wire = FramingTestHelpers.Concat(
            FramingTestHelpers.Encode(message),
            new byte[] { 0x00, 0x00 });

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_ZeroLengthChunkAfterPayload_TerminatesMessage()
    {
        // A zero-length chunk does not extend a message that already has payload bytes.
        var wire = FramingTestHelpers.Concat(
            new byte[] { 0x00, 0x01, 0xAA, 0x00, 0x00 },
            new byte[] { 0x00, 0x01, 0xBB, 0x00, 0x00 });

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(new byte[] { 0xAA }, reader.ReadMessage());
        Assert.Equal(new byte[] { 0xBB }, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65_535)]
    [InlineData(65_536)]
    [InlineData(70_000)]
    public void ReadMessage_RoundTripsWriterOutput(int length)
    {
        var message = FramingTestHelpers.CreatePattern(length);
        var wire = FramingTestHelpers.Encode(message);

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void ReadMessage_ArbitraryFragmentation_ReconstructsMessage(int maxReadSize)
    {
        var message = FramingTestHelpers.CreatePattern(1000);
        var wire = FramingTestHelpers.Encode(message);

        using var stream = new FragmentingStream(wire, maxReadSize);
        var reader = new BoltMessageReader(stream);

        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ReadMessage_PseudoRandomFragmentation_MultipleSeeds(int seed)
    {
        var message = FramingTestHelpers.CreatePattern(5000);
        var wire = FramingTestHelpers.Encode(message);

        using var stream = new FragmentingStream(wire, new Random(seed), maxReadSize: 97);
        var reader = new BoltMessageReader(stream);

        Assert.Equal(message, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void ReadMessage_Position_TracksConsumedBytes()
    {
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03 };
        var wire = FramingTestHelpers.Concat(
            FramingTestHelpers.Encode(first),
            FramingTestHelpers.Encode(second));

        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Equal(0, reader.Position);
        reader.ReadMessage();
        Assert.Equal(6, reader.Position); // 2 header + 2 payload + 2 terminator
        reader.ReadMessage();
        Assert.Equal(11, reader.Position);
    }

    [Fact]
    public void TryReadMessage_WritesToDestination()
    {
        var message = FramingTestHelpers.CreatePattern(100);
        var wire = FramingTestHelpers.Encode(message);

        var destination = new ArrayBufferWriter<byte>();
        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.True(reader.TryReadMessage(destination));
        Assert.Equal(message, destination.WrittenSpan.ToArray());
        Assert.False(reader.TryReadMessage(destination));
        Assert.Equal(message.Length, destination.WrittenCount); // nothing appended at EOF
    }

    [Fact]
    public async Task ReadMessageAsync_ReconstructsFragmentedMessage()
    {
        var message = FramingTestHelpers.CreatePattern(300);
        var wire = FramingTestHelpers.Encode(message);

        using var stream = new FragmentingStream(wire, maxReadSize: 7);
        var reader = new BoltMessageReader(stream);

        Assert.Equal(message, await reader.ReadMessageAsync());
        Assert.Null(await reader.ReadMessageAsync());
    }
}
