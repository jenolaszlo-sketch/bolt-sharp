namespace BoltSharp.PackStream;

/// <summary>
/// Safety limits applied by the PackStream codec.
/// <para>
/// The wire format permits string/byte-array lengths up to 2,147,483,647 and container
/// counts up to the same value, but allocating from an untrusted declared length or count
/// is unsafe, so the codec applies lower, configurable maxima. Defaults: 16 MiB per string
/// or byte array, nesting depth 100, 100,000 items/entries per list/map, and a total of
/// 1,000,000 decoded values per top-level <c>ReadValue</c> call.
/// </para>
/// </summary>
public sealed class PackStreamLimits
{
    /// <summary>The default maximum byte length applied to strings and byte arrays (16 MiB).</summary>
    public const int DefaultMaxLength = 16 * 1024 * 1024;

    /// <summary>The default maximum nesting depth of containers.</summary>
    public const int DefaultMaxDepth = 100;

    /// <summary>The default maximum number of items in a single list.</summary>
    public const int DefaultMaxListItems = 100_000;

    /// <summary>The default maximum number of entries in a single map.</summary>
    public const int DefaultMaxMapEntries = 100_000;

    /// <summary>The default maximum number of values decoded by one top-level read.</summary>
    public const int DefaultMaxTotalElements = 1_000_000;

    /// <summary>A shared instance using the default limits.</summary>
    public static PackStreamLimits Default { get; } = new();

    public PackStreamLimits(
        int maxStringByteLength = DefaultMaxLength,
        int maxBytesLength = DefaultMaxLength,
        int maxDepth = DefaultMaxDepth,
        int maxListItems = DefaultMaxListItems,
        int maxMapEntries = DefaultMaxMapEntries,
        int maxTotalElements = DefaultMaxTotalElements)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxStringByteLength);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytesLength);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxListItems);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMapEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalElements);

        MaxStringByteLength = maxStringByteLength;
        MaxBytesLength = maxBytesLength;
        MaxDepth = maxDepth;
        MaxListItems = maxListItems;
        MaxMapEntries = maxMapEntries;
        MaxTotalElements = maxTotalElements;
    }

    /// <summary>Maximum permitted UTF-8 byte length of a string.</summary>
    public int MaxStringByteLength { get; }

    /// <summary>Maximum permitted length of a byte array.</summary>
    public int MaxBytesLength { get; }

    /// <summary>Maximum permitted nesting depth of lists, maps and structures.</summary>
    public int MaxDepth { get; }

    /// <summary>Maximum permitted item count of a single list.</summary>
    public int MaxListItems { get; }

    /// <summary>Maximum permitted entry count of a single map.</summary>
    public int MaxMapEntries { get; }

    /// <summary>Maximum number of values decoded by one top-level <c>ReadValue</c> call.</summary>
    public int MaxTotalElements { get; }
}
