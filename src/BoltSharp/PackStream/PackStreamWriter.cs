using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.PackStream;

/// <summary>
/// Writes PackStream 1.0 values to an <see cref="IBufferWriter{T}"/>. Supports Null,
/// Boolean, signed Int64, Float64, UTF-8 String and Byte arrays (B1-A/B1-B) plus recursive
/// Lists, Maps and Structures (B1-C). All multi-byte values are written big-endian.
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
    /// <see cref="PackStreamLimits.MaxStringByteLength"/>.
    /// </summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StrictUtf8.ValidateUtf16(value);

        var byteCount = StrictUtf8.Instance.GetByteCount(value);
        if (byteCount > _limits.MaxStringByteLength)
        {
            throw new PackStreamFormatException(
                $"Encoded string length {byteCount} exceeds the configured maximum of {_limits.MaxStringByteLength} byte(s).");
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

    /// <summary>
    /// Writes a byte array. Throws <see cref="PackStreamFormatException"/> if the length
    /// exceeds <see cref="PackStreamLimits.MaxBytesLength"/>.
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length > _limits.MaxBytesLength)
        {
            throw new PackStreamFormatException(
                $"Byte-array length {value.Length} exceeds the configured maximum of {_limits.MaxBytesLength} byte(s).");
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

    /// <summary>Writes a list of values.</summary>
    /// <exception cref="PackStreamFormatException">
    /// The item count exceeds <see cref="PackStreamLimits.MaxListItems"/> or the nesting depth
    /// exceeds <see cref="PackStreamLimits.MaxDepth"/>.
    /// </exception>
    public void WriteList(IReadOnlyList<PackStreamValue?> items) => WriteListCore(items, depth: 0);

    /// <summary>Writes a map. Keys must not be null; each key is written as a string.</summary>
    /// <exception cref="PackStreamFormatException">
    /// An entry count exceeds <see cref="PackStreamLimits.MaxMapEntries"/>, a key is null, or the
    /// nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/>.
    /// </exception>
    public void WriteMap(IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries) =>
        WriteMapCore(entries, depth: 0);

    /// <summary>
    /// Writes a generic structure. Tags must be in the range 0-127 and a structure may carry
    /// at most 15 fields.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The tag is out of range, the field count exceeds 15, or the nesting depth exceeds
    /// <see cref="PackStreamLimits.MaxDepth"/>.
    /// </exception>
    public void WriteStructure(byte tag, IReadOnlyList<PackStreamValue?> fields) =>
        WriteStructureCore(tag, fields, depth: 0);

    /// <summary>Writes a Bolt graph Node structure (tag <c>4E</c>, four fields).</summary>
    public void WriteNode(PackStreamNode node) => WriteNodeCore(node, depth: 0);

    /// <summary>Writes a Bolt graph Relationship structure (tag <c>52</c>, eight fields).</summary>
    public void WriteRelationship(PackStreamRelationship relationship) =>
        WriteRelationshipCore(relationship, depth: 0);

    /// <summary>Writes a Bolt graph UnboundRelationship structure (tag <c>72</c>, four fields).</summary>
    public void WriteUnboundRelationship(PackStreamUnboundRelationship relationship) =>
        WriteUnboundRelationshipCore(relationship, depth: 0);

    /// <summary>Writes a Bolt Path structure (tag <c>50</c>, three fields).</summary>
    public void WritePath(PackStreamPath path) => WritePathCore(path, depth: 0);

    /// <summary>
    /// Writes any supported value recursively. A <see langword="null"/> value writes PackStream Null.
    /// </summary>
    public void WriteValue(PackStreamValue? value) => WriteValue(value, depth: 0);

    private void WriteValue(PackStreamValue? value, int depth)
    {
        switch (value)
        {
            case null:
                WriteNull();
                return;
            case PackStreamBoolean boolean:
                WriteBoolean(boolean.Value);
                return;
            case PackStreamInteger integer:
                WriteInteger(integer.Value);
                return;
            case PackStreamFloat floatValue:
                WriteFloat(floatValue.Value);
                return;
            case PackStreamString stringValue:
                WriteString(stringValue.Value);
                return;
            case PackStreamBytes bytes:
                WriteBytes(bytes.Value.Span);
                return;
            case PackStreamList list:
                WriteListCore(list.Items, depth);
                return;
            case PackStreamMap map:
                WriteMapCore(map.Entries, depth);
                return;
            case PackStreamStructure structure:
                WriteStructureCore(structure.Tag, structure.Fields, depth);
                return;
            case PackStreamNode node:
                WriteNodeCore(node, depth);
                return;
            case PackStreamRelationship relationship:
                WriteRelationshipCore(relationship, depth);
                return;
            case PackStreamUnboundRelationship unboundRelationship:
                WriteUnboundRelationshipCore(unboundRelationship, depth);
                return;
            case PackStreamPath path:
                WritePathCore(path, depth);
                return;
            default:
                throw new PackStreamFormatException(
                    $"Unsupported PackStream value type '{value.GetType().Name}'.");
        }
    }

    private void WriteListCore(IReadOnlyList<PackStreamValue?> items, int depth)
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

        WriteCollectionHeader(
            items.Count,
            PackStreamMarkers.TinyListBase,
            PackStreamMarkers.List8,
            PackStreamMarkers.List16,
            PackStreamMarkers.List32);

        for (var i = 0; i < items.Count; i++)
        {
            WriteValue(items[i], level);
        }
    }

    private void WriteMapCore(IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries, int depth)
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

        WriteCollectionHeader(
            entries.Count,
            PackStreamMarkers.TinyMapBase,
            PackStreamMarkers.Map8,
            PackStreamMarkers.Map16,
            PackStreamMarkers.Map32);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Key is null)
            {
                throw new PackStreamFormatException("PackStream map keys must not be null.");
            }

            WriteString(entry.Key);
            WriteValue(entry.Value, level);
        }
    }

    private void WriteStructureCore(byte tag, IReadOnlyList<PackStreamValue?> fields, int depth)
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

        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        WriteMarker((byte)(PackStreamMarkers.StructureBase | fields.Count));
        WriteMarker(tag);

        for (var i = 0; i < fields.Count; i++)
        {
            WriteValue(fields[i], level);
        }
    }

    private void WriteNodeCore(PackStreamNode node, int depth)
    {
        ArgumentNullException.ThrowIfNull(node);

        var level = EnterContainer(depth);

        WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.NodeFieldCount));
        WriteMarker(PackStreamMarkers.NodeTag);

        WriteInteger(node.Id);
        WriteStringList(node.Labels, level);
        WriteValue(node.Properties, level);
        WriteString(node.ElementId);
    }

    private void WriteRelationshipCore(PackStreamRelationship relationship, int depth)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        var level = EnterContainer(depth);

        WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.RelationshipFieldCount));
        WriteMarker(PackStreamMarkers.RelationshipTag);

        WriteInteger(relationship.Id);
        WriteInteger(relationship.StartNodeId);
        WriteInteger(relationship.EndNodeId);
        WriteString(relationship.Type);
        WriteValue(relationship.Properties, level);
        WriteString(relationship.ElementId);
        WriteString(relationship.StartNodeElementId);
        WriteString(relationship.EndNodeElementId);
    }

    private void WriteUnboundRelationshipCore(PackStreamUnboundRelationship relationship, int depth)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        var level = EnterContainer(depth);

        WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.UnboundRelationshipFieldCount));
        WriteMarker(PackStreamMarkers.UnboundRelationshipTag);

        WriteInteger(relationship.Id);
        WriteString(relationship.Type);
        WriteValue(relationship.Properties, level);
        WriteString(relationship.ElementId);
    }

    private void WritePathCore(PackStreamPath path, int depth)
    {
        ArgumentNullException.ThrowIfNull(path);

        // The path itself is one nesting level; each of its three field lists is one deeper,
        // and the nodes/relationships inside those lists are one deeper again.
        var level = EnterContainer(depth);
        var fieldListLevel = EnterContainer(level);

        WriteMarker((byte)(PackStreamMarkers.StructureBase | PackStreamMarkers.PathFieldCount));
        WriteMarker(PackStreamMarkers.PathTag);

        WriteCollectionHeader(
            path.Nodes.Count,
            PackStreamMarkers.TinyListBase,
            PackStreamMarkers.List8,
            PackStreamMarkers.List16,
            PackStreamMarkers.List32);

        for (var i = 0; i < path.Nodes.Count; i++)
        {
            WriteNodeCore(path.Nodes[i], fieldListLevel);
        }

        WriteCollectionHeader(
            path.UnboundRelationships.Count,
            PackStreamMarkers.TinyListBase,
            PackStreamMarkers.List8,
            PackStreamMarkers.List16,
            PackStreamMarkers.List32);

        for (var i = 0; i < path.UnboundRelationships.Count; i++)
        {
            WriteUnboundRelationshipCore(path.UnboundRelationships[i], fieldListLevel);
        }

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

    private void WriteStringList(IReadOnlyList<string> labels, int depth)
    {
        var level = EnterContainer(depth);

        if (labels.Count > _limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"Label count {labels.Count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        WriteCollectionHeader(
            labels.Count,
            PackStreamMarkers.TinyListBase,
            PackStreamMarkers.List8,
            PackStreamMarkers.List16,
            PackStreamMarkers.List32);

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            if (label is null)
            {
                throw new PackStreamFormatException("Node labels must not be null.");
            }

            WriteString(label);
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
