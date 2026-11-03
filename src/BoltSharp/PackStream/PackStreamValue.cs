using System.Collections.ObjectModel;

namespace BoltSharp.PackStream;

/// <summary>
/// Base type for a decoded PackStream value. A PackStream Null is represented by a C#
/// <see langword="null"/> reference rather than by a dedicated type. All values are
/// immutable: public constructors establish defensively owned state, and public accessors
/// never expose mutable backing storage (B1-R1, finding F1).
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

/// <summary>
/// A PackStream byte array. The payload is defensively copied on construction and is
/// exclusively owned thereafter (B1-R1, finding F1): every access to <see cref="Value"/>
/// returns memory over a fresh copy, so the stored bytes can never be changed through the
/// returned memory — not even via <c>MemoryMarshal.TryGetArray</c>.
/// </summary>
public sealed class PackStreamBytes : PackStreamValue
{
    private readonly byte[] _value;

    public PackStreamBytes(ReadOnlyMemory<byte> value) => _value = value.ToArray();

    /// <summary>
    /// The byte payload. Each access returns an independent copy of the stored bytes;
    /// mutating the returned memory never changes the stored value.
    /// </summary>
    public ReadOnlyMemory<byte> Value => _value.ToArray();

    /// <summary>
    /// Read-only access to the exclusively owned payload for internal encoding. The array
    /// is never exposed outside this assembly; external callers must use
    /// <see cref="Value"/> (which copies).
    /// </summary>
    internal ReadOnlySpan<byte> OwnedBytes => _value;
}

/// <summary>
/// A PackStream list: an ordered, heterogeneous sequence of values. The items are
/// defensively copied on construction and exposed only through a read-only wrapper
/// (B1-R1, finding F1).
/// </summary>
public sealed class PackStreamList : PackStreamValue
{
    public PackStreamList(IReadOnlyList<PackStreamValue?> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = new ReadOnlyCollection<PackStreamValue?>(Copy(items));
    }

    /// <summary>
    /// Internal constructor used by the reader. Takes ownership of the list the reader has
    /// just built; the reader never mutates or exposes it afterwards, so no defensive copy
    /// is required.
    /// </summary>
    internal PackStreamList(List<PackStreamValue?> ownedItems) =>
        Items = new ReadOnlyCollection<PackStreamValue?>(ownedItems);

    /// <summary>
    /// The list items. The returned collection is a read-only wrapper: casting it back to
    /// an array fails, and mutation attempts throw <see cref="NotSupportedException"/>.
    /// </summary>
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
/// the last value for a key wins. The entries are defensively copied on construction and
/// exposed only through a read-only wrapper (B1-R1, finding F1).
/// </summary>
public sealed class PackStreamMap : PackStreamValue
{
    public PackStreamMap(IReadOnlyList<KeyValuePair<string, PackStreamValue?>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = new ReadOnlyCollection<KeyValuePair<string, PackStreamValue?>>(Copy(entries));
    }

    /// <summary>
    /// Internal constructor used by the reader. Takes ownership of the entry list the
    /// reader has just built (its keys are guaranteed non-null and it is never mutated or
    /// exposed afterwards), so no defensive copy is required.
    /// </summary>
    internal PackStreamMap(List<KeyValuePair<string, PackStreamValue?>> ownedEntries) =>
        Entries = new ReadOnlyCollection<KeyValuePair<string, PackStreamValue?>>(ownedEntries);

    /// <summary>
    /// The map entries, in insertion order. The returned collection is a read-only wrapper:
    /// casting it back to an array fails, and mutation attempts throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
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
/// not interpreted here; graph and temporal structures are handled by later tasks. The
/// fields are defensively copied on construction and exposed only through a read-only
/// wrapper (B1-R1, finding F1).
/// <para>
/// <b>Reserved graph tags (qualification closed in B1-A1).</b> The writer validates that
/// generic structures carrying the reserved Bolt graph tags <c>4E</c>, <c>52</c>, <c>72</c>
/// and <c>50</c> declare the required field counts (4, 8, 4 and 3). Correct arity does not
/// establish semantic validity: such a structure may still fail typed graph decoding when
/// its field values do not conform to the graph structure's field types. This is within
/// the accepted B1 contract.
/// </para>
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
        Fields = new ReadOnlyCollection<PackStreamValue?>(Copy(fields));
    }

    /// <summary>
    /// Internal constructor used by the reader. Takes ownership of the field list the
    /// reader has just built; the reader never mutates or exposes it afterwards, so no
    /// defensive copy is required.
    /// </summary>
    internal PackStreamStructure(byte tag, List<PackStreamValue?> ownedFields)
    {
        Tag = tag;
        Fields = new ReadOnlyCollection<PackStreamValue?>(ownedFields);
    }

    public byte Tag { get; }

    /// <summary>
    /// The structure fields, in order. The returned collection is a read-only wrapper:
    /// casting it back to an array fails, and mutation attempts throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
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
