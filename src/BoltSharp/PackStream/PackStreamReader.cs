using System.Buffers.Binary;
using System.Text;

namespace BoltSharp.PackStream;

/// <summary>
/// Reads PackStream 1.0 values from a byte buffer. Supports Null, Boolean, signed Int64,
/// Float64, UTF-8 String and Byte arrays (B1-A/B1-B) plus recursive Lists, Maps and
/// Structures (B1-C). All multi-byte values are interpreted big-endian.
/// </summary>
public sealed class PackStreamReader
{
    private readonly ReadOnlyMemory<byte> _input;
    private readonly PackStreamLimits _limits;
    private int _position;
    private int _remainingElements;

    public PackStreamReader(ReadOnlyMemory<byte> input)
        : this(input, PackStreamLimits.Default)
    {
    }

    public PackStreamReader(ReadOnlyMemory<byte> input, PackStreamLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _input = input;
        _limits = limits;
        _remainingElements = limits.MaxTotalElements;
    }

    /// <summary>Number of bytes consumed so far.</summary>
    public int Position => _position;

    /// <summary>True while unread bytes remain.</summary>
    public bool HasMore => _position < _input.Length;

    /// <summary>Reads Null (<c>C0</c>); throws if the next marker is not Null.</summary>
    public void ReadNull()
    {
        var marker = ReadMarker();
        if (marker != PackStreamMarkers.Null)
        {
            throw new PackStreamFormatException(
                $"Expected the Null marker 0x{PackStreamMarkers.Null:X2} but found 0x{marker:X2}.");
        }
    }

    /// <summary>Reads a Boolean (<c>C2</c>/<c>C3</c>); throws for any other marker.</summary>
    public bool ReadBoolean() => ReadMarker() switch
    {
        PackStreamMarkers.True => true,
        PackStreamMarkers.False => false,
        var marker => throw new PackStreamFormatException(
            $"Expected a Boolean marker (0x{PackStreamMarkers.False:X2} or 0x{PackStreamMarkers.True:X2}) but found 0x{marker:X2}."),
    };

    /// <summary>
    /// Reads a signed 64-bit integer from a tiny, INT_8, INT_16, INT_32 or INT_64
    /// representation. Any valid wider representation is accepted.
    /// </summary>
    public long ReadInteger() => ReadIntegerValue(ReadMarker());

    /// <summary>Reads a Float64 (<c>C1</c> + 8 bytes); throws for any other marker.</summary>
    public double ReadFloat()
    {
        var marker = ReadMarker();
        if (marker != PackStreamMarkers.Float64)
        {
            throw new PackStreamFormatException(
                $"Expected the Float64 marker 0x{PackStreamMarkers.Float64:X2} but found 0x{marker:X2}.");
        }

        return ReadDoublePayload();
    }

    /// <summary>
    /// Reads a UTF-8 string from a TINY_STRING, STRING_8, STRING_16 or STRING_32
    /// representation. Any valid wider representation is accepted.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The marker is not a string representation, the declared length exceeds
    /// <see cref="PackStreamLimits.MaxStringByteLength"/>, or the payload is not valid UTF-8.
    /// </exception>
    /// <exception cref="PackStreamTruncatedException">The payload is incomplete.</exception>
    public string ReadString() => ReadStringValue(ReadMarker());

    /// <summary>
    /// Reads a byte array from a BYTES_8, BYTES_16 or BYTES_32 representation and
    /// returns a new array that the caller owns (a defensive copy; the returned array
    /// never aliases the input buffer). There is no tiny byte-array representation.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The marker is not a byte-array representation, or the declared length exceeds
    /// <see cref="PackStreamLimits.MaxBytesLength"/>.
    /// </exception>
    /// <exception cref="PackStreamTruncatedException">The payload is incomplete.</exception>
    public byte[] ReadBytes() => ReadBytesSegment(ReadMarker()).ToArray();

    /// <summary>
    /// Reads and returns one complete value tree, recursively decoding lists, maps and
    /// structures. A PackStream Null is returned as <see langword="null"/>.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// A marker is invalid, a container declared length exceeds its configured limit, the
    /// nesting depth exceeds <see cref="PackStreamLimits.MaxDepth"/>, a map key is not a
    /// string, a structure tag is out of range, or the total element budget is exhausted.
    /// </exception>
    /// <exception cref="PackStreamTruncatedException">The value is incomplete.</exception>
    public PackStreamValue? ReadValue()
    {
        _remainingElements = _limits.MaxTotalElements;
        return ReadValue(depth: 0);
    }

