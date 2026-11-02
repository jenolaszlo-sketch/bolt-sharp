using System.Buffers.Binary;
using System.Text;

namespace BoltSharp.PackStream;

/// <summary>
/// Reads PackStream 1.0 values from a byte buffer. Supports Null, Boolean, signed Int64,
/// Float64, UTF-8 String and Byte arrays (B1-A/B1-B). All multi-byte values are
/// interpreted big-endian.
/// </summary>
public sealed class PackStreamReader
{
    private readonly ReadOnlyMemory<byte> _input;
    private readonly PackStreamLimits _limits;
    private int _position;

    public PackStreamReader(ReadOnlyMemory<byte> input)
        : this(input, PackStreamLimits.Default)
    {
    }

    public PackStreamReader(ReadOnlyMemory<byte> input, PackStreamLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _input = input;
        _limits = limits;
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

    /// <summary>
    /// Reads a UTF-8 string from a TINY_STRING, STRING_8, STRING_16 or STRING_32
    /// representation. Any valid wider representation is accepted.
    /// </summary>
    /// <exception cref="PackStreamFormatException">
    /// The marker is not a string representation, the declared length exceeds
    /// <see cref="PackStreamLimits.MaxStringByteLength"/>, or the payload is not valid UTF-8.
    /// </exception>
    /// <exception cref="PackStreamTruncatedException">The payload is incomplete.</exception>
    public string ReadString()
    {
        var marker = ReadMarker();

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
    public byte[] ReadBytes()
    {
        var marker = ReadMarker();

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
        var result = _input.Span.Slice(_position, (int)length).ToArray();
        _position += (int)length;
        return result;
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
