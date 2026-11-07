using System.Text;
using BoltSharp.PackStream;

namespace BoltSharp.Messages;

/// <summary>Base type for Bolt server response messages (Bolt 5.8). Responses are immutable.</summary>
public abstract class BoltResponse
{
    /// <summary>The Bolt message signature (structure tag) of this response.</summary>
    public abstract byte Signature { get; }
}

/// <summary>
/// <c>SUCCESS</c> (<c>0x70</c>, one metadata map field): the request succeeded. The metadata map is
/// preserved verbatim in wire order; known Bolt 5.8 metadata keys are type-validated by the codec
/// and exposed through typed accessors where applicable.
/// <para>
/// Schema validity is deliberately separated from driver compatibility: any valid generic
/// <c>SUCCESS</c> metadata set can be encoded (including an empty map), while the metadata a
/// concrete client requires — for example <c>server</c> for the B0-B-qualified Neo4j.Driver 6.4.0
/// initialization path — is enforced by the B3-B response builders, never by this codec.
/// </para>
/// </summary>
public sealed class SuccessResponse : BoltResponse
{
    /// <summary>Creates a SUCCESS response over the given metadata map.</summary>
    public SuccessResponse(PackStreamMap metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Metadata = metadata;
    }

    /// <summary>A SUCCESS response with empty metadata.</summary>
    public static SuccessResponse Empty { get; } = new(BoltMessageMaps.Empty());

    /// <summary>Creates a SUCCESS response from ordered key/value entries.</summary>
    public static SuccessResponse FromEntries(params (string Key, PackStreamValue? Value)[] entries) =>
        new(BoltMessageMaps.FromEntries(entries));

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Success;

    /// <summary>The full metadata map, in the order supplied. Never mutated after construction.</summary>
    public PackStreamMap Metadata { get; }

    /// <summary>The <c>server</c> agent string when present, otherwise <see langword="null"/> (B0-B-Q1 D01: required by the qualified driver's initialization path; optional in the message schema).</summary>
    public string? Server => BoltMessageFields.GetString(Metadata, "server");

    /// <summary>The <c>connection_id</c> string when present, otherwise <see langword="null"/> (B0-B-Q1 D01: optional for the qualified driver).</summary>
    public string? ConnectionId => BoltMessageFields.GetString(Metadata, "connection_id");

    /// <summary>The <c>hints</c> map when present, otherwise <see langword="null"/>.</summary>
    public PackStreamMap? Hints => BoltMessageFields.GetMap(Metadata, "hints");

    /// <summary>The <c>fields</c> list of result-field names for a RUN SUCCESS when present, otherwise <see langword="null"/>.</summary>
    public IReadOnlyList<string>? Fields => BoltMessageFields.GetStringList(Metadata, "fields");

    /// <summary>The <c>has_more</c> flag for PULL/DISCARD SUCCESS when present, otherwise <see langword="null"/>.</summary>
    public bool? HasMore => BoltMessageFields.GetBoolean(Metadata, "has_more");

    /// <summary>The <c>qid</c> statement ID when present, otherwise <see langword="null"/> (explicit transactions only).</summary>
    public long? Qid => BoltMessageFields.GetInteger(Metadata, "qid");

    /// <summary>The <c>t_first</c> integer (ms until the first record) when present, otherwise <see langword="null"/>.</summary>
    public long? TFirst => BoltMessageFields.GetInteger(Metadata, "t_first");

    /// <summary>The <c>db</c> string (resolved home database, 5.8) when present, otherwise <see langword="null"/>.</summary>
    public string? Db => BoltMessageFields.GetString(Metadata, "db");

    /// <summary>The <c>advertised_address</c> string (5.8, LOGON SUCCESS) when present, otherwise <see langword="null"/>.</summary>
    public string? AdvertisedAddress => BoltMessageFields.GetString(Metadata, "advertised_address");

    /// <summary>The <c>credentials_expired</c> flag when present, otherwise <see langword="null"/>.</summary>
    public bool? CredentialsExpired => BoltMessageFields.GetBoolean(Metadata, "credentials_expired");

    /// <summary>Returns a structural summary: entry count plus a bounded set of known key names. Metadata values are never rendered.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder("SUCCESS {");
        BoltResponseHelpers.AppendKnownKeys(builder, Metadata);
        return builder.Append('}').ToString();
    }
}