    private PackStreamValue? ReadValue(int depth)
    {
        ConsumeElementBudget();

        var marker = ReadMarker();

        if (PackStreamMarkers.IsTinyInteger(marker))
        {
            return new PackStreamInteger(DecodeTinyInteger(marker));
        }

        if (PackStreamMarkers.IsTinyString(marker))
        {
            return new PackStreamString(ReadStringValue(marker));
        }

        if (PackStreamMarkers.IsTinyList(marker))
        {
            return ReadListValue(marker, depth);
        }

        if (PackStreamMarkers.IsTinyMap(marker))
        {
            return ReadMapValue(marker, depth);
        }

        if (PackStreamMarkers.IsStructure(marker))
        {
            return ReadStructureValue(marker, depth);
        }

        return marker switch
        {
            PackStreamMarkers.Null => null,
            PackStreamMarkers.True => new PackStreamBoolean(true),
            PackStreamMarkers.False => new PackStreamBoolean(false),
            PackStreamMarkers.Float64 => new PackStreamFloat(ReadDoublePayload()),
            PackStreamMarkers.Int8 => new PackStreamInteger(unchecked((sbyte)ReadByte())),
            PackStreamMarkers.Int16 => new PackStreamInteger(ReadInt16()),
            PackStreamMarkers.Int32 => new PackStreamInteger(ReadInt32()),
            PackStreamMarkers.Int64 => new PackStreamInteger(ReadInt64()),
            PackStreamMarkers.Bytes8 or PackStreamMarkers.Bytes16 or PackStreamMarkers.Bytes32 =>
                new PackStreamBytes(ReadBytesSegment(marker)),
            PackStreamMarkers.String8 or PackStreamMarkers.String16 or PackStreamMarkers.String32 =>
                new PackStreamString(ReadStringValue(marker)),
            PackStreamMarkers.List8 or PackStreamMarkers.List16 or PackStreamMarkers.List32 =>
                ReadListValue(marker, depth),
            PackStreamMarkers.Map8 or PackStreamMarkers.Map16 or PackStreamMarkers.Map32 =>
                ReadMapValue(marker, depth),
            _ => throw new PackStreamFormatException(
                $"Marker 0x{marker:X2} is not a valid PackStream value."),
        };
    }

    private PackStreamList ReadListValue(byte marker, int depth)
    {
        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        uint count;
        if (PackStreamMarkers.IsTinyList(marker))
        {
            count = (uint)(marker & 0x0F);
        }
        else
        {
            count = marker switch
            {
                PackStreamMarkers.List8 => ReadByte(),
                PackStreamMarkers.List16 => ReadUInt16(),
                PackStreamMarkers.List32 => ReadUInt32(),
                _ => throw new PackStreamFormatException(
                    $"Marker 0x{marker:X2} is not a valid PackStream list representation."),
            };
        }

        if (count > (uint)_limits.MaxListItems)
        {
            throw new PackStreamFormatException(
                $"Declared list item count {count} exceeds the configured maximum of {_limits.MaxListItems}.");
        }

        var items = new List<PackStreamValue?>();
        for (uint i = 0; i < count; i++)
        {
            items.Add(ReadValue(level));
        }

        return new PackStreamList(items);
    }

    private PackStreamMap ReadMapValue(byte marker, int depth)
    {
        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        uint count;
        if (PackStreamMarkers.IsTinyMap(marker))
        {
            count = (uint)(marker & 0x0F);
        }
        else
        {
            count = marker switch
            {
                PackStreamMarkers.Map8 => ReadByte(),
                PackStreamMarkers.Map16 => ReadUInt16(),
                PackStreamMarkers.Map32 => ReadUInt32(),
                _ => throw new PackStreamFormatException(
                    $"Marker 0x{marker:X2} is not a valid PackStream map representation."),
            };
        }

        if (count > (uint)_limits.MaxMapEntries)
        {
            throw new PackStreamFormatException(
                $"Declared map entry count {count} exceeds the configured maximum of {_limits.MaxMapEntries}.");
        }

        var entries = new List<KeyValuePair<string, PackStreamValue?>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        for (uint i = 0; i < count; i++)
        {
            if (ReadValue(level) is not PackStreamString key)
            {
                throw new PackStreamFormatException("PackStream map keys must be strings.");
            }

            var value = ReadValue(level);

            if (index.TryGetValue(key.Value, out var existing))
            {
                // Duplicate key: the last value wins, keeping the first-seen position.
                entries[existing] = new KeyValuePair<string, PackStreamValue?>(key.Value, value);
            }
            else
            {
                index[key.Value] = entries.Count;
                entries.Add(new KeyValuePair<string, PackStreamValue?>(key.Value, value));
            }
        }

        return new PackStreamMap(entries);
    }

