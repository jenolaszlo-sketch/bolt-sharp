using System.Buffers.Binary;
using System.Text;

namespace B3AQ1.FalsificationProbe;

/// <summary>
/// The reviewer's independent PackStream 1.0 encoder/decoder, written directly from the published
/// PackStream specification for this qualification. It shares no code with the production
/// <c>BoltSharp.PackStream</c> implementation and none with the producer's test fixtures.
/// Decoding is deliberately <b>generic</b>: graph structure tags are returned as plain structures
/// so the reviewer can verify wire layout (tag, arity, field order) without trusting the
/// production reader's typed interpretation.
/// </summary>
internal static class IndependentPackStream
{
    // ---- Encoded value tree (decoder output) ----

    internal abstract record Psv;

    internal sealed record PNull : Psv;

    internal sealed record PBool(bool Value) : Psv;

    internal sealed record PInt(long Value) : Psv;

    internal sealed record PStr(string Value) : Psv;

    internal sealed record PList(List<Psv> Items) : Psv;

    internal sealed record PMap(List<(string Key, Psv Value)> Entries) : Psv;

    internal sealed record PStruct(byte Tag, List<Psv> Fields) : Psv;

    // ---- Encoding ----

    public static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    public static byte[] Concat(byte[] first, byte[][] rest)
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

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            var b = new byte[3];
            b[0] = 0xC9;
            BinaryPrimitives.WriteInt16BigEndian(b.AsSpan(1), (short)value);
            return b;
        }

        if (value is >= int.MinValue and <= int.MaxValue)
        {
            var b = new byte[5];
            b[0] = 0xCA;
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(1), (int)value);
            return b;
        }

        var o = new byte[9];
        o[0] = 0xCB;
        BinaryPrimitives.WriteInt64BigEndian(o.AsSpan(1), value);
        return o;
    }

    public static byte[] Str(string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length <= 15)
        {
            return Concat(new[] { (byte)(0x80 | utf8.Length) }, utf8);
        }

        if (utf8.Length <= byte.MaxValue)
        {
            return Concat(new[] { (byte)0xD0, (byte)utf8.Length }, utf8);
        }

        var header = new byte[3];
        header[0] = 0xD1;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(1), (ushort)utf8.Length);
        return Concat(header, utf8);
    }

    public static byte[] Bool(bool value) => new[] { value ? (byte)0xC3 : (byte)0xC2 };

    public static byte[] Null() => new[] { (byte)0xC0 };

    public static byte[] List(params byte[][] items) =>
        items.Length <= 15
            ? Concat(new[] { (byte)(0x90 | items.Length) }, items)
            : Concat(new[] { (byte)0xD4, (byte)items.Length }, items);

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        if (entries.Length == 0)
        {
            return new[] { (byte)0xA0 };
        }

        var parts = new List<byte[]> { new[] { (byte)(0xA0 | entries.Length) } };
        foreach (var (key, value) in entries)
        {
            parts.Add(Str(key));
            parts.Add(value);
        }

        return Concat(parts.ToArray());
    }

    public static byte[] Struct(byte tag, params byte[][] fields) =>
        Concat(new[] { (byte)(0xB0 | fields.Length), tag }, fields);

    // ---- Decoding (generic; graph tags stay plain structures) ----

    public static (Psv Value, int Consumed) Decode(ReadOnlySpan<byte> bytes)
    {
        var position = 0;
        var value = DecodeValue(bytes, ref position, depth: 0);
        return (value, position);
    }

    private static Psv DecodeValue(ReadOnlySpan<byte> bytes, ref int position, int depth)
    {
        if (depth > 200)
        {
            throw new InvalidDataException("Reviewer decoder depth guard tripped.");
        }

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
            return ReadList(bytes, ref position, marker & 0x0F, depth);
        }

        if ((marker & 0xF0) == 0xA0)
        {
            return ReadMap(bytes, ref position, marker & 0x0F, depth);
        }

        if ((marker & 0xF0) == 0xB0)
        {
            var fieldCount = marker & 0x0F;
            var tag = bytes[position++];
            var fields = new List<Psv>(fieldCount);
            for (var i = 0; i < fieldCount; i++)
            {
                fields.Add(DecodeValue(bytes, ref position, depth + 1));
            }

            return new PStruct(tag, fields);
        }

        return marker switch
        {
            0xC0 => new PNull(),
            0xC2 => new PBool(false),
            0xC3 => new PBool(true),
            0xC8 => new PInt(unchecked((sbyte)bytes[position++])),
            0xC9 => ReadInt(bytes, ref position, 2),
            0xCA => ReadInt(bytes, ref position, 4),
            0xCB => ReadInt(bytes, ref position, 8),
            0xD0 => new PStr(ReadUtf8(bytes, ref position, bytes[position++])),
            0xD1 => new PStr(ReadUtf8(bytes, ref position, (int)ReadLength(bytes, ref position, 2))),
            0xD2 => new PStr(ReadUtf8(bytes, ref position, (int)ReadLength(bytes, ref position, 4))),
            0xD4 => ReadList(bytes, ref position, bytes[position++], depth),
            0xD5 => ReadList(bytes, ref position, (int)ReadLength(bytes, ref position, 2), depth),
            0xD8 => ReadMap(bytes, ref position, bytes[position++], depth),
            0xD9 => ReadMap(bytes, ref position, (int)ReadLength(bytes, ref position, 2), depth),
            _ => throw new InvalidDataException($"Reviewer decoder: unsupported marker 0x{marker:X2}."),
        };
    }

    private static PInt ReadInt(ReadOnlySpan<byte> bytes, ref int position, int width)
    {
        long value = width switch
        {
            2 => BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(position, 2)),
            4 => BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(position, 4)),
            _ => BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(position, 8)),
        };
        position += width;
        return new PInt(value);
    }

    private static uint ReadLength(ReadOnlySpan<byte> bytes, ref int position, int width)
    {
        uint value = width switch
        {
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(position, 2)),
            _ => BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(position, 4)),
        };
        position += width;
        return value;
    }

    private static PList ReadList(ReadOnlySpan<byte> bytes, ref int position, int count, int depth)
    {
        var items = new List<Psv>(count);
        for (var i = 0; i < count; i++)
        {
            items.Add(DecodeValue(bytes, ref position, depth + 1));
        }

        return new PList(items);
    }

    private static PMap ReadMap(ReadOnlySpan<byte> bytes, ref int position, int count, int depth)
    {
        var entries = new List<(string, Psv)>(count);
        for (var i = 0; i < count; i++)
        {
            var key = DecodeValue(bytes, ref position, depth + 1);
            var value = DecodeValue(bytes, ref position, depth + 1);
            entries.Add((((PStr)key).Value, value));
        }

        return new PMap(entries);
    }

    private static string ReadUtf8(ReadOnlySpan<byte> bytes, ref int position, int length)
    {
        var payload = bytes.Slice(position, length);
        position += length;
        return Encoding.UTF8.GetString(payload);
    }
}
