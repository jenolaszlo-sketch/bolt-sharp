using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.PackStream;

/// <summary>
/// Writes PackStream 1.0 values to an <see cref="IBufferWriter{T}"/>. Supports Null,
/// Boolean, signed Int64, Float64, UTF-8 String and Byte arrays (B1-A/B1-B), recursive
/// Lists, Maps and Structures (B1-C) and the Bolt graph structures (B1-D1/B1-D2). All
/// multi-byte values are written big-endian.
/// <para>
/// <b>Exception-safety contract (B1-R1, finding F3).</b> Every public write operation
/// performs a bounded validation pass — the same recursive walk used for emission, run
/// without writing — before any bytes are committed. If validation of the supplied value
/// fails, the operation throws a <see cref="PackStreamException"/> and the contents of the
/// destination <see cref="IBufferWriter{T}"/> are exactly as they were before the call: a
/// failed write never commits partial output. This guarantee covers validation of the
/// supplied value only. It does not cover failures raised by the destination writer itself
/// (a throwing or contract-violating <see cref="IBufferWriter{T}"/> implementation): if the
/// destination throws mid-write, the state of the destination is undefined and the caller
/// must discard its buffer.
/// </para>
/// </summary>
public sealed class PackStreamWriter
{
    private readonly IBufferWriter<byte> _output;
    private readonly PackStreamLimits _limits;

    public PackStreamWriter(IBufferWriter<byte> output)
        : this(output, PackStreamLimits.Default)
    {
    }

    public PackStreamWriter(IBufferWriter<byte> output, PackStreamLimits limits)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(limits);