    private PackStreamValue ReadStructureValue(byte marker, int depth)
    {
        var level = depth + 1;
        if (level > _limits.MaxDepth)
        {
            throw new PackStreamFormatException(
                $"Nesting depth {level} exceeds the configured maximum of {_limits.MaxDepth}.");
        }

        // The field count is the low nibble, so 0..15; there is no extended structure form.
        var count = marker & 0x0F;

        var tag = ReadByte();
        if (tag > PackStreamMarkers.StructureTagMax)
        {
            throw new PackStreamFormatException(
                $"Structure tag {tag} is outside the permitted range 0-{PackStreamMarkers.StructureTagMax}.");
        }

        return tag switch
        {
            PackStreamMarkers.NodeTag => ReadNode(level, count),
            PackStreamMarkers.RelationshipTag => ReadRelationship(level, count),
            PackStreamMarkers.UnboundRelationshipTag => ReadUnboundRelationship(level, count),
            PackStreamMarkers.PathTag => ReadPath(level, count),
            _ => ReadGenericStructure(level, tag, count),
        };
    }

    private PackStreamStructure ReadGenericStructure(int level, byte tag, int count)
    {
        var fields = new List<PackStreamValue?>(count);
        for (var i = 0; i < count; i++)
        {
            fields.Add(ReadValue(level));
        }

        return new PackStreamStructure(tag, fields);
    }

    private PackStreamNode ReadNode(int level, int count)
    {
        RequireFieldCount("Node", count, PackStreamMarkers.NodeFieldCount);

        var id = RequireInteger(ReadValue(level), "Node.id");
        var labels = RequireStringList(ReadValue(level), "Node.labels");
        var properties = RequireMap(ReadValue(level), "Node.properties");
        var elementId = RequireString(ReadValue(level), "Node.element_id");

        return new PackStreamNode(id, labels, properties, elementId);
    }

    private PackStreamRelationship ReadRelationship(int level, int count)
    {
        RequireFieldCount("Relationship", count, PackStreamMarkers.RelationshipFieldCount);

        var id = RequireInteger(ReadValue(level), "Relationship.id");
        var startNodeId = RequireInteger(ReadValue(level), "Relationship.startNodeId");
        var endNodeId = RequireInteger(ReadValue(level), "Relationship.endNodeId");
        var type = RequireString(ReadValue(level), "Relationship.type");
        var properties = RequireMap(ReadValue(level), "Relationship.properties");
        var elementId = RequireString(ReadValue(level), "Relationship.element_id");
        var startNodeElementId = RequireString(ReadValue(level), "Relationship.start_node_element_id");
        var endNodeElementId = RequireString(ReadValue(level), "Relationship.end_node_element_id");

        return new PackStreamRelationship(
            id, startNodeId, endNodeId, type, properties, elementId, startNodeElementId, endNodeElementId);
    }

    private PackStreamUnboundRelationship ReadUnboundRelationship(int level, int count)
    {
        RequireFieldCount("UnboundRelationship", count, PackStreamMarkers.UnboundRelationshipFieldCount);

        var id = RequireInteger(ReadValue(level), "UnboundRelationship.id");
        var type = RequireString(ReadValue(level), "UnboundRelationship.type");
        var properties = RequireMap(ReadValue(level), "UnboundRelationship.properties");
        var elementId = RequireString(ReadValue(level), "UnboundRelationship.element_id");

        return new PackStreamUnboundRelationship(id, type, properties, elementId);
    }

    private PackStreamPath ReadPath(int level, int count)
    {
        RequireFieldCount("Path", count, PackStreamMarkers.PathFieldCount);

        if (ReadValue(level) is not PackStreamList nodesList)
        {
            throw new PackStreamFormatException("Path.nodes must be a list.");
        }

        var nodes = ReadPathNodes(nodesList);
        if (nodes.Length == 0)
        {
            throw new PackStreamFormatException("A Path must contain at least one node.");
        }

        if (ReadValue(level) is not PackStreamList relationshipsList)
        {
            throw new PackStreamFormatException("Path.unboundRelationships must be a list.");
        }

        var relationships = ReadPathRelationships(relationshipsList);

        if (ReadValue(level) is not PackStreamList indicesList)
        {
            throw new PackStreamFormatException("Path.indices must be a list.");
        }

        var indices = ReadPathIndices(indicesList);
        ValidatePathTraversal(nodes.Length, relationships.Length, indices);

        return new PackStreamPath(nodes, relationships, indices);
    }

