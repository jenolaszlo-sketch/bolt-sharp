namespace BoltSharp.Messages;

/// <summary>
/// Bolt 5.8 message signatures and the known-message classification used by the codec
/// (<see href="https://neo4j.com/docs/bolt/current/bolt/message/">Bolt message specification</a>,
/// "Messages" table; preserved copy: <c>docs/reviews/evidence/B3-A-IMPL-01/spec/message.adoc</c>).
/// </summary>
public static class BoltMessageSignatures
{
    // ---- Request messages (client → server) ----

    /// <summary><c>HELLO</c> (<c>0x01</c>): initialize connection; one <c>extra</c> map field.</summary>
    public const byte Hello = 0x01;

    /// <summary><c>GOODBYE</c> (<c>0x02</c>): close the connection; no fields.</summary>
    public const byte Goodbye = 0x02;

    /// <summary><c>RESET</c> (<c>0x0F</c>): reset the connection; no fields.</summary>
    public const byte Reset = 0x0F;

    /// <summary><c>RUN</c> (<c>0x10</c>): execute a query; query, parameters, extra.</summary>
    public const byte Run = 0x10;

    /// <summary><c>BEGIN</c> (<c>0x11</c>): begin explicit transaction; known for 5.8, not implemented by the B3-A codec.</summary>
    public const byte Begin = 0x11;

    /// <summary><c>COMMIT</c> (<c>0x12</c>): commit transaction; known for 5.8, not implemented by the B3-A codec.</summary>
    public const byte Commit = 0x12;

    /// <summary><c>ROLLBACK</c> (<c>0x13</c>): rollback transaction; known for 5.8, not implemented by the B3-A codec.</summary>
    public const byte Rollback = 0x13;

    /// <summary><c>DISCARD</c> (<c>0x2F</c>): discard records; one <c>extra</c> map field.</summary>
    public const byte Discard = 0x2F;

    /// <summary><c>TELEMETRY</c> (<c>0x54</c>): transmit usage telemetry; one integer field (5.4+).</summary>
    public const byte Telemetry = 0x54;

    /// <summary><c>ROUTE</c> (<c>0x66</c>): fetch routing table; known for 5.8, not implemented by the B3-A codec.</summary>
    public const byte Route = 0x66;

    /// <summary><c>LOGON</c> (<c>0x6A</c>): authenticate; one <c>auth</c> map field (5.1+).</summary>
    public const byte Logon = 0x6A;

    /// <summary><c>LOGOFF</c> (<c>0x6B</c>): log off the current user; no fields (5.1+).</summary>
    public const byte Logoff = 0x6B;

    /// <summary><c>PULL</c> (<c>0x3F</c>): fetch records; one <c>extra</c> map field.</summary>
    public const byte Pull = 0x3F;

    // ---- Summary / detail messages (server → client) ----

    /// <summary><c>SUCCESS</c> (<c>0x70</c>): request succeeded; one metadata map field.</summary>
    public const byte Success = 0x70;

    /// <summary><c>RECORD</c> (<c>0x71</c>): data values; one list field.</summary>
    public const byte Record = 0x71;

    /// <summary><c>IGNORED</c> (<c>0x7E</c>): request was ignored; no fields (Bolt 5.8 specification).</summary>
    public const byte Ignored = 0x7E;

    /// <summary><c>FAILURE</c> (<c>0x7F</c>): request failed; one metadata map field.</summary>
    public const byte Failure = 0x7F;

    /// <summary>
    /// Returns the Bolt 5.8 message name for a signature, or <see langword="null"/> when the
    /// signature is not a message signature defined by Bolt 5.8. Graph-structure tags (Node,
    /// Relationship, UnboundRelationship, Path) are PackStream structures, not messages, and
    /// therefore have no name here.
    /// </summary>
    public static string? TryGetName(byte signature) => signature switch
    {
        Hello => "HELLO",
        Goodbye => "GOODBYE",
        Reset => "RESET",
        Run => "RUN",
        Begin => "BEGIN",
        Commit => "COMMIT",
        Rollback => "ROLLBACK",
        Discard => "DISCARD",
        Telemetry => "TELEMETRY",
        Route => "ROUTE",
        Logon => "LOGON",
        Logoff => "LOGOFF",
        Pull => "PULL",
        Success => "SUCCESS",
        Record => "RECORD",
        Ignored => "IGNORED",
        Failure => "FAILURE",
        _ => null,
    };
}
