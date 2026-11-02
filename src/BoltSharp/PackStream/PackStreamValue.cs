namespace BoltSharp.PackStream;

/// <summary>
/// Base type for a decoded PackStream value. A PackStream Null is represented by a C#
/// <see langword="null"/> reference rather than by a dedicated type. Scalar wrappers are
/// immutable; containers hold defensive copies of their elements.
/// </summary>
public abstract class PackStreamValue
{
}

/// <summary>A PackStream Boolean.</summary>
public sealed class PackStreamBoolean : PackStreamValue
{
    public PackStreamBoolean(bool value) => Value = value;

    public bool Value { get; }
}

/// <summary>A PackStream signed 64-bit integer.</summary>
public sealed class PackStreamInteger : PackStreamValue
{
    public PackStreamInteger(long value) => Value = value;

    public long Value { get; }
}

/// <summary>A PackStream IEEE 754 double-precision float.</summary>
public sealed class PackStreamFloat : PackStreamValue
{
    public PackStreamFloat(double value) => Value = value;

    public double Value { get; }
}

/// <summary>A PackStream UTF-8 string.</summary>
public sealed class PackStreamString : PackStreamValue
{
    public PackStreamString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }
}

/// <summary>A PackStream byte array. The bytes are copied on construction and are immutable.</summary>
public sealed class PackStreamBytes : PackStreamValue
{
    public PackStreamBytes(ReadOnlyMemory<byte> value) => Value = value.ToArray();

    public ReadOnlyMemory<byte> Value { get; }
}

/// <summary>A PackStream list: an ordered, heterogeneous sequence of values.</summary>
public sealed class PackStreamList : PackStreamValue
{
    public PackStreamList(IReadOnlyList<PackStreamValue?> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = Copy(items);
    }

    public IReadOnlyList<PackStreamValue?> Items { get; }

    private static PackStreamValue?[] Copy(IReadOnlyList<PackStreamValue?> items)
    {
        var copy = new PackStreamValue?[items.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = items[i];
        }

        return copy;
    }
}

/// <summary>
/// A PackStream map. Entries preserve insertion order. Keys are always strings and never
/// <see langword="null"/>; duplicate keys are permitted by the wire format and, when read,
/// the last value for a key wins.
/// </summary>
public sealed class PackStreamMap : PackStreamValue
{
    public PackStreamMap(IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = Copy(entries);
    }

    public IReadOnlyList<KeyValuePair<string, PackStreamValue?>> Entries { get; }

    private static KeyValuePair<string, PackStreamValue?>[] Copy(
        IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries)
    {
        var copy = new KeyValuePair<string, PackStreamValue?>[entries.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            var entry = entries[i];
            if (entry.Key is null)
            {
                throw new ArgumentException("PackStream map keys must not be null.", nameof(entries));
            }

            copy[i] = entry;
        }

        return copy;
    }
}

/// <summary>
/// A generic PackStream structure: a tag byte and an ordered list of fields. Its meaning is
/// not interpreted here; graph and temporal structures are handled by later tasks.
/// </summary>
public sealed class PackStreamStructure : PackStreamValue
{
    public PackStreamStructure(byte tag, IReadOnlyList<PackStreamValue?> fields)
    {
        if (tag > PackStreamMarkers.StructureTagMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tag), tag, "PackStream structure tags must be in the range 0-127.");
        }

        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count > PackStreamMarkers.MaxStructureFields)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fields), fields.Count, "A PackStream structure may have at most 15 fields.");
        }

        Tag = tag;
        Fields = Copy(fields);
    }

    public byte Tag { get; }

    public IReadOnlyList<PackStreamValue?> Fields { get; }

    private static PackStreamValue?[] Copy(IReadOnlyList<PackStreamValue?> fields)
    {
        var copy = new PackStreamValue?[fields.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = fields[i];
        }

        return copy;
    }
}
