using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.PackStream;

/// <summary>
/// Writes PackStream 1.0 values to an <see cref="IBufferWriter{T}"/>. Supports Null,
/// Boolean, signed Int64, Float64, UTF-8 String and Byte arrays (B1-A/B1-B). All
/// multi-byte values are written big-endian.
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
