using System.Buffers;
using System.Buffers.Binary;

namespace BoltSharp.PackStream;

/// <summary>
/// Writes PackStream 1.0 scalar values to an <see cref="IBufferWriter{T}"/>.
/// B1-A supports Null, Boolean, signed Int64 and Float64 only. All multi-byte
/// values are written big-endian.
/// </summary>
public sealed class PackStreamWriter
{
    private readonly IBufferWriter<byte> _output;

    public PackStreamWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
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

    private void WriteMarker(byte marker)
    {
        var span = _output.GetSpan(1);
        span[0] = marker;
        _output.Advance(1);
    }
}
