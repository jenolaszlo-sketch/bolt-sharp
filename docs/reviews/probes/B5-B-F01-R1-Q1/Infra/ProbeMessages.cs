// B5-B-F01-R1-Q1 reviewer probe — session/request payload builders (independent encoder) and
// response decoding for assertions. Only structural inspection is performed here; the FAILURE
// byte comparisons use the independent expected encodings in ProbePackStream.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

namespace BoltSharp.B5BF01R1Q1.Probes.Infra;

public static class ProbeMessages
{
    public const byte HelloTag = 0x01;
    public const byte RunTag = 0x10;
    public const byte BeginTag = 0x11;
    public const byte CommitTag = 0x12;
    public const byte RollbackTag = 0x13;
    public const byte RouteTag = 0x66;
    public const byte PullTag = 0x3F;
    public const byte ResetTag = 0x0F;
    public const byte LogonTag = 0x6A;
    public const byte LogoffTag = 0x6B;
    public const byte GoodbyeTag = 0x02;

    public static byte[] Hello() => ProbePackStream.Structure(
        HelloTag,
        ProbePackStream.Map(
            ("user_agent", ProbePackStream.Str("reviewer-probe/1.0")),
            ("bolt_agent", ProbePackStream.Map(("product", ProbePackStream.Str("reviewer-probe"))))));

    /// <summary>Synthetic loopback-only credentials. NEVER store these payloads in evidence.</summary>
    public static byte[] Logon(string principal = "reviewer", string secret = "reviewer-secret") =>
        ProbePackStream.Structure(
            LogonTag,
            ProbePackStream.Map(
                ("scheme", ProbePackStream.Str("basic")),
                ("principal", ProbePackStream.Str(principal)),
                ("credentials", ProbePackStream.Str(secret))));

    public static byte[] Logoff() => ProbePackStream.Structure(LogoffTag);

    public static byte[] Goodbye() => ProbePackStream.Structure(GoodbyeTag);

    public static byte[] Reset() => ProbePackStream.Structure(ResetTag);

    public static byte[] Begin() => ProbePackStream.Structure(BeginTag, ProbePackStream.Map());

    public static byte[] Commit() => ProbePackStream.Structure(CommitTag, ProbePackStream.Map());

    public static byte[] Rollback() => ProbePackStream.Structure(RollbackTag, ProbePackStream.Map());

    public static byte[] Route() => ProbePackStream.Structure(
        RouteTag,
        ProbePackStream.Map(("address", ProbePackStream.Str("127.0.0.1:1"))),
        ProbePackStream.List(),
        ProbePackStream.Map());

    public static byte[] Run(string query, params (string Key, byte[] Value)[] parameters)
    {
        var map = parameters.Length == 0
            ? ProbePackStream.Map()
            : ProbePackStream.Map(parameters);
        return ProbePackStream.Structure(RunTag, ProbePackStream.Str(query), map, ProbePackStream.Map());
    }

    public static byte[] Pull(long n) => ProbePackStream.Structure(
        PullTag, ProbePackStream.Map(("n", ProbePackStream.Int(n))));

    public static byte[] ScalarQuery() => Run("RETURN 42 AS answer");

    public static byte[] ParameterQuery() =>
        Run("RETURN $value AS answer", ("value", ProbePackStream.Int(7)));

    public static byte[] GraphQuery() => Run("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b");

    public static byte[] UnsupportedQuery() => Run("CREATE (n:Review)");

    /// <summary>Signature byte of one complete message payload (tag), or -1 when absent.</summary>
    public static int Signature(byte[]? payload) => payload is { Length: >= 2 } ? payload[1] : -1;

    public static string Name(int signature) => signature switch
    {
        0x70 => "SUCCESS",
        0x71 => "RECORD",
        0x7F => "FAILURE",
        0x7E => "IGNORED",
        BeginTag => "BEGIN",
        CommitTag => "COMMIT",
        RollbackTag => "ROLLBACK",
        RouteTag => "ROUTE",
        RunTag => "RUN",
        PullTag => "PULL",
        ResetTag => "RESET",
        LogonTag => "LOGON",
        LogoffTag => "LOGOFF",
        GoodbyeTag => "GOODBYE",
        HelloTag => "HELLO",
        _ => $"0x{signature:X2}",
    };

    /// <summary>Extracts all map entries of a FAILURE metadata map: payload is B1 7F A4 (k v)*4.</summary>
    public static IReadOnlyDictionary<string, string> FailureMetadata(byte[] payload)
    {
        var result = new Dictionary<string, string>();
        if (payload.Length < 4 || payload[0] != 0xB1 || payload[1] != 0x7F)
        {
            return result;
        }

        var i = 3;
        while (i + 1 < payload.Length)
        {
            var (key, next) = ReadString(payload, i);
            if (key is null)
            {
                break;
            }

            var (value, after) = ReadString(payload, next);
            result[key] = value ?? string.Empty;
            i = after;
        }

        return result;
    }

    private static (string? Value, int Next) ReadString(byte[] payload, int offset)
    {
        if (offset >= payload.Length)
        {
            return (null, offset);
        }

        var marker = payload[offset];
        int length;
        int headerSize;
        if (marker is >= 0x80 and <= 0x8F)
        {
            length = marker & 0x0F;
            headerSize = 1;
        }
        else if (marker == 0xD0)
        {
            length = payload[offset + 1];
            headerSize = 2;
        }
        else
        {
            return (null, offset);
        }

        var start = offset + headerSize;
        if (start + length > payload.Length)
        {
            return (null, offset);
        }

        return (System.Text.Encoding.UTF8.GetString(payload, start, length), start + length);
    }
}
