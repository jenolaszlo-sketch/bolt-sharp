using System.Buffers;
using System.Buffers.Binary;

using BoltSharp.Framing;

using BoltSharp.Tests.Framing;

using Xunit;

namespace BoltSharp.Tests.Qualification;

/// <summary>
/// Independent B2-A qualification probes for the chunked-message framing codec. Fixtures are
/// built by hand (not with the production <c>BoltMessageWriter</c>) so that the writer and the
/// reader are falsified independently. Probes target chunk boundaries, message boundaries,
/// NOOP placement, pipelining, fragmentation, truncation and allocation safety.
/// </summary>
public sealed class B2Q1FramingProbes
{
    // ---- Independent reference implementations ------------------------------------------

    /// <summary>Chunks a message by hand using only the wire format, not the production writer.</summary>
    private static byte[] ManualEncode(ReadOnlySpan<byte> message)
    {
        var output = new List<byte>(message.Length + 8);
        var offset = 0;
        while (offset < message.Length)
        {
            var length = Math.Min(65_535, message.Length - offset);
            output.Add((byte)(length >> 8));
            output.Add((byte)(length & 0xFF));
            for (var i = 0; i < length; i++)
            {
                output.Add(message[offset + i]);
            }

            offset += length;
        }

        output.Add(0x00);
        output.Add(0x00);
        return output.ToArray();
    }

    private static byte[] ReadOne(byte[] wire, BoltFramingLimits? limits = null, int maxReadSize = int.MaxValue)
    {
        using var stream = new FragmentingStream(wire, maxReadSize);
        var reader = new BoltMessageReader(stream, limits ?? BoltFramingLimits.Default);
        var message = reader.ReadMessage();
        Assert.NotNull(message);
        Assert.Null(reader.ReadMessage());
        return message!;
    }

    private static List<byte[]> ReadAll(byte[] wire, BoltFramingLimits? limits = null, int maxReadSize = int.MaxValue)
    {
        using var stream = new FragmentingStream(wire, maxReadSize);
        var reader = new BoltMessageReader(stream, limits ?? BoltFramingLimits.Default);
        var messages = new List<byte[]>();
        byte[]? message;
        while ((message = reader.ReadMessage()) is not null)
        {
            messages.Add(message);
        }

        return messages;
    }

    // ---- Chunk boundaries ----------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(65_534)]
    [InlineData(65_535)]
    public void B2Q1_ChunkHeader_IsBigEndian_ForSingleChunk(int length)
    {
        var message = FramingTestHelpers.CreatePattern(length);
        var expected = ManualEncode(message);

        // The very first two bytes are the length, big-endian, computed independently.
        Assert.Equal((byte)(length >> 8), expected[0]);
        Assert.Equal((byte)(length & 0xFF), expected[1]);

        // Production writer must match the hand-built reference exactly.
        Assert.Equal(expected, FramingTestHelpers.Encode(message));
        // And the reader must reproduce the message from the hand-built reference.
        Assert.Equal(message, ReadOne(expected));
    }

    [Fact]
    public void B2Q1_MaxChunkPayload_NeverExceeded_ForLargeMessage()
    {
        var message = FramingTestHelpers.CreatePattern(200_000);
        var wire = FramingTestHelpers.Encode(message);

        // 200,000 = 65,535 + 65,535 + 65,535 + 3,395
        var offset = 0;
        var expectedChunks = new[] { 65_535, 65_535, 65_535, 3_395 };
        foreach (var chunk in expectedChunks)
        {
            var declared = BinaryPrimitives.ReadUInt16BigEndian(wire.AsSpan(offset, 2));
            Assert.Equal(chunk, declared);
            Assert.True(declared <= 65_535);
            offset += 2 + chunk;
        }

        Assert.Equal(0x00, wire[offset]);
        Assert.Equal(0x00, wire[offset + 1]);
        Assert.Equal(offset + 2, wire.Length);
        Assert.Equal(message, ReadOne(wire));
    }

    [Theory]
    [InlineData(131_069)]
    [InlineData(131_070)] // exactly two maximum chunks
    [InlineData(131_071)]
    [InlineData(196_605)] // exactly three maximum chunks
    public void B2Q1_MultipleMaximumChunks_RoundTrip(int length)
    {
        var message = FramingTestHelpers.CreatePattern(length);
        var expected = ManualEncode(message);
        Assert.Equal(expected, FramingTestHelpers.Encode(message));
        Assert.Equal(message, ReadOne(expected));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(13)]
    public void B2Q1_HeadersSplitAcrossReads_RoundTrip(int maxReadSize)
    {
        // A multi-chunk message guarantees headers appear at several offsets; small read
        // sizes split the 2-byte headers themselves.
        var message = FramingTestHelpers.CreatePattern(70_000);
        var wire = ManualEncode(message);

        Assert.Equal(message, ReadOne(wire, maxReadSize: maxReadSize));
    }

    // ---- Message boundaries and NOOPs ----------------------------------------------------

