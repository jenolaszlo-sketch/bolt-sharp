namespace BoltSharp.PackStream;

/// <summary>
/// Safety limits applied to PackStream strings and byte arrays.
/// <para>
/// The wire format permits lengths up to 2,147,483,647 bytes (a signed 32-bit value),
/// but allocating from an untrusted declared length is unsafe, so the codec applies a
/// lower, configurable maximum. The default is 16 MiB (16,777,216 bytes) for both
/// <see cref="MaxStringByteLength"/> and <see cref="MaxBytesLength"/>.
/// </para>
/// </summary>
public sealed class PackStreamLimits
{
    /// <summary>The default maximum length applied when no limits are supplied (16 MiB).</summary>
    public const int DefaultMaxLength = 16 * 1024 * 1024;

    /// <summary>A shared instance using <see cref="DefaultMaxLength"/> for both limits.</summary>
    public static PackStreamLimits Default { get; } = new();

    public PackStreamLimits(int maxStringByteLength = DefaultMaxLength, int maxBytesLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxStringByteLength);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytesLength);

        MaxStringByteLength = maxStringByteLength;
        MaxBytesLength = maxBytesLength;
    }

    /// <summary>Maximum permitted UTF-8 byte length of a string.</summary>
    public int MaxStringByteLength { get; }

    /// <summary>Maximum permitted length of a byte array.</summary>
    public int MaxBytesLength { get; }
}
