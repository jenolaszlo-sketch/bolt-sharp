using BoltSharp.PackStream;

namespace BoltSharp.Messages;

/// <summary>
/// Internal typed accessors over PackStream maps used by the message contracts. Lookups use the
/// reader's duplicate-key semantics (last value for a key wins). Accessors never throw: absence is
/// reported as <see langword="null"/> and type mismatches are left to the codec's validation layer,
/// which reports deterministic categories.
/// </summary>
internal static class BoltMessageFields
{
    /// <summary>Gets the value for <paramref name="key"/> (last-wins), or <see langword="null"/> when absent.</summary>
    public static PackStreamValue? GetValue(PackStreamMap map, string key)
    {
        PackStreamValue? result = null;
        foreach (var entry in map.Entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                result = entry.Value;
            }
        }

        return result;
    }

    /// <summary>Tries to get the value for <paramref name="key"/> (last-wins).</summary>
    public static bool TryGet(PackStreamMap map, string key, out PackStreamValue? value)
    {
        value = GetValue(map, key);
        return value is not null;
    }

    /// <summary>Gets a string value, or <see langword="null"/> when absent or not a string.</summary>
    public static string? GetString(PackStreamMap map, string key) =>
        GetValue(map, key) as PackStreamString is { } value ? value.Value : null;

    /// <summary>Gets an integer value, or <see langword="null"/> when absent or not an integer.</summary>
    public static long? GetInteger(PackStreamMap map, string key) =>
        GetValue(map, key) as PackStreamInteger is { } value ? value.Value : null;

    /// <summary>Gets a Boolean value, or <see langword="null"/> when absent or not a Boolean.</summary>
    public static bool? GetBoolean(PackStreamMap map, string key) =>
        GetValue(map, key) as PackStreamBoolean is { } value ? value.Value : null;

    /// <summary>Gets a map value, or <see langword="null"/> when absent or not a map.</summary>
    public static PackStreamMap? GetMap(PackStreamMap map, string key) =>
        GetValue(map, key) as PackStreamMap;

    /// <summary>Gets a list value, or <see langword="null"/> when absent or not a list.</summary>
    public static PackStreamList? GetList(PackStreamMap map, string key) =>
        GetValue(map, key) as PackStreamList;

    /// <summary>Gets a list-of-strings value, or <see langword="null"/> when absent or not a list of strings.</summary>
    public static IReadOnlyList<string>? GetStringList(PackStreamMap map, string key)
    {
        if (GetList(map, key) is not { } list)
        {
            return null;
        }

        var result = new string[list.Items.Count];
        for (var i = 0; i < result.Length; i++)
        {
            if (list.Items[i] is not PackStreamString item)
            {
                return null;
            }

            result[i] = item.Value;
        }

        return result;
    }
}

/// <summary>Internal construction helpers for ordered metadata maps.</summary>
internal static class BoltMessageMaps
{
    /// <summary>Creates an empty map.</summary>
    public static PackStreamMap Empty() =>
        new(Array.Empty<KeyValuePair<string, PackStreamValue?>>());

    /// <summary>Creates a map from ordered key/value entries, preserving the supplied order.</summary>
    public static PackStreamMap FromEntries((string Key, PackStreamValue? Value)[] entries)
    {
        var list = new List<KeyValuePair<string, PackStreamValue?>>(entries.Length);
        foreach (var (key, value) in entries)
        {
            list.Add(new KeyValuePair<string, PackStreamValue?>(key, value));
        }

        return new PackStreamMap(list);
    }
}
