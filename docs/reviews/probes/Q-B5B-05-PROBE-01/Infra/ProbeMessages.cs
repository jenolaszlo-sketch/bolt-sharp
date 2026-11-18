// Q-B5B-05-PROBE-01 producer probe — independent Bolt 5.8 message encoders and signature names.
//
// Everything here is implemented from the protocol specification so that the probe remains an
// independent oracle for what actually crossed the wire. Nothing in this file calls the
// BoltSharp codec, framing or session types.
//
// PackStream facts used here (verified against the production codec's arity contract):
//   TINY_STRUCT marker = 0xB0 | fieldCount; a Bolt message is one structure
//   <marker> <signature> <field 0> <field 1> ...
//   HELLO/LOGON/RUN/PULL/DISCARD/TELEMETRY/SUCCESS/RECORD/FAILURE: arity >= 1 (marker 0xB1..)
//   RESET/GOODBYE/LOGOFF/IGNORED: arity 0 (marker 0xB0)
using System.Buffers.Binary;
using System.Text;

namespace BoltSharp.Q5B05.Probe.Infra;

/// <summary>Bolt client and server message signatures used by this probe.</summary>
public static class ProbeMessages
{
    public const byte HelloTag = 0x01;
    public const byte LogonTag = 0x6A;
    public const byte LogoffTag = 0x6B;
    public const byte TelemetryTag = 0x54;
    public const byte GoodbyeTag = 0x02;
    public const byte ResetTag = 0x0F;
    public const byte RunTag = 0x10;
    public const byte BeginTag = 0x11;
    public const byte CommitTag = 0x12;
    public const byte RollbackTag = 0x13;
    public const byte DiscardTag = 0x2F;
    public const byte PullTag = 0x3F;
    public const byte RouteTag = 0x66;
    public const byte SuccessTag = 0x70;
    public const byte RecordTag = 0x71;
    public const byte IgnoredTag = 0x7E;
    public const byte FailureTag = 0x7F;

    /// <summary>The accepted zero-field IGNORED response encoding: <c>B0 7E</c>.</summary>
    public static readonly byte[] Ignored = { 0xB0, IgnoredTag };

    private static readonly Dictionary<byte, string> ClientNames = new()
    {
        [HelloTag] = "HELLO",
        [LogonTag] = "LOGON",
        [LogoffTag] = "LOGOFF",
        [TelemetryTag] = "TELEMETRY",
        [GoodbyeTag] = "GOODBYE",
        [ResetTag] = "RESET",
        [RunTag] = "RUN",
        [BeginTag] = "BEGIN",
        [CommitTag] = "COMMIT",
        [RollbackTag] = "ROLLBACK",
        [DiscardTag] = "DISCARD",
        [PullTag] = "PULL",
        [RouteTag] = "ROUTE",
    };

    private static readonly Dictionary<byte, string> ServerNames = new()
    {
        [SuccessTag] = "SUCCESS",
        [RecordTag] = "RECORD",
        [IgnoredTag] = "IGNORED",
        [FailureTag] = "FAILURE",
    };

    public static string Name(byte tag, bool clientToServer) =>
        (clientToServer ? ClientNames : ServerNames).TryGetValue(tag, out var name)
            ? name
            : "0x" + tag.ToString("X2");

    // ---- PackStream values -----------------------------------------------

    /// <summary>
    /// PackStream string with the minimal marker: TINY_STRING (0x80|len, ≤15 bytes), STRING8
    /// (0xD0, ≤255), STRING16 (0xD1) or STRING32 (0xD2).
    /// </summary>
    public static byte[] Text(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes.Length, int.MaxValue, nameof(value));

        if (bytes.Length <= 15)
        {
            var tiny = new byte[1 + bytes.Length];
            tiny[0] = (byte)(0x80 | bytes.Length);
            bytes.CopyTo(tiny, 1);
            return tiny;
        }

        if (bytes.Length <= 255)
        {
            return [.. new byte[] { 0xD0, (byte)bytes.Length }, .. bytes];
        }

        if (bytes.Length <= 65535)
        {
            Span<byte> head = stackalloc byte[3];
            head[0] = 0xD1;
            BinaryPrimitives.WriteUInt16BigEndian(head[1..], (ushort)bytes.Length);
            return [.. head, .. bytes];
        }

