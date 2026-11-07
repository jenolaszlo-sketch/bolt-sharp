using BoltSharp.PackStream;

namespace BoltSharp.Messages;

/// <summary>Authentication schemes predefined by the Bolt specification for <c>LOGON</c> (5.1+).</summary>
public enum BoltAuthScheme
{
    /// <summary>The authentication map carries a scheme name this codec does not recognize. Structural validation only; support is a B3-B decision.</summary>
    Unknown = 0,

    /// <summary><c>"none"</c>: no credentials.</summary>
    None = 1,

    /// <summary><c>"basic"</c>: <c>principal</c> and <c>credentials</c> strings.</summary>
    Basic = 2,

    /// <summary><c>"bearer"</c>: a <c>credentials</c> token string.</summary>
    Bearer = 3,

    /// <summary><c>"kerberos"</c>: a <c>credentials</c> token string.</summary>
    Kerberos = 4,
}

/// <summary>Base type for decoded client request messages (Bolt 5.8). Requests are immutable.</summary>
public abstract class BoltRequest
{
    /// <summary>The Bolt message signature (structure tag) of this request.</summary>
    public abstract byte Signature { get; }
}

/// <summary>
/// <c>HELLO</c> (<c>0x01</c>, one <c>extra</c> map field): initialize the connection. The map is
/// defensively owned via the PackStream value system. <see cref="Extra"/> preserves every entry in
/// wire order; known 5.8 fields (user_agent, routing, bolt_agent, notifications_*) are validated by
/// the codec and exposed through typed accessors where applicable.
/// <para>Legacy authentication entries (<c>scheme</c>, <c>credentials</c>, …) are prohibited in
/// Bolt 5.8 <c>HELLO</c> and rejected by the codec; authentication moved to <c>LOGON</c> in 5.1.</para>
/// </summary>
public sealed class HelloRequest : BoltRequest
{
    /// <summary>Creates a decoded HELLO request over the (already validated) extra map.</summary>
    public HelloRequest(PackStreamMap extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        Extra = extra;
    }

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Hello;

    /// <summary>The full <c>extra</c> map, in wire order. Never mutated after construction.</summary>
    public PackStreamMap Extra { get; }

    /// <summary>The <c>user_agent</c> string (required by the Bolt 5.8 schema).</summary>
    public string UserAgent => BoltMessageFields.GetString(Extra, "user_agent")!;

    /// <summary>The <c>bolt_agent</c> map when present, otherwise <see langword="null"/> (5.3+; the schema marks it mandatory, the codec validates its shape when present).</summary>
    public PackStreamMap? BoltAgent => BoltMessageFields.GetMap(Extra, "bolt_agent");

    /// <summary>The <c>routing</c> context map when present and non-null, otherwise <see langword="null"/> (<see langword="null"/> is a valid "no routing" value per the specification).</summary>
    public PackStreamMap? Routing => BoltMessageFields.GetMap(Extra, "routing");

    /// <summary>
    /// Returns a redacted representation: the message name and user agent only. Routing contexts,
    /// agent details and unknown metadata are never rendered.
    /// </summary>
    public override string ToString() => $"HELLO {{user_agent: \"{UserAgent}\"}}";
}

/// <summary>
/// <c>LOGON</c> (<c>0x6A</c>, one <c>auth</c> map field; 5.1+): authenticate the connection.
/// The codec validates the structural contract of the authentication map and recognizes the four
/// predefined scheme names. <b>Recognition does not imply support</b>: which schemes are accepted,
/// and whether credentials are valid, is a B3-B decision.
/// </summary>
public sealed class LogonRequest : BoltRequest
{
    /// <summary>Creates a decoded LOGON request over the (already validated) auth map.</summary>
    public LogonRequest(PackStreamMap auth, BoltAuthScheme recognizedScheme)
    {
        ArgumentNullException.ThrowIfNull(auth);
        Auth = auth;
        RecognizedScheme = recognizedScheme;
    }

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Logon;

    /// <summary>The full <c>auth</c> map, in wire order. Never mutated after construction.</summary>
    public PackStreamMap Auth { get; }

    /// <summary>The <c>scheme</c> string (required by the schema).</summary>
    public string Scheme => BoltMessageFields.GetString(Auth, "scheme")!;

    /// <summary>The recognized predefined scheme, or <see cref="BoltAuthScheme.Unknown"/> for any other scheme string.</summary>
    public BoltAuthScheme RecognizedScheme { get; }

    /// <summary>
    /// Returns a redacted representation: the message name and scheme name only. Principal,
    /// credentials, tokens and any other auth-map entries are never rendered.
    /// </summary>
    public override string ToString() => $"LOGON {{scheme: \"{Scheme}\"}}";
}

