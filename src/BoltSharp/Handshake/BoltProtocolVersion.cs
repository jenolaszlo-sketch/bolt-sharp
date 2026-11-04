using System.Buffers.Binary;
using System.Globalization;

namespace BoltSharp.Handshake;

/// <summary>
/// A concrete Bolt protocol version, a <c>major</c>/<c>minor</c> pair such as <c>5.8</c>.
/// <para>
/// <b>Wire encoding (Bolt version 4.0 style).</b> A concrete version is a big-endian 32-bit
/// value whose first 16 bits are reserved (zero), whose next 8 bits are the minor version and
/// whose last 8 bits are the major version. For example <c>4.1</c> is <c>00 00 01 04</c> and
/// <c>5.8</c> is <c>00 00 08 05</c>. <see cref="ToUInt32"/> therefore produces
/// <c>(minor &lt;&lt; 8) | major</c>.
/// </para>
/// <para>
/// <see cref="FromUInt32"/> rejects a value that sets the reserved 16 bits, so a
/// <see cref="BoltVersionRange"/> encoding or the manifest marker cannot be interpreted
/// silently as a concrete version.
/// </para>
/// </summary>
public readonly struct BoltProtocolVersion : IEquatable<BoltProtocolVersion>, IComparable<BoltProtocolVersion>
{
    public BoltProtocolVersion(int major, int minor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 255);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minor, 255);

        Major = major;
        Minor = minor;
    }

    /// <summary>The major version (0-255).</summary>
    public int Major { get; }

    /// <summary>The minor version (0-255).</summary>
    public int Minor { get; }

    /// <summary>
    /// The Bolt version 4.0 style 32-bit wire value (<c>(minor &lt;&lt; 8) | major</c>).
    /// </summary>
    public uint ToUInt32() => (uint)((Minor << 8) | Major);

    /// <summary>Writes the four big-endian bytes of the Bolt version 4.0 style value.</summary>
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 4)
        {
            throw new ArgumentException("At least four bytes are required.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, ToUInt32());
    }

    /// <summary>
    /// Decodes a Bolt version 4.0 style value. The reserved high 16 bits must be zero; a value
    /// that encodes a minor-version range is rejected with
    /// <see cref="BoltHandshakeFormatException"/>.
    /// </summary>
    public static BoltProtocolVersion FromUInt32(uint value)
    {
        if ((value & 0xFFFF0000u) != 0)
        {
            throw new BoltHandshakeFormatException(
                $"The handshake value 0x{value:X8} is not a concrete Bolt version: the reserved " +
                "high 16 bits must be zero (a minor-version range is not permitted here).");
        }

        return new BoltProtocolVersion((int)(value & 0xFF), (int)((value >> 8) & 0xFF));
    }

    public bool Equals(BoltProtocolVersion other) => Major == other.Major && Minor == other.Minor;

    public override bool Equals(object? obj) => obj is BoltProtocolVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor);

    public int CompareTo(BoltProtocolVersion other)
    {
        var major = Major.CompareTo(other.Major);
        return major != 0 ? major : Minor.CompareTo(other.Minor);
    }

    public static bool operator ==(BoltProtocolVersion left, BoltProtocolVersion right) => left.Equals(right);

    public static bool operator !=(BoltProtocolVersion left, BoltProtocolVersion right) => !left.Equals(right);

    public static bool operator <(BoltProtocolVersion left, BoltProtocolVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(BoltProtocolVersion left, BoltProtocolVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(BoltProtocolVersion left, BoltProtocolVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(BoltProtocolVersion left, BoltProtocolVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");
}
