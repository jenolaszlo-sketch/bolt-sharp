using System.Buffers.Binary;
using System.Text;

namespace B3AQ2.ClosureProbe;

/// <summary>
/// Fresh, reviewer-generated PackStream 1.0 fixtures for the Q2 requalification. Written directly
/// from the published PackStream specification for this review; independent of the Q1 probe's
/// encoder, the producer's test fixtures and the production writer. The decoder is generic
/// (graph tags stay plain structures) and is used only to inspect fixture bytes.
/// </summary>
internal static class IndependentFixtures
{
    // ---- encoder ----

    public static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var p in parts)
        {
            total += p.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, offset);
            offset += p.Length;
        }

        return result;
    }

    private static byte[] ConcatFirst(byte[] first, byte[][] rest)
    {
        var all = new byte[rest.Length + 1][];
        all[0] = first;
        Array.Copy(rest, 0, all, 1, rest.Length);
        return Concat(all);
    }

    public static byte[] Int(long value)
    {
        if (value is >= -16 and <= 127)
        {
            return new[] { unchecked((byte)value) };
        }

        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
        {
            return new[] { (byte)0xC8, unchecked((byte)value) };
        }

        var b = new byte[5];
        b[0] = 0xCA;
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(1), (int)value);
        return b;
    }

    public static byte[] Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        return utf8.Length <= 15
            ? Concat(new[] { (byte)(0x80 | utf8.Length) }, utf8)
            : Concat(new[] { (byte)0xD0, (byte)utf8.Length }, utf8);
    }

    public static byte[] Null() => new byte[] { 0xC0 };

    public static byte[] List(params byte[][] items) =>
        items.Length <= 15 ? ConcatFirst(new[] { (byte)(0x90 | items.Length) }, items) : throw new NotSupportedException();

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        if (entries.Length == 0)
        {
            return new[] { (byte)0xA0 };
        }

        var parts = new List<byte[]> { new[] { (byte)(0xA0 | entries.Length) } };
        foreach (var (k, v) in entries)
        {
            parts.Add(Str(k));
            parts.Add(v);
        }

        return Concat(parts.ToArray());
    }

    public static byte[] Hello(params (string Key, byte[] Value)[] extra) => Struct(0x01, Map(extra));

    public static byte[] Struct(byte tag, params byte[][] fields) =>
        ConcatFirst(new[] { (byte)(0xB0 | fields.Length), tag }, fields);

    // ---- decoder (generic) ----

    public abstract record Psv;

    public sealed record PNull : Psv;

    public sealed record PInt(long Value) : Psv;

    public sealed record PStr(string Value) : Psv;

    public sealed record PList(List<Psv> Items) : Psv;

    public sealed record PMap(List<(string Key, Psv Value)> Entries) : Psv;

    public sealed record PStruct(byte Tag, List<Psv> Fields) : Psv;

    public static (Psv Value, int Consumed) Decode(byte[] bytes)
    {
        var position = 0;
        var value = DecodeValue(bytes, ref position);
        return (value, position);
    }

    private static Psv DecodeValue(byte[] bytes, ref int position)
    {
        var marker = bytes[position++];
        if (marker <= 0x7F)
        {
            return new PInt(marker);
        }

        if (marker >= 0xF0)
        {
            return new PInt(unchecked((sbyte)marker));
        }

        if ((marker & 0xF0) == 0x80)
        {
            return new PStr(ReadUtf8(bytes, ref position, marker & 0x0F));
        }

        if ((marker & 0xF0) == 0x90)
        {
            var n = marker & 0x0F;
            var items = new List<Psv>(n);
            for (var i = 0; i < n; i++)
            {
                items.Add(DecodeValue(bytes, ref position));
            }

            return new PList(items);
        }

        if ((marker & 0xF0) == 0xA0)
        {
            var n = marker & 0x0F;
            var entries = new List<(string, Psv)>(n);
            for (var i = 0; i < n; i++)
            {
                var key = (PStr)DecodeValue(bytes, ref position);
                entries.Add((key.Value, DecodeValue(bytes, ref position)));
            }

            return new PMap(entries);
        }

        if ((marker & 0xF0) == 0xB0)
        {
            var n = marker & 0x0F;
            var tag = bytes[position++];
            var fields = new List<Psv>(n);
            for (var i = 0; i < n; i++)
            {
                fields.Add(DecodeValue(bytes, ref position));
            }

            return new PStruct(tag, fields);
        }

        return marker switch
        {
            0xC0 => new PNull(),
            0xC8 => new PInt(unchecked((sbyte)bytes[position++])),
            0xD0 => new PStr(ReadUtf8(bytes, ref position, bytes[position++])),
            _ => throw new InvalidDataException($"fixture decoder: unsupported marker 0x{marker:X2}"),
        };
    }

    private static string ReadUtf8(byte[] bytes, ref int position, int length)
    {
        var s = Encoding.UTF8.GetString(bytes, position, length);
        position += length;
        return s;
    }
}
