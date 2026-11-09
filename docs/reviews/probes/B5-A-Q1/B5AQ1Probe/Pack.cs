using System.Buffers;
using System.Text;

namespace B5AQ1Probe;

/// <summary>
/// Reviewer-owned PackStream writer and parser, derived from the normative PackStream 1 /
/// Bolt 5.8 marker rules, NOT from the production encoder. Golden byte vectors from the
/// accepted B0-B/B3-A evidence provide independent oracles.
/// </summary>
internal static class Pack
{
    public static byte[] Str(string s)
    {
        var payload = Encoding.UTF8.GetBytes(s);
        return Header(0x80, (uint)payload.Length).Concat(payload).ToArray();
    }

    public static byte[] Int(long v)
    {
        if (v is >= -16 and <= 127) { return new[] { unchecked((byte)v) }; }
        if (v is >= -128 and <= 127) { return new[] { (byte)0xC8, unchecked((byte)v) }; }
        if (v is >= short.MinValue and <= short.MaxValue)
        {
            return new[] { (byte)0xC9, (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };
        }

        if (v is >= int.MinValue and <= int.MaxValue)
        {
            var r = new byte[5];
            r[0] = 0xCA;
            for (var i = 0; i < 4; i++) { r[1 + i] = (byte)((v >> (8 * (3 - i))) & 0xFF); }
            return r;
        }

        var r8 = new byte[9];
        r8[0] = 0xCB;
        for (var i = 0; i < 8; i++) { r8[1 + i] = (byte)((v >> (8 * (7 - i))) & 0xFF); }
        return r8;
    }

    public static byte[] Null { get; } = { 0xC0 };

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        if (entries.Length > 15) { throw new NotSupportedException("probe writer handles tiny maps only"); }
        var bytes = new List<byte> { (byte)(0xA0 + entries.Length) };
        foreach (var (key, value) in entries)
        {
            bytes.AddRange(Str(key));
            bytes.AddRange(value);
        }

        return bytes.ToArray();
    }

    public static byte[] MapEmpty { get; } = { 0xA0 };

    public static byte[] List(params byte[][] items)
    {
        var bytes = new List<byte> { (byte)(0x90 + items.Length) };
        foreach (var item in items) { bytes.AddRange(item); }
        return bytes.ToArray();
    }

    public static byte[] Structure(byte tag, params byte[][] fields)
    {
        if (fields.Length > 15) { throw new NotSupportedException("probe writer handles tiny structs only"); }
        var bytes = new List<byte> { (byte)(0xB0 + fields.Length), tag };
        foreach (var field in fields) { bytes.AddRange(field); }
        return bytes.ToArray();
    }

    private static IEnumerable<byte> Header(byte tinyBase, uint size)
    {
        if (size < 16) { yield return (byte)(tinyBase + size); }
        else if (size < 256) { yield return tinyBase switch { 0x80 => 0xD0, 0x90 => 0xD4, _ => throw new NotSupportedException() }; yield return (byte)size; }
        else if (size < 65536) { yield return tinyBase switch { 0x80 => 0xD1, _ => throw new NotSupportedException() }; yield return (byte)(size >> 8); yield return (byte)size; }
        else { yield return 0xD2; yield return (byte)(size >> 24); yield return (byte)(size >> 16); yield return (byte)(size >> 8); yield return (byte)size; }
    }
}

/// <summary>Messages built by the reviewer (independent request encoder).</summary>
internal static class Review
{
    public static byte[] Hello(string userAgent = "reviewer/1.0") => Pack.Structure(
        0x01,
        Pack.Map(("user_agent", Pack.Str(userAgent)), ("bolt_agent", Pack.Map(("product", Pack.Str(userAgent))))));

    public static byte[] Logon(string principal = "reviewer", string credentials = "reviewer-secret") => Pack.Structure(
        0x6A,
        Pack.Map(("scheme", Pack.Str("basic")), ("principal", Pack.Str(principal)), ("credentials", Pack.Str(credentials))));

    public static byte[] Run(string query, params (string Key, byte[] Value)[] parameters) => Pack.Structure(
        0x10,
        Pack.Str(query),
        parameters.Length == 0 ? Pack.MapEmpty : Pack.Map(parameters),
        Pack.MapEmpty);

    public static byte[] Pull(long n, long? qid = null)
    {
        var entries = new List<(string, byte[])> { ("n", Pack.Int(n)) };
        if (qid is { } q) { entries.Add(("qid", Pack.Int(q))); }
        return Pack.Structure(0x3F, Pack.Map(entries.ToArray()));
    }

    public static byte[] Discard(long n) => Pack.Structure(0x2F, Pack.Map(("n", Pack.Int(n))));

    public static byte[] Reset => Pack.Structure(0x0F);
    public static byte[] Goodbye => Pack.Structure(0x02);
    public static byte[] Logoff => Pack.Structure(0x6B);
    public static byte[] Telemetry(long api) => Pack.Structure(0x54, Pack.Int(api));
    public static byte[] RunBadExtra(byte[] extra) => Pack.Structure(0x10, Pack.Str("RETURN 42 AS answer"), Pack.MapEmpty, extra);
}

/// <summary>Independent chunked-framing writer/reader (reviewer-owned, normative rules).</summary>
internal static class Framing
{
    public static byte[] Frame(params byte[][] chunks)
    {
        var bytes = new List<byte>();
        foreach (var chunk in chunks)
        {
            if (chunk.Length > 0xFFFF) { throw new OverflowException("chunk too large"); }
            bytes.Add((byte)(chunk.Length >> 8));
            bytes.Add((byte)(chunk.Length & 0xFF));
            bytes.AddRange(chunk);
        }

        bytes.Add(0);
        bytes.Add(0);
        return bytes.ToArray();
    }

    public static byte[][] Split(byte[] message, int chunkSize)
    {
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < message.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, message.Length - offset);
            var chunk = new byte[length];
            Array.Copy(message, offset, chunk, 0, length);
            chunks.Add(chunk);
        }

        return chunks.ToArray();
    }

    /// <summary>
    /// Reads one complete message from the stream. Returns the payload bytes, or null on clean
    /// EOF before any byte. NOOPs (leading zero-length chunks) are skipped per the normative
    /// keep-alive rule. Throws EndOfStreamException on EOF mid-message.
    /// </summary>
    public static byte[]? ReadMessage(Stream stream, int maxMessageSize = 0x1000_0000)
    {
        var payload = new List<byte>();
        while (true)
        {
            var header = new byte[2];
            if (!ReadExactly(stream, header)) { return payload.Count == 0 ? null : throw new EndOfStreamException("EOF mid-message (header)"); }
            var length = (header[0] << 8) | header[1];
            if (length == 0)
            {
                if (payload.Count == 0) { continue; } // inter-message NOOP keep-alive
                return payload.ToArray(); // message terminator
            }

            if (payload.Count + length > maxMessageSize) { throw new InvalidOperationException("reviewer message limit exceeded"); }
            var chunk = new byte[length];
            if (!ReadExactly(stream, chunk)) { throw new EndOfStreamException("EOF mid-message (payload)"); }
            payload.AddRange(chunk);
        }
    }

    public static bool ReadExactly(Stream stream, byte[] destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = stream.Read(destination, read, destination.Length - read);
            if (count == 0) { return false; }
            read += count;
        }

        return true;
    }
}