        Span<byte> wide = stackalloc byte[5];
        wide[0] = 0xD2;
        BinaryPrimitives.WriteUInt32BigEndian(wide[1..], (uint)bytes.Length);
        return [.. wide, .. bytes];
    }

    /// <summary>Empty map: <c>A0</c>.</summary>
    public static byte[] Map() => new byte[] { 0xA0 };

    /// <summary>TINY_MAP with up to 15 string keys and already-encoded values.</summary>
    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entries.Length, 15);
        var result = new List<byte> { (byte)(0xA0 | entries.Length) };
        foreach (var (key, value) in entries)
        {
            result.AddRange(Text(key));
            result.AddRange(value);
        }

        return result.ToArray();
    }

    /// <summary>Minimal-representation integer per the PackStream table.</summary>
    public static byte[] Int(long value)
    {
        if (value is >= -4 and <= 127)
        {
            return [(byte)value];
        }

        if (value is >= -32768 and <= 32767)
        {
            Span<byte> buffer = stackalloc byte[3];
            buffer[0] = 0xC9;
            BinaryPrimitives.WriteInt16BigEndian(buffer[1..], (short)value);
            return buffer.ToArray();
        }

        if (value is >= -2147483648 and <= 2147483647)
        {
            Span<byte> buffer = stackalloc byte[5];
            buffer[0] = 0xCA;
            BinaryPrimitives.WriteInt32BigEndian(buffer[1..], (int)value);
            return buffer.ToArray();
        }

        Span<byte> wide = stackalloc byte[9];
        wide[0] = 0xCB;
        BinaryPrimitives.WriteInt64BigEndian(wide[1..], value);
        return wide.ToArray();
    }

    // ---- Messages ---------------------------------------------------------

    /// <summary>HELLO: arity 1 (extra map). Requires user_agent and bolt_agent{product}.</summary>
    public static byte[] Hello(string userAgent, string product) =>
        Structure(HelloTag, Map(
            ("user_agent", Text(userAgent)),
            ("bolt_agent", Map(("product", Text(product))))));

    /// <summary>LOGON: arity 1 (auth map) with basic authentication.</summary>
    public static byte[] Logon(string scheme, string principal, string credentials) =>
        Structure(LogonTag, Map(
            ("scheme", Text(scheme)),
            ("principal", Text(principal)),
            ("credentials", Text(credentials))));

    /// <summary>RUN: arity 3 (query, parameters, extra) — the structure carries three separate fields.</summary>
    public static byte[] Run(string query, byte[]? parameters = null, byte[]? extra = null) =>
        Structure(RunTag, Text(query), parameters ?? Map(), extra ?? Map());

    /// <summary>PULL: arity 1 (extra map with n and an optional qid).</summary>
    public static byte[] Pull(long n = -1, long? qid = null)
    {
        var entries = new List<(string, byte[])> { ("n", Int(n)) };
        if (qid is not null)
        {
            entries.Add(("qid", Int(qid.Value)));
        }

        return Structure(PullTag, Map(entries.ToArray()));
    }

    /// <summary>DISCARD: arity 1 (extra map with n and an optional qid).</summary>
    public static byte[] Discard(long n = -1, long? qid = null)
    {
        var entries = new List<(string, byte[])> { ("n", Int(n)) };
        if (qid is not null)
        {
            entries.Add(("qid", Int(qid.Value)));
        }

        return Structure(DiscardTag, Map(entries.ToArray()));
    }

    /// <summary>RESET: arity 0 — the encoding is exactly <c>B0 0F</c>.</summary>
    public static byte[] Reset() => Structure(ResetTag);

    /// <summary>GOODBYE: arity 0 — the encoding is exactly <c>B0 02</c>.</summary>
    public static byte[] Goodbye() => Structure(GoodbyeTag);

    /// <summary>BEGIN: arity 1 (extra map) — used only to drive the terminal unsupported-message path.</summary>
    public static byte[] Begin() => Structure(BeginTag, Map());

    /// <summary>Bolt message structure: <c>0xB0 | fieldCount</c>, signature, then the fields.</summary>
    public static byte[] Structure(byte signature, params byte[][] fields)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fields.Length, 15);
        var result = new List<byte> { (byte)(0xB0 | fields.Length), signature };
        foreach (var field in fields)
        {
            result.AddRange(field);
        }

        return result.ToArray();
    }

    /// <summary>
    /// The hex encoding of a value's ASCII bytes, for byte-exact assertions against a captured
    /// payload. Keeping the expectation in the probe (rather than decoding with the production
    /// codec) preserves the probe as an independent wire oracle.
    /// </summary>
    public static string AsciiHex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// B2 chunk framing for one message: a two-byte big-endian payload length, the payload, and
    /// the 00 00 end-of-message sentinel. Messages up to 65,535 bytes fit one chunk, which covers
    /// every message this probe sends.
    /// </summary>
    public static byte[] Frame(byte[] payload)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, ushort.MaxValue);
        var frame = new byte[payload.Length + 4];
        frame[0] = (byte)(payload.Length >> 8);
        frame[1] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 2, payload.Length);
        frame[^2] = 0x00;
        frame[^1] = 0x00;
        return frame;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}