    private static PackStreamNode[] ReadPathNodes(PackStreamList nodes)
    {
        var result = new PackStreamNode[nodes.Items.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = nodes.Items[i] as PackStreamNode
                ?? throw new PackStreamFormatException($"Path.nodes[{i}] must be a Node structure.");
        }

        return result;
    }

    private static PackStreamUnboundRelationship[] ReadPathRelationships(PackStreamList relationships)
    {
        var result = new PackStreamUnboundRelationship[relationships.Items.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = relationships.Items[i] as PackStreamUnboundRelationship
                ?? throw new PackStreamFormatException(
                    $"Path.unboundRelationships[{i}] must be an UnboundRelationship structure.");
        }

        return result;
    }

    private static long[] ReadPathIndices(PackStreamList indices)
    {
        var result = new long[indices.Items.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = indices.Items[i] is PackStreamInteger integer
                ? integer.Value
                : throw new PackStreamFormatException($"Path.indices[{i}] must be an integer.");
        }

        return result;
    }

    private static void ValidatePathTraversal(int nodeCount, int relationshipCount, long[] indices)
    {
        if ((indices.Length & 1) != 0)
        {
            throw new PackStreamFormatException(
                $"A Path must contain an even number of indices but {indices.Length} were found.");
        }

        for (var i = 0; i < indices.Length; i += 2)
        {
            var relationshipIndex = indices[i];
            var nodeIndex = indices[i + 1];

            // Relationship indexes are 1-based; negative values mean reverse traversal and
            // zero is invalid. Only comparisons are used, so long.MinValue cannot overflow.
            if (relationshipIndex > 0)
            {
                if (relationshipIndex > relationshipCount)
                {
                    throw new PackStreamFormatException(
                        $"Path relationship index {relationshipIndex} is outside the relationship list of {relationshipCount}.");
                }
            }
            else if (relationshipIndex < 0)
            {
                if (relationshipIndex < -relationshipCount)
                {
                    throw new PackStreamFormatException(
                        $"Path relationship index {relationshipIndex} is outside the relationship list of {relationshipCount}.");
                }
            }
            else
            {
                throw new PackStreamFormatException("Path relationship index zero is invalid.");
            }

            // Node indexes are 0-based and nonnegative.
            if (nodeIndex < 0 || nodeIndex >= nodeCount)
            {
                throw new PackStreamFormatException(
                    $"Path node index {nodeIndex} is outside the node list of {nodeCount}.");
            }
        }
    }