        _output = output;
        _limits = limits;
    }

    /// <summary>Writes the Null value (<c>C0</c>).</summary>
    public void WriteNull() => WriteMarker(PackStreamMarkers.Null);

    /// <summary>Writes a Boolean (<c>C3</c> true, <c>C2</c> false).</summary>
    public void WriteBoolean(bool value) =>
        WriteMarker(value ? PackStreamMarkers.True : PackStreamMarkers.False);

    /// <summary>
    /// Writes a signed 64-bit integer using the most compact PackStream
    /// representation permitted by the specification's optimal-representation table.
    /// </summary>
    public void WriteInteger(long value)
    {
        // TINY_INT: -16..127, encoded directly in the marker byte.
        if (value is >= -16 and <= 127)
        {
            WriteMarker(unchecked((byte)value));
            return;
        }

        // INT_8: -128..-17 (positive values >= 128 cannot use a signed 8-bit field).
        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            var span = _output.GetSpan(2);
            span[0] = PackStreamMarkers.Int8;
            span[1] = unchecked((byte)value);
            _output.Advance(2);
            return;
        }

        // INT_16: -32768..-129 and 128..32767.
        if (value is >= short.MinValue and <= short.MaxValue)
        {
            var span = _output.GetSpan(3);
            span[0] = PackStreamMarkers.Int16;
            BinaryPrimitives.WriteInt16BigEndian(span[1..], (short)value);
            _output.Advance(3);
            return;
        }

        // INT_32: -2147483648..-32769 and 32768..2147483647.
        if (value is >= int.MinValue and <= int.MaxValue)
        {
            var span = _output.GetSpan(5);
            span[0] = PackStreamMarkers.Int32;
            BinaryPrimitives.WriteInt32BigEndian(span[1..], (int)value);
            _output.Advance(5);
            return;
        }

        // INT_64: everything else.
        var span64 = _output.GetSpan(9);
        span64[0] = PackStreamMarkers.Int64;
        BinaryPrimitives.WriteInt64BigEndian(span64[1..], value);
        _output.Advance(9);
    }

    /// <summary>Writes an IEEE 754 double-precision value (<c>C1</c> + 8 bytes).</summary>
    public void WriteFloat(double value)
    {
        var span = _output.GetSpan(9);
        span[0] = PackStreamMarkers.Float64;
        BinaryPrimitives.WriteDoubleBigEndian(span[1..], value);
        _output.Advance(9);
    }

    /// <summary>
    /// Writes a UTF-8 string. The marker is chosen from the UTF-8 byte length, not the
    /// .NET character count. Throws <see cref="PackStreamFormatException"/> for invalid
    /// UTF-16 input (for example unpaired surrogates) or if the encoded length exceeds
    /// <see cref="PackStreamLimits.MaxStringByteLength"/>. Validation precedes all writes,
    /// so a rejected string leaves the destination unchanged.
    /// </summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteStringValue(value, validateOnly: false);
    }

    /// <summary>
    /// Writes a byte array. Throws <see cref="PackStreamFormatException"/> if the length
    /// exceeds <see cref="PackStreamLimits.MaxBytesLength"/>. Validation precedes all
    /// writes, so a rejected byte array leaves the destination unchanged.
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> value) => WriteBytesValue(value, validateOnly: false);

    /// <summary>
    /// Writes a list of values. Validated in full before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The item count exceeds <see cref="PackStreamLimits.MaxListItems"/>, the nesting depth
    /// exceeds <see cref="PackStreamLimits.MaxDepth"/>, or any nested value fails
    /// validation. On failure the destination is left unchanged (see the class-level
    /// exception-safety contract).
    /// </exception>
    public void WriteList(IReadOnlyList<PackStreamValue?> items)
    {
        WriteListCore(items, depth: 0, validateOnly: true);
        WriteListCore(items, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes a map. Keys must not be null; each key is written as a string. Validated in
    /// full before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// An entry count exceeds <see cref="PackStreamLimits.MaxMapEntries"/>, a key is null,
    /// the nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/>, or any nested
    /// value fails validation. On failure the destination is left unchanged (see the
    /// class-level exception-safety contract).
    /// </exception>
    public void WriteMap(IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries)
    {
        WriteMapCore(entries, depth: 0, validateOnly: true);
        WriteMapCore(entries, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes a generic structure. Tags must be in the range 0-127 and a structure may carry
    /// at most 15 fields. The tags reserved for Bolt graph structures (<c>4E</c> Node,
    /// <c>52</c> Relationship, <c>72</c> UnboundRelationship, <c>50</c> Path) require the
    /// exact field counts those structures have on the wire — 4, 8, 4 and 3 respectively —
    /// so that only decodable structures are emitted (B1-R1, finding F4). Unknown tags
    /// remain writable with any field count. Validated in full before any bytes are
    /// committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The tag is out of range, the field count exceeds 15, a reserved graph tag is used
    /// with a field count other than its own, the nesting depth exceeds
    /// <see cref="PackStreamLimits.MaxDepth"/>, or any nested value fails validation. On
    /// failure the destination is left unchanged (see the class-level exception-safety
    /// contract).
    /// </exception>
    public void WriteStructure(byte tag, IReadOnlyList<PackStreamValue?> fields)
    {
        WriteStructureCore(tag, fields, depth: 0, validateOnly: true);
        WriteStructureCore(tag, fields, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes a Bolt graph Node structure (tag <c>4E</c>, four fields). Validated in full
    /// before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/> or any nested value
    /// (label string, property map entry, element ID) fails validation. On failure the
    /// destination is left unchanged (see the class-level exception-safety contract).
    /// </exception>
    public void WriteNode(PackStreamNode node)
    {
        WriteNodeCore(node, depth: 0, validateOnly: true);
        WriteNodeCore(node, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes a Bolt graph Relationship structure (tag <c>52</c>, eight fields). Validated
    /// in full before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/> or any nested value
    /// fails validation. On failure the destination is left unchanged (see the class-level
    /// exception-safety contract).
    /// </exception>
    public void WriteRelationship(PackStreamRelationship relationship)
    {
        WriteRelationshipCore(relationship, depth: 0, validateOnly: true);
        WriteRelationshipCore(relationship, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes a Bolt graph UnboundRelationship structure (tag <c>72</c>, four fields).
    /// Validated in full before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/> or any nested value
    /// fails validation. On failure the destination is left unchanged (see the class-level
    /// exception-safety contract).
    /// </exception>
    public void WriteUnboundRelationship(PackStreamUnboundRelationship relationship)
    {
        WriteUnboundRelationshipCore(relationship, depth: 0, validateOnly: true);
        WriteUnboundRelationshipCore(relationship, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes a Bolt Path structure (tag <c>50</c>, three fields). The three field lists
    /// (nodes, unbound relationships, indices) each respect
    /// <see cref="PackStreamLimits.MaxListItems"/>, exactly like <see cref="WriteList"/>, so
    /// the writer never emits a Path its own reader would reject under identical limits
    /// (B1-R1, finding F2). Validated in full before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// Any of the three field lists exceeds <see cref="PackStreamLimits.MaxListItems"/>, the
    /// nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/>, or any nested value
    /// fails validation. On failure the destination is left unchanged (see the class-level
    /// exception-safety contract).
    /// </exception>
    public void WritePath(PackStreamPath path)
    {
        WritePathCore(path, depth: 0, validateOnly: true);
        WritePathCore(path, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Writes any supported value recursively. A <see langword="null"/> value writes PackStream
    /// Null. Validated in full before any bytes are committed.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// Any limit (depth, count, string or byte-array length) or value constraint is
    /// violated anywhere in the tree, or a value type is unsupported. On failure the
    /// destination is left unchanged (see the class-level exception-safety contract).
    /// </exception>
    public void WriteValue(PackStreamValue? value)
    {
        WriteValue(value, depth: 0, validateOnly: true);
        WriteValue(value, depth: 0, validateOnly: false);
    }

    /// <summary>
    /// Recursive writer core. In <paramref name="validateOnly"/> mode the exact same
    /// validation that emission performs is executed without touching the destination
    /// (B1-R1, finding F3); in emission mode the value is written. The two modes cannot
    /// diverge because they share this single code path.
    /// </summary>
    private void WriteValue(PackStreamValue? value, int depth, bool validateOnly)
    {
        switch (value)
        {
            case null:
                if (!validateOnly)
                {
                    WriteNull();
                }

                return;
            case PackStreamBoolean boolean:
                if (!validateOnly)
                {
                    WriteBoolean(boolean.Value);
                }

                return;
            case PackStreamInteger integer:
                if (!validateOnly)
                {
                    WriteInteger(integer.Value);
                }

                return;
            case PackStreamFloat floatValue:
                if (!validateOnly)
                {
                    WriteFloat(floatValue.Value);
                }

                return;
            case PackStreamString stringValue:
                WriteStringValue(stringValue.Value, validateOnly);
                return;
            case PackStreamBytes bytes:
                WriteBytesValue(bytes.OwnedBytes, validateOnly);
                return;
            case PackStreamList list:
                WriteListCore(list.Items, depth, validateOnly);
                return;
            case PackStreamMap map:
                WriteMapCore(map.Entries, depth, validateOnly);
                return;
            case PackStreamStructure structure:
                WriteStructureCore(structure.Tag, structure.Fields, depth, validateOnly);
                return;
            case PackStreamNode node:
                WriteNodeCore(node, depth, validateOnly);
                return;
            case PackStreamRelationship relationship:
                WriteRelationshipCore(relationship, depth, validateOnly);
                return;
            case PackStreamUnboundRelationship unboundRelationship:
                WriteUnboundRelationshipCore(unboundRelationship, depth, validateOnly);
                return;
            case PackStreamPath path:
                WritePathCore(path, depth, validateOnly);
                return;
            default:
                throw new PackStreamFormatException(
                    $"Unsupported PackStream value type '{value.GetType().Name}'.");
        }
    }

    private void WriteListCore(IReadOnlyList<PackStreamValue?> items, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(items);

        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        if (items.Count > _limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"List item count {items.Count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        if (!validateOnly)
        {
            WriteCollectionHeader(
                items.Count,
                PackStreamMarkers.TinyListBase,
                PackStreamMarkers.List8,
                PackStreamMarkers.List16,
                PackStreamMarkers.List32);
        }

        for (var i = 0; i < items.Count; i++)
        {
            WriteValue(items[i], level, validateOnly);
        }
    }

    private void WriteMapCore(IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        if (entries.Count > _limits.MaxMapEntries)
        {
            throw new PackStreamFormatException(
                $"Map entry count {entries.Count} exceeds the configured maximum of {_limits.MaxMapEntries}.");
        }

        if (!validateOnly)
        {
            WriteCollectionHeader(
                entries.Count,
                PackStreamMarkers.TinyMapBase,
                PackStreamMarkers.Map8,
                PackStreamMarkers.Map16,
                PackStreamMarkers.Map32);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Key is null)
            {
                throw new PackStreamFormatException("PackStream map keys must not be null.");
            }

            WriteStringValue(entry.Key, validateOnly);
            WriteValue(entry.Value, level, validateOnly);
        }
    }

    private void WriteStructureCore(byte tag, IReadOnlyList<PackStreamValue?> fields, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (tag > PackStreamMarkers.StructureTagMax)
        {
            throw new PackStreamFormatException(
                $"Structure tag {tag} is outside the permitted range 0-{PackStreamMarkers.StructureTagMax}.");
        }

        if (fields.Count > PackStreamMarkers.MaxStructureFields)
        {
            throw new PackStreamFormatException(
                $"Structure field count {fields.Count} exceeds the maximum of {PackStreamMarkers.MaxStructureFields}.");
        }

        ValidateReservedGraphTagArity(tag, fields.Count);

        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        if (!validateOnly)
        {
            WriteMarker((byte)(PackStreamMarkers.StructureBase | fields.Count));
            WriteMarker(tag);
        }

        for (var i = 0; i < fields.Count; i++)
        {
            WriteValue(fields[i], level, validateOnly);
        }
    }

    private void WriteNodeCore(PackStreamNode node, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(node);

        var level = EnterContainer(depth);

        if (!validateOnly)
        {
            WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.NodeFieldCount));
            WriteMarker(PackStreamMarkers.NodeTag);

            WriteInteger(node.Id);
        }

        WriteStringList(node.Labels, level, validateOnly);
        WriteValue(node.Properties, level, validateOnly);
        WriteStringValue(node.ElementId, validateOnly);
    }

    private void WriteRelationshipCore(PackStreamRelationship relationship, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        var level = EnterContainer(depth);

        if (!validateOnly)
        {
            WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.RelationshipFieldCount));
            WriteMarker(PackStreamMarkers.RelationshipTag);

            WriteInteger(relationship.Id);
            WriteInteger(relationship.StartNodeId);
            WriteInteger(relationship.EndNodeId);
        }

        WriteStringValue(relationship.Type, validateOnly);
        WriteValue(relationship.Properties, level, validateOnly);
        WriteStringValue(relationship.ElementId, validateOnly);
        WriteStringValue(relationship.StartNodeElementId, validateOnly);
        WriteStringValue(relationship.EndNodeElementId, validateOnly);
    }

    private void WriteUnboundRelationshipCore(PackStreamUnboundRelationship relationship, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        var level = EnterContainer(depth);

        if (!validateOnly)
        {
            WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.UnboundRelationshipFieldCount));
            WriteMarker(PackStreamMarkers.UnboundRelationshipTag);

            WriteInteger(relationship.Id);
        }

        WriteStringValue(relationship.Type, validateOnly);
        WriteValue(relationship.Properties, level, validateOnly);
        WriteStringValue(relationship.ElementId, validateOnly);
    }

    private void WritePathCore(PackStreamPath path, int depth, bool validateOnly)
    {
        ArgumentNullException.ThrowIfNull(path);

        // B1-R1, finding F2: all three field lists are lists on the wire and must respect
        // MaxListItems exactly like WriteList does, so the writer never emits a Path that
        // its own reader rejects under identical limits. Counts are compared as ints only;
        // no arithmetic is performed on them, so no overflow is possible. The checks run
        // before any bytes are emitted.
        if (path.Nodes.Count > _limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"Path node count {path.Nodes.Count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        if (path.UnboundRelationships.Count > _limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"Path relationship count {path.UnboundRelationships.Count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        if (path.Indices.Count > _limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"Path index count {path.Indices.Count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        // The path itself is one nesting level; each of its three field lists is one deeper,
        // and the nodes/relationships inside those lists are one deeper again.
        var level = EnterContainer(depth);
        var fieldListLevel = EnterContainer(level);

        if (!validateOnly)
        {
            WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.PathFieldCount));
            WriteMarker(PackStreamMarkers.PathTag);

            WriteCollectionHeader(
                path.Nodes.Count,
                PackStreamMarkers.TinyListBase,
                PackStreamMarkers.List8,
                PackStreamMarkers.List16,
                PackStreamMarkers.List32);
        }

        for (var i = 0; i < path.Nodes.Count; i++)
        {
            WriteNodeCore(path.Nodes[i], fieldListLevel, validateOnly);
        }

        if (!validateOnly)
        {
            WriteCollectionHeader(
                path.UnboundRelationships.Count,
                PackStreamMarkers.TinyListBase,
                PackStreamMarkers.List8,
                PackStreamMarkers.List16,
                PackStreamMarkers.List32);
        }

        for (var i = 0; i < path.UnboundRelationships.Count; i++)
        {
            WriteUnboundRelationshipCore(path.UnboundRelationships[i], fieldListLevel, validateOnly);
        }

        if (!validateOnly)
        {
            WriteCollectionHeader(
                path.Indices.Count,
                PackStreamMarkers.TinyListBase,
                PackStreamMarkers.List8,
                PackStreamMarkers.List16,
                PackStreamMarkers.List32);

            for (var i = 0; i < path.Indices.Count; i++)
            {
                WriteInteger(path.Indices[i]);
            }
        }
    }

    private void WriteStringList(IReadOnlyList<string> labels, int depth, bool validateOnly)
    {
        var level = EnterContainer(depth);

        if (labels.Count > _limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"Label count {labels.Count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        if (!validateOnly)
        {
            WriteCollectionHeader(
                labels.Count,
                PackStreamMarkers.TinyListBase,
                PackStreamMarkers.List8,
                PackStreamMarkers.List16,
                PackStreamMarkers.List32);
        }

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            if (label is null)
            {
                throw new PackStreamFormatException("Node labels must not be null.");
            }

            WriteStringValue(label, validateOnly);
        }
    }

    /// <summary>
    /// B1-R1, finding F4: the tags reserved for Bolt graph structures must not be emitted
    /// with a field count that no reader accepts. Generic structures using these tags with
    /// the correct arity remain writable; unknown tags are not affected.
    /// </summary>
    private static void ValidateReservedGraphTagArity(byte tag, int fieldCount)
    {
        var requiredFieldCount = tag switch
        {
            PackStreamMarkers.NodeTag => PackStreamMarkers.NodeFieldCount,
            PackStreamMarkers.RelationshipTag => PackStreamMarkers.RelationshipFieldCount,
            PackStreamMarkers.UnboundRelationshipTag => PackStreamMarkers.UnboundRelationshipFieldCount,
            PackStreamMarkers.PathTag => PackStreamMarkers.PathFieldCount,
            _ => -1,
        };

        if (requiredFieldCount >= 0 && fieldCount != requiredFieldCount)
        {
            throw new PackStreamFormatException(
                $"Structure tag 0x{tag:X2} is reserved for a Bolt graph structure that requires exactly " +
                $"{requiredFieldCount} field(s), but {fieldCount} were supplied.");
        }
    }

    private void WriteStringValue(string value, bool validateOnly)
    {
        StrictUtf8.ValidateUtf16(value);

        var byteCount = StrictUtf8.Instance.GetByteCount(value);
        if (byteCount > _limits.MaxStringByteLength)
        {
            throw new PackStreamFormatException(
                $"Encoded string length {byteCount} exceeds the configured maximum of {_limits.MaxStringByteLength} byte(s).");
        }

        if (validateOnly)
        {
            return;
        }

        if (byteCount <= 15)
        {
            WriteMarker((byte)(PackStreamMarkers.TinyStringBase | byteCount));
        }
        else if (byteCount <= byte.MaxValue)
        {
            WriteMarker(PackStreamMarkers.String8);
            WriteLengthBigEndian((uint)byteCount, 1);
        }
        else if (byteCount <= ushort.MaxValue)
        {
            WriteMarker(PackStreamMarkers.String16);
            WriteLengthBigEndian((uint)byteCount, 2);
        }
        else
        {
            WriteMarker(PackStreamMarkers.String32);
            WriteLengthBigEndian((uint)byteCount, 4);
        }

        if (byteCount > 0)
        {
            var span = _output.GetSpan(byteCount);
            StrictUtf8.Instance.GetBytes(value, span);
            _output.Advance(byteCount);
        }
    }

    private void WriteBytesValue(ReadOnlySpan<byte> value, bool validateOnly)
    {
        if (value.Length > _limits.MaxBytesLength)
        {
            throw new PackStreamFormatException(
                $"Byte-array length {value.Length} exceeds the configured maximum of {_limits.MaxBytesLength} byte(s).");
        }

        if (validateOnly)
        {
            return;
        }

        if (value.Length <= byte.MaxValue)
        {
            WriteMarker(PackStreamMarkers.Bytes8);
            WriteLengthBigEndian((uint)value.Length, 1);
        }
        else if (value.Length <= ushort.MaxValue)
        {
            WriteMarker(PackStreamMarkers.Bytes16);
            WriteLengthBigEndian((uint)value.Length, 2);
        }
        else
        {
            WriteMarker(PackStreamMarkers.Bytes32);
            WriteLengthBigEndian((uint)value.Length, 4);
        }

        if (!value.IsEmpty)
        {
            var span = _output.GetSpan(value.Length);
            value.CopyTo(span);
            _output.Advance(value.Length);
        }
    }

    private int EnterContainer(int depth)
    {
        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        return level;
    }

    private void WriteCollectionHeader(int count, byte tinyBase, byte marker8, byte marker16, byte marker32)
    {
        if (count <= 15)
        {
            WriteMarker((byte)(tinyBase | count));
        }
        else if (count <= byte.MaxValue)
        {
            WriteMarker(marker8);
            WriteLengthBigEndian((uint)count, 1);
        }
        else if (count <= ushort.MaxValue)
        {
            WriteMarker(marker16);
            WriteLengthBigEndian((uint)count, 2);
        }
        else
        {
            WriteMarker(marker32);
            WriteLengthBigEndian((uint)count, 4);
        }
    }

    private void WriteMarker(byte marker)
    {
        var span = _output.GetSpan(1);
        span[0] = marker;
        _output.Advance(1);
    }

    private void WriteLengthBigEndian(uint value, int size)
    {
        var span = _output.GetSpan(size);

        switch (size)
        {
            case 1:
                span[0] = (byte)value;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)value);
                break;
            default:
                BinaryPrimitives.WriteUInt32BigEndian(span, value);
                break;
        }

        _output.Advance(size);
    }
}