/// <summary>
/// <c>RUN</c> (<c>0x10</c>, three fields): execute a query. The query text is preserved verbatim —
/// the codec never parses Cypher and never decides whether a query is read-only. Parameters and
/// extra are preserved as PackStream maps with nested values intact.
/// </summary>
public sealed class RunRequest : BoltRequest
{
    /// <summary>Creates a decoded RUN request over the (already validated) fields.</summary>
    public RunRequest(string query, PackStreamMap parameters, PackStreamMap extra)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(extra);
        Query = query;
        Parameters = parameters;
        Extra = extra;
    }

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Run;

    /// <summary>The query string, verbatim.</summary>
    public string Query { get; }

    /// <summary>The parameter map. Nested values are preserved with signed-integer and graph semantics intact.</summary>
    public PackStreamMap Parameters { get; }

    /// <summary>The extra metadata map (bookmarks, tx_timeout, tx_metadata, mode, db, imp_user, notifications_*). Known 5.8 fields are type-validated by the codec.</summary>
    public PackStreamMap Extra { get; }

    /// <summary>
    /// Returns the query text and entry counts. Parameter and extra values are never rendered:
    /// query parameters may carry application-sensitive data.
    /// </summary>
    public override string ToString() =>
        $"RUN {{query: \"{Query}\", parameters: {Parameters.Entries.Count} entry(ies), extra: {Extra.Entries.Count} entry(ies)}}";
}

/// <summary>
/// <c>PULL</c> (<c>0x3F</c>, one <c>extra</c> map field): fetch records. <c>n</c> is required;
/// <c>n = -1</c> fetches all remaining records. <c>qid</c> is optional with default <c>-1</c>
/// (the last executed statement). The codec never fetches records and never schedules I/O.
/// </summary>
public sealed class PullRequest : BoltRequest
{
    /// <summary>Creates a decoded PULL request.</summary>
    public PullRequest(long n, long? qid)
    {
        N = n;
        Qid = qid;
    }

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Pull;

    /// <summary>The required record count; <c>-1</c> means all remaining records.</summary>
    public long N { get; }

    /// <summary>The optional query ID; <see langword="null"/> means absent on the wire (default <c>-1</c> semantics per the specification).</summary>
    public long? Qid { get; }

    /// <summary>Returns field values only; PULL carries no sensitive material.</summary>
    public override string ToString() => Qid is { } qid ? $"PULL {{n: {N}, qid: {qid}}}" : $"PULL {{n: {N}}}";
}

/// <summary>
/// <c>DISCARD</c> (<c>0x2F</c>, one <c>extra</c> map field): throw away records. Same top-level
/// shape as PULL: required <c>n</c> (with <c>n = -1</c> discarding all records) and optional
/// <c>qid</c>. The codec never consumes or disposes cursors; B0-B-Q1 observed that the qualified
/// driver emits DISCARD both when consuming a partially fetched result and on early session
/// disposal — sequence handling belongs to B3-B/B3-C.
/// </summary>
public sealed class DiscardRequest : BoltRequest
{
    /// <summary>Creates a decoded DISCARD request.</summary>
    public DiscardRequest(long n, long? qid)
    {
        N = n;
        Qid = qid;
    }

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Discard;

    /// <summary>The required record count; <c>-1</c> means all remaining records.</summary>
    public long N { get; }

    /// <summary>The optional query ID; <see langword="null"/> means absent on the wire (default <c>-1</c> semantics per the specification).</summary>
    public long? Qid { get; }

    /// <summary>Returns field values only; DISCARD carries no sensitive material.</summary>
    public override string ToString() => Qid is { } qid ? $"DISCARD {{n: {N}, qid: {qid}}}" : $"DISCARD {{n: {N}}}";
}

/// <summary><c>RESET</c> (<c>0x0F</c>, no fields): reset the connection. The codec performs no side effects; interruption and recovery are B3-B responsibilities.</summary>
public sealed class ResetRequest : BoltRequest
{
    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Reset;

    /// <summary>Returns the message name only.</summary>
    public override string ToString() => "RESET";
}

/// <summary><c>GOODBYE</c> (<c>0x02</c>, no fields): close the connection. The codec performs no side effects; connection shutdown is transport/B3-B responsibility.</summary>
public sealed class GoodbyeRequest : BoltRequest
{
    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Goodbye;

    /// <summary>Returns the message name only.</summary>
    public override string ToString() => "GOODBYE";
}

/// <summary><c>LOGOFF</c> (<c>0x6B</c>, no fields; 5.1+): log off the current user. The codec performs no side effects; session state is a B3-B responsibility.</summary>
public sealed class LogoffRequest : BoltRequest
{
    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Logoff;

    /// <summary>Returns the message name only.</summary>
    public override string ToString() => "LOGOFF";
}

/// <summary>
/// <c>TELEMETRY</c> (<c>0x54</c>, one integer field; 5.4+): transmit usage telemetry. Only the
/// structural contract (one integer) is validated here. The specification's API value domain
/// (0–3) is enforced by the server at processing time — the specification's own example shows an
/// out-of-domain value answered with FAILURE, not rejected as malformed — so the codec accepts any
/// integer and B3-B owns the value-domain decision.
/// </summary>
public sealed class TelemetryRequest : BoltRequest
{
    /// <summary>Creates a decoded TELEMETRY request.</summary>
    public TelemetryRequest(long api) => Api = api;

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Telemetry;

    /// <summary>The <c>api</c> integer, verbatim.</summary>
    public long Api { get; }

    /// <summary>Returns field values only; TELEMETRY carries no sensitive material.</summary>
    public override string ToString() => $"TELEMETRY {Api}";
}
