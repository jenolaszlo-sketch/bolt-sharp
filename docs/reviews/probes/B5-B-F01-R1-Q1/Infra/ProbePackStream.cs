// B5-B-F01-R1-Q1 reviewer probe — INDEPENDENT PackStream 1.0 / Bolt 5.8 encoder.
//
// This encoder is written from the PackStream 1.0 specification (marker tables) by the reviewer.
// It is deliberately NOT the production PackStreamWriter: it is the second half of the byte-level
// oracle, used (a) to build request payloads and (b) to construct the expected FAILURE bytes from
// the accepted B3-B metadata contract, so that actual and expected values never share an encoder.
namespace BoltSharp.B5BF01R1Q1.Probes.Infra;

public static class ProbePackStream
{
    public static byte[] Structure(byte tag, params byte[][] fields)
    {
        var payload = new List<byte> { (byte)(0xB0 | fields.Length), tag };
        foreach (var field in fields)
        {
            payload.AddRange(field);
        }

        return payload.ToArray();
    }

    public static byte[] Str(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var header = bytes.Length switch
        {
            <= 0x0F => new[] { (byte)(0x80 | bytes.Length) },
            <= 0xFF => new byte[] { 0xD0, (byte)bytes.Length },
            _ => new byte[] { 0xD1, (byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF) },
        };
        return header.Concat(bytes).ToArray();
    }

    public static byte[] Int(long value) => value switch
    {
        >= -16 and < 0 => new[] { (byte)(0xF0 + (value + 16)) },
        >= 0 and <= 127 => new[] { (byte)value },
        // INT16 via C1 (big-endian) is enough for the probe's values.
        _ => new byte[]
        {
            0xC9,
            (byte)(value >> 56), (byte)(value >> 48), (byte)(value >> 40), (byte)(value >> 32),
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value,
        },
    };

    public static byte[] Map(params (string Key, byte[] Value)[] entries)
    {
        var payload = new List<byte> { (byte)(0xA0 | entries.Length) };
        foreach (var (key, value) in entries)
        {
            payload.AddRange(Str(key));
            payload.AddRange(value);
        }

        return payload.ToArray();
    }

    public static byte[] List(params byte[][] items)
    {
        var payload = new List<byte> { (byte)(0x90 | items.Length) };
        foreach (var item in items)
        {
            payload.AddRange(item);
        }

        return payload.ToArray();
    }

    public static byte[] Null() => new byte[] { 0xC0 };

    // ---- Accepted B3-B refusal constructions (metadata contract from the accepted B3-B
    // ---- evidence; entry order follows the documented FAILURE metadata shape) ----

    public const string UnsupportedDescription =
        "Explicit transactions and routing are not supported by this server.";

    public const string ProtocolViolationDescription =
        "The message cannot be handled in the current connection state.";

    /// <summary>Independent expected bytes of the accepted unsupported-command terminal FAILURE.</summary>
    public static byte[] ExpectedUnsupportedCommandFailure() => Structure(
        0x7F,
        Map(
            ("gql_status", Str("08N06")),
            ("neo4j_code", Str("Neo.ClientError.Request.Invalid")),
            ("description", Str(UnsupportedDescription)),
            ("diagnostic_record", Map())));

    /// <summary>Independent expected bytes of the accepted terminal protocol-violation FAILURE.</summary>
    public static byte[] ExpectedProtocolViolationFailure() => Structure(
        0x7F,
        Map(
            ("gql_status", Str("08N06")),
            ("neo4j_code", Str("Neo.ClientError.Request.Invalid")),
            ("description", Str(ProtocolViolationDescription)),
            ("diagnostic_record", Map())));
}
