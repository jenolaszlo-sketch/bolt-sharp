using System.Buffers;
using System.Runtime.InteropServices;

using BoltSharp.PackStream;

using Xunit;

namespace BoltSharp.Tests.Qualification;

/// <summary>
/// B1-Q1 independent reviewer probes.
/// These probes were designed by the qualification reviewer directly from the PackStream 1.0
/// and Bolt 5.8 structure-semantics specifications, independently of the producer's fixtures.
/// Some probes are <c>documenting</c> probes: they pass while demonstrating a defect that is
/// reported in docs/reviews/B1-Q1-packstream-qualification.md. Such tests are marked
/// "KNOWN DEFECT" and must not be read as endorsements of the observed behaviour.
/// </summary>
public sealed class B1Q1ReviewerProbes
{
    // ------------------------------------------------------------------
    // Probe 01: every byte value as a single-byte input.
    // Safety property: no marker may produce a non-PackStream exception
    // (IndexOutOfRange, Overflow, ...) and no marker may decode from less
    // input than it requires.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe01_SingleByteSweep_NeverThrowsForeignExceptions()
    {
        for (var b = 0; b < 256; b++)
        {
            var marker = (byte)b;
            var reader = new PackStreamReader(new[] { marker });
            PackStreamValue? value = null;
            Exception? failure = null;

            try
            {
                value = reader.ReadValue();
            }
            catch (PackStreamException ex)
            {
                failure = ex;
            }
            catch (Exception ex)
            {
                Assert.Fail($"Marker 0x{marker:X2} threw a non-PackStream exception: {ex.GetType().Name}: {ex.Message}");
            }

            if (IsCompleteSingleByteValue(marker))
            {
                Assert.True(failure is null, $"Marker 0x{marker:X2} should decode from a single byte but threw {failure?.GetType().Name}.");
                AssertCompleteSingleByteValue(marker, value);
            }
            else
            {
                Assert.True(failure is not null, $"Marker 0x{marker:X2} should not decode from a single byte.");
                Assert.True(
                    failure is PackStreamTruncatedException or PackStreamFormatException,
                    $"Marker 0x{marker:X2} failed with an unexpected exception type {failure!.GetType().Name}.");
            }
        }
    }

    private static bool IsCompleteSingleByteValue(byte marker) =>
        marker <= 0x7F ||
        marker == 0x80 ||
        marker == 0x90 ||
        marker == 0xA0 ||
        marker == 0xC0 ||
        marker == 0xC2 ||
        marker == 0xC3 ||
        marker >= 0xF0;

    private static void AssertCompleteSingleByteValue(byte marker, PackStreamValue? value)
    {
        switch (marker)
        {
            case <= 0x7F:
                Assert.Equal(marker, Assert.IsType<PackStreamInteger>(value).Value);
                break;
            case 0x80:
                Assert.Equal(string.Empty, Assert.IsType<PackStreamString>(value).Value);
                break;
            case 0x90:
                Assert.Empty(Assert.IsType<PackStreamList>(value).Items);
                break;
            case 0xA0:
                Assert.Empty(Assert.IsType<PackStreamMap>(value).Entries);
                break;
            case 0xC0:
                Assert.Null(value);
                break;
            case 0xC2:
                Assert.False(Assert.IsType<PackStreamBoolean>(value).Value);
                break;
            case 0xC3:
                Assert.True(Assert.IsType<PackStreamBoolean>(value).Value);
                break;
            default:
                // 0xF0-0xFF: tiny integers -16..-1.
                Assert.Equal(marker - 256, Assert.IsType<PackStreamInteger>(value).Value);
                break;
        }
    }

    // ------------------------------------------------------------------
    // Probe 02: truncation at every byte boundary of representative values.
    // Safety property: every proper prefix of a complete value fails with a
    // PackStream exception; nothing ever decodes partially and no foreign
    // exception escapes.
    // ------------------------------------------------------------------

    public static TheoryData<string, byte[]> TruncationTargets() => new()
    {
        { "Node", NodeBytes() },
        { "Path", SpecExamplePathBytes() },
        { "Map", MapBytes() },
        { "List", ListBytes() },
        { "Bytes", BytesBytes() },
        { "TinyString", new byte[] { 0x81, 0x41 } },
    };