    private static void RequireFieldCount(string structure, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new PackStreamFormatException(
                $"A {structure} structure must have {expected} fields but the marker declares {actual}.");
        }
    }

    private static long RequireInteger(PackStreamValue? value, string field) =>
        value is PackStreamInteger integer
            ? integer.Value
            : throw new PackStreamFormatException($"{field} must be a signed integer.");

    private static string RequireString(PackStreamValue? value, string field) =>
        value is PackStreamString str
            ? str.Value
            : throw new PackStreamFormatException($"{field} must be a string.");

    private static PackStreamMap RequireMap(PackStreamValue? value, string field) =>
        value as PackStreamMap
            ?? throw new PackStreamFormatException($"{field} must be a map.");

    private static IReadOnlyList<string> RequireStringList(PackStreamValue? value, string field)
    {
        if (value is not PackStreamList list)
        {
            throw new PackStreamFormatException($"{field} must be a list.");
        }

        var result = new string[list.Items.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = list.Items[i] is PackStreamString item
                ? item.Value
                : throw new PackStreamFormatException($"{field}[{i}] must be a string.");
        }

        return result;
    }

    private string ReadStringValue(byte marker)
    {
        uint length;
        if (PackStreamMarkers.IsTinyString(marker))
        {
            length = (uint)(marker & 0x0F);
        }
        else
        {
            length = marker switch
            {
                PackStreamMarkers.String8 => ReadByte(),
                PackStreamMarkers.String16 => ReadUInt16(),
                PackStreamMarkers.String32 => ReadUInt32(),
                _ => throw new PackStreamFormatException(
                    $"Marker 0x{marker:X2} is not a valid PackStream string representation."),
            };
        }

        if (length > (uint)_limits.MaxStringByteLength)
        {
            throw new PackStreamFormatException(
                $"Declared string length {length} exceeds the configured maximum of {_limits.MaxStringByteLength} byte(s).");
        }

        EnsureRemaining((int)length);
        var payload = _input.Span.Slice(_position, (int)length);

        string value;
        try
        {
            value = StrictUtf8.Instance.GetString(payload);
        }
        catch (DecoderFallbackException ex)
        {
            throw new PackStreamFormatException("The string contained malformed UTF-8 data.", ex);
        }

        _position += (int)length;
        return value;
    }

    private ReadOnlyMemory<byte> ReadBytesSegment(byte marker)
    {
        uint length = marker switch
        {
            PackStreamMarkers.Bytes8 => ReadByte(),
            PackStreamMarkers.Bytes16 => ReadUInt16(),
            PackStreamMarkers.Bytes32 => ReadUInt32(),
            _ => throw new PackStreamFormatException(
                $"Marker 0x{marker:X2} is not a valid PackStream byte-array representation."),
        };

        if (length > (uint)_limits.MaxBytesLength)
        {
            throw new PackStreamFormatException(
                $"Declared byte-array length {length} exceeds the configured maximum of {_limits.MaxBytesLength} byte(s).");
        }

        EnsureRemaining((int)length);
        var segment = _input.Slice(_position, (int)length);
        _position += (int)length;
        return segment;
    }

    private long ReadIntegerValue(byte marker)
    {
        if (PackStreamMarkers.IsTinyInteger(marker))
        {
            return DecodeTinyInteger(marker);
        }

        return marker switch
        {
            PackStreamMarkers.Int8 => unchecked((sbyte)ReadByte()),
            PackStreamMarkers.Int16 => ReadInt16(),
            PackStreamMarkers.Int32 => ReadInt32(),
            PackStreamMarkers.Int64 => ReadInt64(),
            _ => throw new PackStreamFormatException(
                $"Marker 0x{marker:X2} is not a valid PackStream integer representation."),
        };
    }

    private double ReadDoublePayload()
    {
        EnsureRemaining(sizeof(double));
        var value = BinaryPrimitives.ReadDoubleBigEndian(_input.Span[_position..]);
        _position += sizeof(double);
        return value;
    }

    private void ConsumeElementBudget()
    {
        if (_remainingElements <= 0)
        {
            throw new PackStreamFormatException(
                $"The decoded value exceeds the configured element budget of {_limits.MaxTotalElements}.");
        }

        _remainingElements--;
    }

    private static long DecodeTinyInteger(byte marker) =>
        marker <= PackStreamMarkers.TinyIntegerMax ? marker : unchecked((sbyte)marker);

    private byte ReadMarker()
    {
        EnsureRemaining(1);
        return _input.Span[_position++];
    }

    private byte ReadByte()
    {
        EnsureRemaining(1);
        return _input.Span[_position++];
    }

    private ushort ReadUInt16()
    {
        EnsureRemaining(sizeof(ushort));
        var value = BinaryPrimitives.ReadUInt16BigEndian(_input.Span[_position..]);
        _position += sizeof(ushort);
        return value;
    }

    private uint ReadUInt32()
    {
        EnsureRemaining(sizeof(uint));
        var value = BinaryPrimitives.ReadUInt32BigEndian(_input.Span[_position..]);
        _position += sizeof(uint);
        return value;
    }

    private short ReadInt16()
    {
        EnsureRemaining(sizeof(short));
        var value = BinaryPrimitives.ReadInt16BigEndian(_input.Span[_position..]);
        _position += sizeof(short);
        return value;
    }

    private int ReadInt32()
    {
        EnsureRemaining(sizeof(int));
        var value = BinaryPrimitives.ReadInt32BigEndian(_input.Span[_position..]);
        _position += sizeof(int);
        return value;
    }

    private long ReadInt64()
    {
        EnsureRemaining(sizeof(long));
        var value = BinaryPrimitives.ReadInt64BigEndian(_input.Span[_position..]);
        _position += sizeof(long);
        return value;
    }

    private void EnsureRemaining(int count)
    {
        var remaining = _input.Length - _position;
        if (remaining < count)
        {
            throw new PackStreamTruncatedException(
                $"Unexpected end of PackStream input: needed {count} byte(s) but only {remaining} remained.");
        }
    }
}