    [Fact]
    public void B2Q1_ConsecutiveMessages_NoDelimiter_AreSeparated()
    {
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03 };
        var wire = FramingTestHelpers.Concat(ManualEncode(first), ManualEncode(second));

        var messages = ReadAll(wire);

        Assert.Equal(2, messages.Count);
        Assert.Equal(first, messages[0]);
        Assert.Equal(second, messages[1]);
    }

    [Fact]
    public void B2Q1_RepeatedTerminators_AfterMessage_YieldNoExtraMessages()
    {
        var message = new byte[] { 0xAB };
        var wire = FramingTestHelpers.Concat(ManualEncode(message), new byte[] { 0x00, 0x00, 0x00, 0x00 });

        var messages = ReadAll(wire);

        Assert.Single(messages);
        Assert.Equal(message, messages[0]);
    }

    [Fact]
    public void B2Q1_LeadingNoopFlood_IsSkipped()
    {
        var noops = new byte[1000];
        var message = new byte[] { 0x42 };
        var wire = FramingTestHelpers.Concat(noops, ManualEncode(message));

        var messages = ReadAll(wire);

        Assert.Single(messages);
        Assert.Equal(message, messages[0]);
    }

    [Fact]
    public void B2Q1_NoopOnlyStream_ReturnsNoMessages()
    {
        var wire = new byte[200]; // 100 NOOP chunks

        Assert.Empty(ReadAll(wire));
    }

    [Fact]
    public void B2Q1_ZeroLengthChunkAfterPayloadWithinSplitMessage_EndsMessage()
    {
        // chunk("AB") then terminator, then chunk("CD") then terminator: two messages, even
        // though a reader might have merged them if it treated 00 00 as a NOOP.
        var wire = new byte[]
        {
            0x00, 0x02, 0x41, 0x42, 0x00, 0x00,
            0x00, 0x02, 0x43, 0x44, 0x00, 0x00,
        };

        var messages = ReadAll(wire);

        Assert.Equal(2, messages.Count);
        Assert.Equal(new byte[] { 0x41, 0x42 }, messages[0]);
        Assert.Equal(new byte[] { 0x43, 0x44 }, messages[1]);
    }

    [Fact]
    public void B2Q1_NoopInterleavedBetweenPipelinedMessages_IsSkipped()
    {
        var first = new byte[] { 0x11 };
        var second = new byte[] { 0x22, 0x33 };
        var third = new byte[] { 0x44 };
        var wire = FramingTestHelpers.Concat(
            ManualEncode(first), new byte[] { 0x00, 0x00 },
            ManualEncode(second), new byte[] { 0x00, 0x00, 0x00, 0x00 },
            ManualEncode(third));

        var messages = ReadAll(wire);

        Assert.Equal(3, messages.Count);
        Assert.Equal(first, messages[0]);
        Assert.Equal(second, messages[1]);
        Assert.Equal(third, messages[2]);
    }

    [Fact]
    public void B2Q1_CoalescedAndFragmentedInputs_AreEquivalent()
    {
        var a = FramingTestHelpers.CreatePattern(40_000);
        var b = FramingTestHelpers.CreatePattern(3);
        var c = FramingTestHelpers.CreatePattern(65_536);
        var wire = FramingTestHelpers.Concat(
            new byte[] { 0x00, 0x00 },
            ManualEncode(a),
            ManualEncode(b),
            new byte[] { 0x00, 0x00 },
            ManualEncode(c));

        var coalesced = ReadAll(wire);
        var fragmented = ReadAll(wire, maxReadSize: 1);

        Assert.Equal(3, coalesced.Count);
        Assert.Equal(a, coalesced[0]);
        Assert.Equal(b, coalesced[1]);
        Assert.Equal(c, coalesced[2]);

        Assert.Equal(coalesced.Count, fragmented.Count);
        for (var i = 0; i < coalesced.Count; i++)
        {
            Assert.Equal(coalesced[i], fragmented[i]);
        }
    }

    // ---- Truncation and malformed input --------------------------------------------------

    [Fact]
    public void B2Q1_TruncatedHeader_SingleByte_ThrowsAndReportsPosition()
    {
        var reader = new BoltMessageReader(new MemoryStream(new byte[] { 0x01 }));
        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
        Assert.Equal(1, reader.Position);
    }

    [Fact]
    public void B2Q1_TruncatedMaxChunk_MidPayload_ReportsExactPosition()
    {
        var wire = new byte[2 + 100];
        wire[0] = 0xFF;
        wire[1] = 0xFF; // declares 65,535 bytes
        var reader = new BoltMessageReader(new MemoryStream(wire));

        Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
        Assert.Equal(102, reader.Position);
    }

    [Fact]
    public void B2Q1_AllTruncationsOfAValidMessage_AreDeterministic()
    {
        var message = FramingTestHelpers.CreatePattern(300);
        var wire = ManualEncode(message);

        // An empty stream is a clean end of stream, not a truncation.
        Assert.Null(new BoltMessageReader(new MemoryStream(Array.Empty<byte>())).ReadMessage());

        // Every non-empty prefix that does not include both terminator bytes must throw
        // truncated; the complete wire is the message itself.
        for (var length = 1; length < wire.Length; length++)
        {
            var prefix = wire[..length];
            var reader = new BoltMessageReader(new MemoryStream(prefix));
            Assert.Throws<BoltFramingTruncatedException>(() => reader.ReadMessage());
        }

        Assert.Equal(message, ReadOne(wire));
    }

    [Fact]
    public void B2Q1_OverLimit_IsRejectedBeforeAnyLargeRead()
    {
        // A guard stream that records the largest single read requested and throws if asked
        // to read more than the limit. The codec must reject the declared 65,535-byte chunk
        // before reading it.
        var guard = new MaxReadGuardedStream(new byte[] { 0xFF, 0xFF }, maxAllowedRead: 100);
        var reader = new BoltMessageReader(guard, new BoltFramingLimits(maxMessageSize: 100));

        Assert.Throws<BoltFramingFormatException>(() => reader.ReadMessage());
        Assert.Equal(2, guard.LargestReadRequested);
    }

    [Fact]
    public void B2Q1_OverLimit_OnSecondChunk_IsRejected()
    {
        // First chunk exactly fills the limit; the second chunk pushes it over.
        var limits = new BoltFramingLimits(maxMessageSize: 65_535);
        var wire = new byte[]
        {
            0xFF, 0xFF,
        }.Concat(new byte[65_535])
         .Concat(new byte[] { 0x00, 0x01, 0x2A, 0x00, 0x00 })
         .ToArray();

        var reader = new BoltMessageReader(new MemoryStream(wire), limits);

        Assert.Throws<BoltFramingFormatException>(() => reader.ReadMessage());
    }

    [Fact]
    public void B2Q1_ExactlyAtLimit_BuiltFromTwoChunks_Passes()
    {
        var message = FramingTestHelpers.CreatePattern(65_536);
        var wire = ManualEncode(message);
        var limits = new BoltFramingLimits(maxMessageSize: 65_536);

        Assert.Equal(message, ReadOne(wire, limits));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void B2Q1_NonPositiveMessageLimit_IsRejected(int maxMessageSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoltFramingLimits(maxMessageSize));
    }

    // ---- Async and mixed API -------------------------------------------------------------

    [Fact]
    public async Task B2Q1_Async_NoopAndFragmentation_ReconstructsMessages()
    {
        var first = FramingTestHelpers.CreatePattern(70_000);
        var second = new byte[] { 0x09 };
        var wire = FramingTestHelpers.Concat(
            new byte[] { 0x00, 0x00 },
            ManualEncode(first),
            new byte[] { 0x00, 0x00 },
            ManualEncode(second));

        using var stream = new FragmentingStream(wire, maxReadSize: 3);
        var reader = new BoltMessageReader(stream);

        Assert.Equal(first, await reader.ReadMessageAsync());
        Assert.Equal(second, await reader.ReadMessageAsync());
        Assert.Null(await reader.ReadMessageAsync());
    }

    [Fact]
    public void B2Q1_TryReadMessage_And_ReadMessage_ShareOneReader()
    {
        var a = new byte[] { 0x01 };
        var b = new byte[] { 0x02, 0x03 };
        var wire = FramingTestHelpers.Concat(ManualEncode(a), ManualEncode(b));
        var reader = new BoltMessageReader(new MemoryStream(wire));

        var destination = new ArrayBufferWriter<byte>();
        Assert.True(reader.TryReadMessage(destination));
        Assert.Equal(a, destination.WrittenSpan.ToArray());
        Assert.Equal(b, reader.ReadMessage());
        Assert.Null(reader.ReadMessage());
    }

    [Fact]
    public void B2Q1_Position_IncludesNoopHeaderBytes()
    {
        var wire = FramingTestHelpers.Concat(new byte[] { 0x00, 0x00 }, new byte[] { 0x00, 0x01, 0xAB, 0x00, 0x00 });
        var reader = new BoltMessageReader(new MemoryStream(wire));

        reader.ReadMessage();

        Assert.Equal(wire.Length, reader.Position);
    }

    [Fact]
    public void B2Q1_EmptyMessage_IsNotRepresentable()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);
        Assert.Throws<ArgumentException>(() => writer.WriteMessage(ReadOnlySpan<byte>.Empty));
        Assert.Equal(0, buffer.WrittenCount);

        // A lone terminator is a NOOP, not a zero-length message.
        Assert.Empty(ReadAll(new byte[] { 0x00, 0x00 }));
    }

    [Fact]
    public void B2Q1_WriteNoop_ProducesBytesTheReaderIgnores()
    {
        var payload = new byte[] { 0x7E };
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BoltMessageWriter(buffer);
        writer.WriteNoop();
        writer.WriteMessage(payload);

        Assert.Equal(
            FramingTestHelpers.Concat(new byte[] { 0x00, 0x00 }, ManualEncode(payload)),
            buffer.WrittenSpan.ToArray());

        Assert.Equal(new[] { payload }, ReadAll(buffer.WrittenSpan.ToArray()));
    }
}