    [Theory]
    [MemberData(nameof(TruncationTargets))]
    public void Probe02_TruncationAtEveryByteBoundary_AlwaysFailsWithPackStreamException(string name, byte[] full)
    {
        for (var length = 0; length < full.Length; length++)
        {
            var prefix = full.AsSpan(0, length).ToArray();
            var reader = new PackStreamReader(prefix);
            var exception = Record.Exception(() => reader.ReadValue());

            Assert.True(
                exception is PackStreamException,
                $"{name} truncated to {length}/{full.Length} bytes: expected PackStreamException, saw " +
                $"{exception?.GetType().Name ?? "successful decode"}");
        }
    }

    private static byte[] NodeBytes() => new byte[]
    {
        0xB4, 0x4E, 0x01, 0x91, 0x86, 0x50, 0x65, 0x72, 0x73, 0x6F, 0x6E,
        0xA1, 0x84, 0x6E, 0x61, 0x6D, 0x65, 0x85, 0x41, 0x6C, 0x69, 0x63, 0x65,
        0x83, 0x6E, 0x3A, 0x31,
    };

    private static byte[] SpecExamplePathBytes() => new byte[]
    {
        0xB3, 0x50,
        0x93,
        0xB4, 0x4E, 0x2A, 0x90, 0xA0, 0x84, 0x6E, 0x3A, 0x34, 0x32,
        0xB4, 0x4E, 0x45, 0x90, 0xA0, 0x84, 0x6E, 0x3A, 0x36, 0x39,
        0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31,
        0x92,
        0xB4, 0x72, 0xC9, 0x03, 0xE8, 0x80, 0xA0, 0x86, 0x72, 0x3A, 0x31, 0x30, 0x30, 0x30,
        0xB4, 0x72, 0xC9, 0x03, 0xE9, 0x80, 0xA0, 0x86, 0x72, 0x3A, 0x31, 0x30, 0x30, 0x31,
        0x96, 0x01, 0x01, 0x01, 0x00, 0xFE, 0x02,
    };

    private static byte[] MapBytes() => new byte[] { 0xA2, 0x81, 0x61, 0x01, 0x81, 0x62, 0x02 };

    private static byte[] ListBytes() => new byte[] { 0x93, 0x01, 0x02, 0x03 };

    private static byte[] BytesBytes() => new byte[] { 0xCC, 0x03, 0x01, 0x02, 0x03 };

    // ------------------------------------------------------------------
    // Probe 03: the aggregate element budget must bound a graph of nested
    // containers, not merely the number of top-level values.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe03_ElementBudgetBoundsNestedContainerGraphs()
    {
        // One list of 100 lists of 20 integers = 1 + 100 + 2000 = 2101 decoded values.
        // Each individual list is far below MaxListItems, so only the aggregate budget
        // can bound this input.
        var bytes = BuildListOfLists(outerLists: 100, innerIntegers: 20);
        Assert.Equal(2101, CountValues(bytes));

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(bytes, new PackStreamLimits(maxTotalElements: 1000)).ReadValue());

