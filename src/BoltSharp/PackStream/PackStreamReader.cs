using System.Buffers.Binary;

namespace BoltSharp.PackStream;

/// <summary>
/// Reads PackStream 1.0 scalar values from a byte buffer.
/// B1-A supports Null, Boolean, signed Int64 and Float64 only. All multi-byte
/// values are interpreted big-endian.
/// </summary>
public sealed class PackStreamReader
{
    private readonly ReadOnlyMemory<byte> _input;
    private int _position;

    public PackStreamReader(ReadOnlyMemory<byte> input)
    {
        _input = input;
        _position = 0;
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
    public long ReadInteger()
    {
        var marker = ReadMarker();

        return marker switch
        {
            PackStreamMarkers.Int8 => unchecked((sbyte)ReadByte()),
            PackStreamMarkers.Int16 => ReadInt16(),
            PackStreamMarkers.Int32 => ReadInt32(),
            PackStreamMarkers.Int64 => ReadInt64(),
            _ when PackStreamMarkers.IsTinyInteger(marker) => DecodeTinyInteger(marker),
            _ => throw new PackStreamFormatException(
                $"Marker 0x{marker:X2} is not a valid PackStream integer representation."),
        };
    }

    /// <summary>Reads a Float64 (<c>C1</c> + 8 bytes); throws for any other marker.</summary>
    public double ReadFloat()
    {
        var marker = ReadMarker();
        if (marker != PackStreamMarkers.Float64)
        {
            throw new PackStreamFormatException(
                $"Expected the Float64 marker 0x{PackStreamMarkers.Float64:X2} but found 0x{marker:X2}.");
        }

        EnsureRemaining(sizeof(double));
        var value = BinaryPrimitives.ReadDoubleBigEndian(_input.Span[_position..]);
        _position += sizeof(double);
        return value;
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
