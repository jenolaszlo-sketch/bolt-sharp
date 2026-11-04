using System.Buffers.Binary;
using System.Globalization;

namespace BoltSharp.Handshake;

/// <summary>
/// A contiguous range of minor versions within one major version, using the Bolt version 4.3
/// style encoding.
/// <para>
/// <b>Wire encoding.</b> A big-endian 32-bit value whose first 8 bits are reserved (zero), whose
/// next 8 bits are the number of consecutive <i>previous</i> minor versions (<see cref="Range"/>),
/// whose next 8 bits are the highest minor version (<see cref="Minor"/>) and whose last 8 bits
/// are the major version (<see cref="Major"/>). For example <c>00 02 08 05</c> is Bolt
/// <c>5.8</c> down to <c>5.6</c>. A <see cref="Range"/> of zero encodes a single concrete
/// version, which is how a Bolt version 4.0 style proposal (<c>00 00 01 04</c>) decodes.
/// </para>
/// <para>
/// A range cannot span multiple major versions, so <see cref="Range"/> must not exceed
/// <see cref="Minor"/>. <see cref="FromUInt32"/> enforces this and the reserved byte, raising
/// <see cref="BoltHandshakeFormatException"/> for malformed input.
/// </para>
/// </summary>
public readonly struct BoltVersionRange : IEquatable<BoltVersionRange>
{
    public BoltVersionRange(int major, int minor, int range)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(major, 255);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minor, 255);
        ArgumentOutOfRangeException.ThrowIfNegative(range);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(range, 255);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(range, minor);

        Major = major;
        Minor = minor;
        Range = range;
    }

    /// <summary>The major version (0-255).</summary>
    public int Major { get; }

    /// <summary>The highest minor version covered by the range (0-255).</summary>
    public int Minor { get; }

    /// <summary>The number of consecutive minor versions below <see cref="Minor"/> (0-255).</summary>
    public int Range { get; }

    /// <summary>The lowest minor version covered by the range.</summary>
    public int MinMinor => Minor - Range;

    /// <summary>The highest concrete version covered by the range.</summary>
    public BoltProtocolVersion Max => new(Major, Minor);

    /// <summary>The lowest concrete version covered by the range.</summary>
    public BoltProtocolVersion Min => new(Major, MinMinor);

    /// <summary>The Bolt version 4.3 style 32-bit wire value.</summary>
    public uint ToUInt32() => (uint)((Range << 16) | (Minor << 8) | Major);

    /// <summary>Writes the four big-endian bytes of the Bolt version 4.3 style value.</summary>
    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < 4)
        {
            throw new ArgumentException("At least four bytes are required.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, ToUInt32());
    }

    /// <summary>
    /// Decodes a Bolt version 4.3 style value (which also covers the version 4.0 style, whose
    /// range field is zero). The reserved byte and the major-spanning constraint are enforced.
    /// </summary>
    public static BoltVersionRange FromUInt32(uint value)
    {
        var reserved = (value >> 24) & 0xFF;
        var range = (int)((value >> 16) & 0xFF);
        var minor = (int)((value >> 8) & 0xFF);
        var major = (int)(value & 0xFF);

        if (reserved != 0)
        {
            throw new BoltHandshakeFormatException(
                $"The handshake value 0x{value:X8} sets the reserved first byte 0x{reserved:X2}; " +
                "only the Bolt version 4.0 and 4.3 encodings are recognized.");
        }

        if (range > minor)
        {
            throw new BoltHandshakeFormatException(
                $"The handshake value 0x{value:X8} declares a range of {range} minor version(s) " +
                $"below {major}.{minor}, which would span a major version.");
        }

        return new BoltVersionRange(major, minor, range);
    }

    /// <summary>The concrete versions covered by the range, highest minor first.</summary>
    public IReadOnlyList<BoltProtocolVersion> Expand()
    {
        var versions = new BoltProtocolVersion[Range + 1];
        for (var i = 0; i <= Range; i++)
        {
            versions[i] = new BoltProtocolVersion(Major, Minor - i);
        }

        return versions;
    }

    /// <summary>Whether the range covers the given concrete version.</summary>
    public bool Contains(BoltProtocolVersion version) =>
        version.Major == Major && version.Minor >= MinMinor && version.Minor <= Minor;

    /// <summary>
    /// Groups concrete versions into the smallest deterministic set of contiguous ranges, one
    /// per major version, ordered by major descending then by minor descending. The input is
    /// de-duplicated; an empty input yields an empty list.
    /// </summary>
    public static IReadOnlyList<BoltVersionRange> FromVersions(IEnumerable<BoltProtocolVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var distinct = new HashSet<BoltProtocolVersion>();
        foreach (var version in versions)
        {
            distinct.Add(version);
        }

        if (distinct.Count == 0)
        {
            return Array.Empty<BoltVersionRange>();
        }

        var majors = new List<int>();
        foreach (var version in distinct)
        {
            if (!majors.Contains(version.Major))
            {
                majors.Add(version.Major);
            }
        }

        majors.Sort();
        majors.Reverse();

        var result = new List<BoltVersionRange>();
        foreach (var major in majors)
        {
            var minors = new List<int>();
            foreach (var version in distinct)
            {
                if (version.Major == major)
                {
                    minors.Add(version.Minor);
                }
            }

            minors.Sort();
            minors.Reverse();

            var index = 0;
            while (index < minors.Count)
            {
                var high = minors[index];
                var end = index;
                while (end + 1 < minors.Count && minors[end + 1] == minors[end] - 1)
                {
                    end++;
                }

                var low = minors[end];
                result.Add(new BoltVersionRange(major, high, high - low));
                index = end + 1;
            }
        }

        return result.AsReadOnly();
    }

    public bool Equals(BoltVersionRange other) =>
        Major == other.Major && Minor == other.Minor && Range == other.Range;

    public override bool Equals(object? obj) => obj is BoltVersionRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Range);

    public static bool operator ==(BoltVersionRange left, BoltVersionRange right) => left.Equals(right);

    public static bool operator !=(BoltVersionRange left, BoltVersionRange right) => !left.Equals(right);

    public override string ToString() =>
        Range == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}-{Major}.{MinMinor}");
}