/// <summary>
/// <c>RECORD</c> (<c>0x71</c>, one list field): data values. The single field is a list whose
/// elements are arbitrary supported PackStream values, including Bolt graph structures (Node,
/// Relationship, UnboundRelationship, Path) decoded to their accepted B1 typed representations —
/// graph structures are never reduced to ordinary dictionaries, and node/relationship identities
/// remain distinct even when numeric IDs overlap.
/// <para>
/// <b>NC-05b boundary.</b> The codec enforces the outer RECORD structure arity (exactly one list
/// field). Verifying that the number of values matches the <c>fields</c> list of a previous RUN
/// SUCCESS requires cross-message context and is deliberately <b>not</b> done here: the codec is
/// stateless. That assertion belongs to B3-B/B3-C.
/// </para>
/// </summary>
public sealed class RecordResponse : BoltResponse
{
    /// <summary>Creates a RECORD response over the given data list.</summary>
    public RecordResponse(PackStreamList data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>Creates a RECORD response from ordered values.</summary>
    public static RecordResponse FromValues(params PackStreamValue?[] values) =>
        new(new PackStreamList(values));

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Record;

    /// <summary>The data list. Graph structures inside are typed PackStream values; the collection is read-only.</summary>
    public PackStreamList Data { get; }

    /// <summary>Returns a structural summary: value count and type names only. Record values are never rendered (they may carry application data).</summary>
    public override string ToString()
    {
        var builder = new StringBuilder($"RECORD {{values: {Data.Items.Count} [");
        for (var i = 0; i < Data.Items.Count && i < 8; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(Data.Items[i]?.GetType().Name ?? "null");
        }

        if (Data.Items.Count > 8)
        {
            builder.Append(", …");
        }

        return builder.Append("]}").ToString();
    }
}

/// <summary>
/// <c>FAILURE</c> (<c>0x7F</c>, one metadata map field): the request failed. Bolt 5.7+ GQL
/// semantics apply: <c>gql_status</c>, <c>neo4j_code</c>, <c>description</c>, <c>message</c>,
/// <c>diagnostic_record</c> and bounded recursive <c>cause</c> maps are recognized and
/// type-validated; the pre-5.7 <c>code</c> field is prohibited. A missing optional field is never
/// treated as evidence of successful execution — that judgment is state-machine work (B3-B).
/// </summary>
public sealed class FailureResponse : BoltResponse
{
    /// <summary>Creates a FAILURE response over the given metadata map.</summary>
    public FailureResponse(PackStreamMap metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Metadata = metadata;
    }

    /// <summary>Creates a FAILURE response from ordered key/value entries.</summary>
    public static FailureResponse FromEntries(params (string Key, PackStreamValue? Value)[] entries) =>
        new(BoltMessageMaps.FromEntries(entries));

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Failure;

    /// <summary>The full metadata map, in the order supplied. Never mutated after construction.</summary>
    public PackStreamMap Metadata { get; }

    /// <summary>The <c>gql_status</c> string when present, otherwise <see langword="null"/> (5.7+; the stable failure identifier per the specification).</summary>
    public string? GqlStatus => BoltMessageFields.GetString(Metadata, "gql_status");

    /// <summary>The <c>neo4j_code</c> string when present, otherwise <see langword="null"/> (B0-B-Q1 D04: its presence drives the qualified driver's exception subclass).</summary>
    public string? Neo4jCode => BoltMessageFields.GetString(Metadata, "neo4j_code");

    /// <summary>The <c>description</c> string when present, otherwise <see langword="null"/>. Under new-style metadata the qualified driver exposes this through <c>GqlStatusDescription</c>, not necessarily <c>Message</c>.</summary>
    public string? Description => BoltMessageFields.GetString(Metadata, "description");

    /// <summary>The <c>message</c> string when present, otherwise <see langword="null"/>. Not a stable marker (documented from Bolt 6.0 onwards; retained as valid 5.8 metadata).</summary>
    public string? Message => BoltMessageFields.GetString(Metadata, "message");

    /// <summary>The <c>diagnostic_record</c> map when present, otherwise <see langword="null"/>.</summary>
    public PackStreamMap? DiagnosticRecord => BoltMessageFields.GetMap(Metadata, "diagnostic_record");

    /// <summary>The nested <c>cause</c> map when present, otherwise <see langword="null"/>. Causes are validated recursively with bounded nesting.</summary>
    public PackStreamMap? Cause => BoltMessageFields.GetMap(Metadata, "cause");

    /// <summary>Returns a structural summary: status/code identifiers only. Descriptions, messages and diagnostic records are never rendered.</summary>
    public override string ToString() =>
        $"FAILURE {{gql_status: {GqlStatus ?? "absent"}, neo4j_code: {Neo4jCode ?? "absent"}}}";
}

/// <summary>
/// <c>IGNORED</c> (<c>0x7E</c>, no fields): the request was ignored. Encoded as a zero-field
/// structure with no metadata or payload, exactly as the Bolt 5.8 specification defines it.
/// The codec never decides when to emit IGNORED; the failed-state logic (including the observed
/// pipelined PULL after FAILURE) belongs to B3-B.
/// </summary>
public sealed class IgnoredResponse : BoltResponse
{
    /// <summary>The shared IGNORED instance; the message carries no state.</summary>
    public static IgnoredResponse Instance { get; } = new();

    /// <inheritdoc/>
    public override byte Signature => BoltMessageSignatures.Ignored;

    /// <summary>Returns the message name only.</summary>
    public override string ToString() => "IGNORED";
}

/// <summary>Shared helpers for the typed response accessors.</summary>
internal static class BoltResponseHelpers
{
    private static readonly string[] KnownSuccessKeys =
    {
        "server", "connection_id", "hints", "protocol_version", "fields", "t_first", "qid", "db",
        "has_more", "advertised_address", "credentials_expired",
    };

    internal static void AppendKnownKeys(StringBuilder builder, PackStreamMap map)
    {
        var first = true;
        foreach (var known in KnownSuccessKeys)
        {
            if (BoltMessageFields.TryGet(map, known, out _))
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(known);
                first = false;
            }
        }

        var unknownCount = 0;
        foreach (var entry in map.Entries)
        {
            if (Array.IndexOf(KnownSuccessKeys, entry.Key) < 0)
            {
                unknownCount++;
            }
        }

        if (unknownCount > 0)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            builder.Append($"… +{unknownCount} other(s)");
        }
    }
}