        Assert.IsType<PackStreamList>(
            new PackStreamReader(bytes, new PackStreamLimits(maxTotalElements: 3000)).ReadValue());
    }

    private static byte[] BuildListOfLists(int outerLists, int innerIntegers)
    {
        var inner = new byte[2 + innerIntegers];
        inner[0] = 0xD4;
        inner[1] = (byte)innerIntegers;
        inner.AsSpan(2).Fill(0x01);

        // Outer list: LIST_16 marker + two-byte count, then the inner lists.
        var prefix = new byte[] { 0xD5, (byte)(outerLists >> 8), (byte)outerLists };
        var result = new byte[prefix.Length + (outerLists * inner.Length)];
        prefix.CopyTo(result, 0);
        var offset = prefix.Length;
        for (var i = 0; i < outerLists; i++)
        {
            inner.CopyTo(result, offset);
            offset += inner.Length;
        }

        return result;
    }

    private static int CountValues(byte[] bytes)
    {
        var index = 0;
        return Count(bytes, ref index);

        static int Count(byte[] bytes, ref int index)
        {
            var marker = bytes[index++];

            if (marker == 0xD4 || marker == 0xD5)
            {
                var count = marker == 0xD4
                    ? bytes[index++]
                    : (bytes[index++] << 8) | bytes[index++];

                var total = 1;
                for (var i = 0; i < count; i++)
                {
                    total += Count(bytes, ref index);
                }

                return total;
            }

            // Leaf value (a tiny integer in this fixture): one byte, already consumed.
            return 1;
        }
    }

    // ------------------------------------------------------------------
    // Probe 04: duplicate map keys whose values are complex nested containers.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe04_DuplicateKeys_LastComplexValueWins()
    {
        // {"a": [1], "a": {"b": 2}} -> single entry "a" whose value is the map.
        var bytes = new byte[] { 0xA2, 0x81, 0x61, 0x91, 0x01, 0x81, 0x61, 0xA1, 0x81, 0x62, 0x02 };

        var map = Assert.IsType<PackStreamMap>(new PackStreamReader(bytes).ReadValue());

        var entry = Assert.Single(map.Entries);
        Assert.Equal("a", entry.Key);
        var nested = Assert.IsType<PackStreamMap>(entry.Value);
        Assert.Equal(2, Assert.IsType<PackStreamInteger>(nested.Entries[0].Value).Value);
    }

    // ------------------------------------------------------------------
    // Probe 05 (KNOWN DEFECT): immutability is not enforced; public collection
    // references can be mutated through array casts.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe05_PublicCollections_CanBeMutatedThroughArrayCasts_KnownDefect()
    {
        var node = new PackStreamNode(1, new[] { "Person" }, EmptyMap(), "n:1");
        ((string[])node.Labels)[0] = "MUTATED";
        Assert.Equal("MUTATED", node.Labels[0]);

        var path = new PackStreamPath(
            new[] { Node(1, "n:1") },
            Array.Empty<PackStreamUnboundRelationship>(),
            Array.Empty<long>());
        ((PackStreamNode[])path.Nodes)[0] = Node(9, "n:9");
        Assert.Equal("n:9", path.Nodes[0].ElementId);

        // The mutation changes the wire bytes: the encoder no longer round-trips the
        // value it decoded.
        var buffer = new ArrayBufferWriter<byte>();
        new PackStreamWriter(buffer).WriteNode(node);
        var encoded = buffer.WrittenSpan.ToArray();

        Assert.Equal(
            "MUTATED",
            Assert.IsType<PackStreamNode>(new PackStreamReader(encoded).ReadValue()).Labels[0]);
    }

    [Fact]
    public void Probe05b_DecodedByteArrays_CanBeMutatedThroughMemoryMarshal_KnownDefect()
    {
        var value = new PackStreamBytes(new byte[] { 0x01, 0x02, 0x03 });

        Assert.True(MemoryMarshal.TryGetArray(value.Value, out var segment));
        segment.Array![segment.Offset] = 0xFF;

        Assert.Equal(0xFF, value.Value.ToArray()[0]);
    }

    // ------------------------------------------------------------------
    // Probe 06 (KNOWN DEFECT): the writer emits bytes before a nested
    // validation failure, and that contract is not documented.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe06_WriterValidationFailureAfterPartialOutput_LeavesBytesWritten_KnownDefect()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer);

        var list = new PackStreamList(new PackStreamValue?[]
        {
            new PackStreamInteger(1),
            new PackStreamString("\uD800"), // unpaired surrogate: invalid UTF-16
        });

        Assert.Throws<PackStreamFormatException>(() => writer.WriteList(list.Items));

        // The list header and the first element were already emitted.
        Assert.Equal(new byte[] { 0x92, 0x01 }, buffer.WrittenSpan.ToArray());
    }

    // ------------------------------------------------------------------
    // Probe 07 (KNOWN DEFECT): WritePath does not enforce MaxListItems, so the
    // writer can emit a path that its own reader rejects under the same limits.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe07_WritePath_BypassesMaxListItems_KnownDefect()
    {
        var limits = new PackStreamLimits(maxListItems: 4);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, limits);

        // WriteList enforces the configured list limit.
        Assert.Throws<PackStreamFormatException>(
            () => writer.WriteList(new PackStreamValue?[] { One(), One(), One(), One(), One() }));

        // WritePath does not: five nodes are emitted although MaxListItems is 4.
        var path = new PackStreamPath(FiveNodes(), Array.Empty<PackStreamUnboundRelationship>(), Array.Empty<long>());
        writer.WritePath(path);

        var bytes = buffer.WrittenSpan.ToArray();
        Assert.Equal(0xB3, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
        Assert.Equal(0x95, bytes[2]); // TINY_LIST carrying a five-item count
        Assert.Equal(0xB4, bytes[3]); // the first node follows immediately

        // The same limits reject those bytes on read: a writer/reader asymmetry.
        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(bytes, limits).ReadValue());
    }

    // ------------------------------------------------------------------
    // Probe 08 (KNOWN DEFECT): a generic structure may carry a reserved graph
    // tag with an arity that no decoder accepts, producing undecodable output.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe08_GenericStructureWithReservedGraphTag_IsWritableButUndecodable_KnownDefect()
    {
        var buffer = new ArrayBufferWriter<byte>();
        new PackStreamWriter(buffer).WriteStructure(0x4E, new PackStreamValue?[] { new PackStreamInteger(1) });
        var bytes = buffer.WrittenSpan.ToArray();

        Assert.Equal(new byte[] { 0xB1, 0x4E, 0x01 }, bytes);
        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(bytes).ReadValue());
    }

    // ------------------------------------------------------------------
    // Probe 09: sequential values, malformed trailing value, and reuse of a
    // reader after a failure.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe09_MalformedTrailingValue_FailsAfterPriorSuccess()
    {
        var reader = new PackStreamReader(new byte[] { 0x01, 0xC8 });

        Assert.Equal(1L, reader.ReadInteger());
        Assert.Throws<PackStreamTruncatedException>(() => reader.ReadInteger());
        Assert.Equal(2, reader.Position);
    }

    [Fact]
    public void Probe09b_ContinuingAfterAFailure_ProducesMisalignedValues_Documented()
    {
        // After a failed read the reader is not realigned: the byte that follows the
        // aborted structure is interpreted as a fresh top-level value. The documented
        // contract is to discard the reader; this probe records the consequence of not
        // doing so. (B0 4E is a Node tag with an invalid zero-field arity.)
        var reader = new PackStreamReader(new byte[] { 0xB0, 0x4E, 0x2A });

        Assert.Throws<PackStreamFormatException>(() => reader.ReadValue());
        Assert.Equal(2, reader.Position);
        Assert.Equal(42L, reader.ReadInteger());
    }

    // ------------------------------------------------------------------
    // Probe 10: wider-than-necessary representations of empty values.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0xD0, 0x00 })]
    [InlineData(new byte[] { 0xD1, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xD2, 0x00, 0x00, 0x00, 0x00 })]
    public void Probe10_WiderEmptyStringRepresentations_AreAccepted(byte[] bytes) =>
        Assert.Equal(string.Empty, new PackStreamReader(bytes).ReadString());

    [Theory]
    [InlineData(new byte[] { 0xCD, 0x00, 0x00 })]
    [InlineData(new byte[] { 0xCE, 0x00, 0x00, 0x00, 0x00 })]
    public void Probe10b_WiderEmptyByteArrayRepresentations_AreAccepted(byte[] bytes) =>
        Assert.Empty(new PackStreamReader(bytes).ReadBytes());

    // ------------------------------------------------------------------
    // Probe 11: extreme index values in paths.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe11_LongMaxValueRelationshipIndex_IsRejected()
    {
        var bytes = Concat(
            HeaderWithOneNode(),
            new byte[] { 0x91, 0xB4, 0x72, 0x01, 0x80, 0xA0, 0x83, 0x72, 0x3A, 0x31 },
            new byte[] { 0x92, 0xCB, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 });

        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(bytes).ReadValue());
    }

    [Fact]
    public void Probe11b_LongMaxValueNodeIndex_IsRejected()
    {
        var bytes = Concat(
            HeaderWithOneNode(),
            new byte[] { 0x91, 0xB4, 0x72, 0x01, 0x80, 0xA0, 0x83, 0x72, 0x3A, 0x31 },
            new byte[] { 0x92, 0x01, 0xCB, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(bytes).ReadValue());
    }

    // ------------------------------------------------------------------
    // Probe 12: a declared length inside the configured limit but larger than
    // the available input must be a truncation, not a limit failure.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe12_DeclaredLengthWithinLimitButBeyondInput_ThrowsTruncated()
    {
        // STRING_16 declaring 100,000 bytes, within the 16 MiB default limit.
        var bytes = new byte[] { 0xD1, 0x01, 0x86, 0xA0 };

        Assert.Throws<PackStreamTruncatedException>(() => new PackStreamReader(bytes).ReadString());
    }

    // ------------------------------------------------------------------
    // Probe 13: degenerate limits behave deterministically.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe13_ZeroStringLimit_AllowsEmptyAndRejectsContent()
    {
        var limits = new PackStreamLimits(maxStringByteLength: 0);

        Assert.Equal(string.Empty, new PackStreamReader(new byte[] { 0x80 }, limits).ReadString());
        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(new byte[] { 0x81, 0x41 }, limits).ReadString());
    }

    [Fact]
    public void Probe13b_ZeroDepthLimit_RejectsContainersOnBothSides()
    {
        var limits = new PackStreamLimits(maxDepth: 0);

        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamWriter(new ArrayBufferWriter<byte>(), limits).WriteList(Array.Empty<PackStreamValue?>()));
        Assert.Throws<PackStreamFormatException>(
            () => new PackStreamReader(new byte[] { 0x90 }, limits).ReadValue());
    }

    // ------------------------------------------------------------------
    // Probe 14: null values inside graph field lists.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe14_NullInsidePathNodeList_ThrowsFormat()
    {
        var bytes = new byte[] { 0xB3, 0x50, 0x91, 0xC0, 0x90, 0x90 };

        Assert.Throws<PackStreamFormatException>(() => new PackStreamReader(bytes).ReadValue());
    }

    // ------------------------------------------------------------------
    // Probe 15: unknown (non-graph) structure tags are preserved generically.
    // This records pass-through behaviour for Bolt tags the codec does not
    // implement, such as temporal values (0x49 'I').
    // ------------------------------------------------------------------

    [Fact]
    public void Probe15_TemporalStyleTag_IsPreservedAsGenericStructure()
    {
        var bytes = new byte[] { 0xB2, 0x49, 0x01, 0x02 };

        var structure = Assert.IsType<PackStreamStructure>(new PackStreamReader(bytes).ReadValue());

        Assert.Equal(0x49, structure.Tag);
        Assert.Equal(2, structure.Fields.Count);
    }

    // ------------------------------------------------------------------
    // Probe 16: a map whose declared entry count is within the limit but
    // larger than the available input fails as a truncation.
    // ------------------------------------------------------------------

    [Fact]
    public void Probe16_DeclaredMapCountWithinLimitButBeyondInput_ThrowsTruncated()
    {
        // MAP_32 declaring 100,000 entries (within the default limit) with no payload.
        var bytes = new byte[] { 0xDA, 0x00, 0x01, 0x86, 0xA0 };

        Assert.Throws<PackStreamTruncatedException>(() => new PackStreamReader(bytes).ReadValue());
    }

    // ---- helpers ---------------------------------------------------------

    private static PackStreamMap EmptyMap() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    private static PackStreamNode Node(long id, string elementId) =>
        new(id, Array.Empty<string>(), EmptyMap(), elementId);

    private static PackStreamInteger One() => new(1);

    private static PackStreamNode[] FiveNodes()
    {
        var nodes = new PackStreamNode[5];
        for (var i = 0; i < nodes.Length; i++)
        {
            nodes[i] = Node(i + 1, "n:" + (i + 1));
        }

        return nodes;
    }

    private static byte[] HeaderWithOneNode() => new byte[]
    {
        0xB3, 0x50,
        0x91,
        0xB4, 0x4E, 0x01, 0x90, 0xA0, 0x83, 0x6E, 0x3A, 0x31,
    };

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(part => part.Length);
        var result = new byte[total];
        var offset = 0;

        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
