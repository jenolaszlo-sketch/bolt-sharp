using System.Buffers;
using BoltSharp.PackStream;

namespace BoltSharp.Messages;

/// <summary>
/// Stateful-free Bolt 5.8 message codec: decodes one complete client request payload into a typed
/// <see cref="BoltRequest"/>, decodes one complete server response payload into a typed
/// <see cref="BoltResponse"/>, and encodes a <see cref="BoltResponse"/> into Bolt 5.8 PackStream
/// bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary.</b> The codec receives one complete, unchunked message payload (B2 framing has
/// already removed chunk headers and the end-of-message sentinel) and knows nothing about how the
/// bytes arrived. It performs no session work: no state transitions, no authentication decisions,
/// no query execution, no cursor management, no response sequencing, and it never fabricates a
/// response. Whether a parse failure becomes a FAILURE message, an IGNORED, or a connection closure
/// is a B3-B decision.
/// </para>
/// <para>
/// <b>Determinism and ordering.</b> Decoding is deterministic. Encoding preserves the caller's
/// metadata-map order; no canonical map ordering is claimed or enforced. Integer encoding uses the
/// PackStream minimal-representation table.
/// </para>
/// <para>
/// <b>Limits.</b> The codec reuses the existing PackStream limits (nesting depth, list/map sizes,
/// string/byte-array lengths, total element budget) via <see cref="PackStreamLimits"/>; it defines
/// no parallel limit system.
/// </para>
/// <para>
/// <b>Errors.</b> All rejections are <see cref="BoltMessageException"/> with a deterministic
/// <see cref="BoltMessageErrorCategory"/>. Messages reference field names, expected types and
/// counts only — never field values — so authentication material and query payloads can never leak
/// through diagnostics. A parse failure never produces a response.
/// </para>
/// </remarks>
public sealed class BoltMessageCodec
{
    private readonly PackStreamLimits _limits;

    /// <summary>Creates a codec using the default PackStream limits.</summary>
    public BoltMessageCodec() : this(PackStreamLimits.Default)
    {
    }

    /// <summary>Creates a codec with explicit PackStream limits.</summary>
    public BoltMessageCodec(PackStreamLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
    }

    /// <summary>The PackStream limits applied to decoding and encoding.</summary>
    public PackStreamLimits Limits => _limits;

    // ---- Requests ----

    /// <summary>
    /// Decodes one complete client request payload into a typed message.
    /// </summary>
    /// <param name="payload">The complete, unchunked Bolt message payload (one PackStream structure).</param>
    /// <returns>The typed request message.</returns>
    /// <exception cref="BoltMessageException">
    /// The payload is empty, not a single structure, carries an unknown or unsupported signature,
    /// has the wrong structure arity, misses a required field, carries a wrong-typed or
    /// prohibited field, contains trailing bytes, is truncated, or contains invalid nested
    /// PackStream data. The exception category identifies the failure precisely.
    /// </exception>
    public BoltRequest DecodeRequest(ReadOnlyMemory<byte> payload)
    {
        var structure = ReadTopLevelStructure(payload);
        return structure.Tag switch
        {
            BoltMessageSignatures.Hello => DecodeHello(structure),
            BoltMessageSignatures.Logon => DecodeLogon(structure),
            BoltMessageSignatures.Run => DecodeRun(structure),
            BoltMessageSignatures.Pull => DecodePullOrDiscard(structure, isPull: true),
            BoltMessageSignatures.Discard => DecodePullOrDiscard(structure, isPull: false),
            BoltMessageSignatures.Reset => ZeroArity(structure, () => new ResetRequest()),
            BoltMessageSignatures.Goodbye => ZeroArity(structure, () => new GoodbyeRequest()),
            BoltMessageSignatures.Logoff => ZeroArity(structure, () => new LogoffRequest()),
            BoltMessageSignatures.Telemetry => DecodeTelemetry(structure),
            BoltMessageSignatures.Begin or
            BoltMessageSignatures.Commit or
            BoltMessageSignatures.Rollback or
            BoltMessageSignatures.Route => throw new BoltMessageException(
                BoltMessageErrorCategory.UnsupportedMessage,
                $"Message {Name(structure.Tag)} is defined by Bolt 5.8 but is not implemented by this codec; " +
                "transaction and routing behaviour belongs to later milestones.",
                structure.Tag),
            _ => throw new BoltMessageException(
                BoltMessageErrorCategory.UnknownSignature,
                $"Structure tag 0x{structure.Tag:X2} is not a Bolt 5.8 message signature.",
                structure.Tag),
        };
    }

    // ---- Responses (decode: diagnostics and test verification) ----

    /// <summary>
    /// Decodes one complete server response payload into a typed message. Intended for diagnostics
    /// and test verification; encoding remains the authoritative server direction.
    /// </summary>
    /// <exception cref="BoltMessageException">Identical failure contract to <see cref="DecodeRequest"/>.</exception>
    public BoltResponse DecodeResponse(ReadOnlyMemory<byte> payload)
    {
        var structure = ReadTopLevelStructure(payload);
        switch (structure.Tag)
        {
            case BoltMessageSignatures.Success:
                RequireArity(structure, 1);
                var successMetadata = RequireMap(structure, 0, "metadata");
                ValidateSuccessMetadata(successMetadata);
                return new SuccessResponse(successMetadata);
            case BoltMessageSignatures.Record:
                RequireArity(structure, 1);
                if (structure.Fields[0] is not PackStreamList data)
                {
                    throw InvalidField(structure.Tag, "data", "a list");
                }

                return new RecordResponse(data);
            case BoltMessageSignatures.Failure:
                RequireArity(structure, 1);
                var failureMetadata = RequireMap(structure, 0, "metadata");
                ValidateFailureMetadata(failureMetadata, depth: 0);
                return new FailureResponse(failureMetadata);
            case BoltMessageSignatures.Ignored:
                // Bolt 5.8 defines IGNORED with no fields. The B0-B producer fixture emitted a
                // one-field form (B1 7E A0) that the qualified driver tolerates by discarding
                // unconsumed structure bytes; the codec follows the specification and rejects the
                // extra field. See docs/reviews/B3-A-IMPL-01.md for the recorded discrepancy.
                RequireArity(structure, 0);
                return IgnoredResponse.Instance;
            default:
                throw new BoltMessageException(
                    BoltMessageErrorCategory.UnknownSignature,
                    $"Structure tag 0x{structure.Tag:X2} is not a Bolt 5.8 response signature.",
                    structure.Tag);
        }
    }

    // ---- Response encoding (server direction) ----

    /// <summary>
    /// Encodes one valid server response into Bolt 5.8 PackStream bytes (a single structure; chunk
    /// framing is a B2 responsibility). Encoding is deterministic for the same input values and
    /// preserves the caller's metadata-map order.
    /// </summary>
    /// <exception cref="ArgumentNullException">The response is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The response type is not a supported Bolt response.</exception>
    /// <exception cref="BoltMessageException">
    /// The response metadata violates the Bolt 5.8 schema (wrong-typed or prohibited fields,
    /// unbounded cause nesting). No bytes are committed when validation fails.
    /// </exception>
    public byte[] EncodeResponse(BoltResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PackStreamWriter(buffer, _limits);
        switch (response)
        {
            case SuccessResponse success:
                ValidateSuccessMetadata(success.Metadata);
                writer.WriteStructure(BoltMessageSignatures.Success, new PackStreamValue?[] { success.Metadata });
                break;
            case RecordResponse record:
                writer.WriteStructure(BoltMessageSignatures.Record, new PackStreamValue?[] { record.Data });
                break;
            case FailureResponse failure:
                ValidateFailureMetadata(failure.Metadata, depth: 0);
                writer.WriteStructure(BoltMessageSignatures.Failure, new PackStreamValue?[] { failure.Metadata });
                break;
            case IgnoredResponse:
                writer.WriteStructure(BoltMessageSignatures.Ignored, Array.Empty<PackStreamValue?>());
                break;
            default:
                throw new ArgumentException(
                    $"Unknown Bolt response type '{response.GetType().Name}'.", nameof(response));
        }

        return buffer.WrittenSpan.ToArray();
    }

    // ---- Payload framing (one complete structure, nothing else) ----

    private PackStreamStructure ReadTopLevelStructure(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.EmptyPayload,
                "The message payload is empty; a Bolt message must be one PackStream structure.");
        }

        var reader = new PackStreamReader(payload, _limits);
        PackStreamValue? value;
        try
        {
            value = reader.ReadValue();
        }
        catch (PackStreamTruncatedException ex)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.Truncated,
                "The message payload ended before the complete structure could be read.",
                null,
                ex);
        }
        catch (PackStreamFormatException ex)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.InvalidPackStreamValue,
                $"The message payload contains invalid PackStream data: {ex.Message}",
                null,
                ex);
        }

        if (reader.HasMore)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.TrailingBytes,
                $"The message payload contains {payload.Length - reader.Position} byte(s) after the top-level structure.");
        }

        if (value is not PackStreamStructure structure)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.NotAStructure,
                $"A Bolt message must be a PackStream structure but the payload's top-level value is a {TypeName(value)}.");
        }

        return structure;
    }

    // ---- Per-message decoding ----

    private BoltRequest DecodeHello(PackStreamStructure structure)
    {
        RequireArity(structure, 1);
        var extra = RequireMap(structure, 0, "extra");
        ValidateHelloExtra(extra, structure.Tag);
        return new HelloRequest(extra);
    }

    private BoltRequest DecodeLogon(PackStreamStructure structure)
    {
        RequireArity(structure, 1);
        var auth = RequireMap(structure, 0, "auth");

        var schemeValue = BoltMessageFields.GetValue(auth, "scheme");
        if (schemeValue is null)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.MissingRequiredField,
                "Message LOGON field 'auth.scheme' is required.",
                structure.Tag);
        }

        if (schemeValue is not PackStreamString scheme)
        {
            throw InvalidField(structure.Tag, "auth.scheme", "a string");
        }

        var recognized = scheme.Value switch
        {
            "none" => BoltAuthScheme.None,
            "basic" => BoltAuthScheme.Basic,
            "bearer" => BoltAuthScheme.Bearer,
            "kerberos" => BoltAuthScheme.Kerberos,
            _ => BoltAuthScheme.Unknown,
        };

        // Structural contract of the predefined schemes. Recognition never implies support:
        // accepting or rejecting the credentials is a B3-B decision. Unknown schemes are preserved
        // without further validation.
        switch (recognized)
        {
            case BoltAuthScheme.Basic:
                RequireStringField(auth, "principal", structure.Tag, "auth");
                RequireStringField(auth, "credentials", structure.Tag, "auth");
                break;
            case BoltAuthScheme.Bearer:
            case BoltAuthScheme.Kerberos:
                RequireStringField(auth, "credentials", structure.Tag, "auth");
                break;
        }

        return new LogonRequest(auth, recognized);
    }

    private BoltRequest DecodeRun(PackStreamStructure structure)
    {
        RequireArity(structure, 3);
        if (structure.Fields[0] is not PackStreamString query)
        {
            throw InvalidField(structure.Tag, "query", "a string");
        }

        var parameters = RequireMap(structure, 1, "parameters");
        var extra = RequireMap(structure, 2, "extra");
        ValidateRunExtra(extra, structure.Tag);
        return new RunRequest(query.Value, parameters, extra);
    }

    private BoltRequest DecodePullOrDiscard(PackStreamStructure structure, bool isPull)
    {
        RequireArity(structure, 1);
        var extra = RequireMap(structure, 0, "extra");
        var name = isPull ? "PULL" : "DISCARD";

        var nValue = BoltMessageFields.GetValue(extra, "n");
        if (nValue is null)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.MissingRequiredField,
                $"Message {name} field 'extra.n' is required and has no default.",
                structure.Tag);
        }

        if (nValue is not PackStreamInteger n)
        {
            throw InvalidField(structure.Tag, "extra.n", "an integer");
        }

        if (n.Value < -1)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.InvalidFieldValue,
                $"Message {name} field 'extra.n' must be a record count or -1 (all records); {n.Value} is outside the permitted domain.",
                structure.Tag);
        }

        long? qid = null;
        if (BoltMessageFields.GetValue(extra, "qid") is { } qidValue)
        {
            if (qidValue is not PackStreamInteger qidInteger)
            {
                throw InvalidField(structure.Tag, "extra.qid", "an integer");
            }

            if (qidInteger.Value < -1)
            {
                throw new BoltMessageException(
                    BoltMessageErrorCategory.InvalidFieldValue,
                    $"Message {name} field 'extra.qid' must be a statement ID or -1 (the last executed statement).",
                    structure.Tag);
            }

            qid = qidInteger.Value;
        }

        return isPull ? new PullRequest(n.Value, qid) : new DiscardRequest(n.Value, qid);
    }

    private BoltRequest DecodeTelemetry(PackStreamStructure structure)
    {
        RequireArity(structure, 1);
        if (structure.Fields[0] is not PackStreamInteger api)
        {
            throw InvalidField(structure.Tag, "api", "an integer");
        }

        return new TelemetryRequest(api.Value);
    }

    private BoltRequest ZeroArity(PackStreamStructure structure, Func<BoltRequest> create)
    {
        RequireArity(structure, 0);
        return create();
    }

    // ---- Schema validation (shared by decode and encode) ----

    private void ValidateHelloExtra(PackStreamMap extra, byte signature)
    {
        // Prohibited legacy fields. Bolt 5.8 HELLO carries no authentication (moved to LOGON in
        // 5.1) and no protocol patching (removed after 4.4).
        RejectIfPresent(extra, "scheme", signature,
            "legacy authentication field; authentication moved to LOGON in Bolt 5.1");
        RejectIfPresent(extra, "credentials", signature,
            "legacy authentication field; authentication moved to LOGON in Bolt 5.1");
        RejectIfPresent(extra, "principal", signature,
            "legacy authentication field; authentication moved to LOGON in Bolt 5.1");
        RejectIfPresent(extra, "patch_bolt", signature,
            "protocol patching was removed after Bolt 4.4");

        var schemeValue = BoltMessageFields.GetValue(extra, "user_agent");
        if (schemeValue is null)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.MissingRequiredField,
                "Message HELLO field 'extra.user_agent' is required.",
                signature);
        }

        if (schemeValue is not PackStreamString)
        {
            throw InvalidField(signature, "extra.user_agent", "a string");
        }

        ValidateOptionalMapOrNull(extra, "routing", signature);
        if (BoltMessageFields.GetValue(extra, "routing") is PackStreamMap routingMap)
        {
            // The specification types the routing context as Dictionary(address::String): map
            // entries carry string values (null routing itself is a valid "no routing" value).
            for (var i = 0; i < routingMap.Entries.Count; i++)
            {
                if (routingMap.Entries[i].Value is not PackStreamString)
                {
                    throw new BoltMessageException(
                        BoltMessageErrorCategory.InvalidFieldType,
                        $"Message {Name(signature)} field 'routing[{i}]' must be a string.",
                        signature);
                }
            }
        }

        ValidateOptionalStringOrNull(extra, "notifications_minimum_severity", signature);
        ValidateOptionalStringListOrNull(extra, "notifications_disabled_classifications", signature);

        if (BoltMessageFields.GetValue(extra, "bolt_agent") is { } agent)
        {
            if (agent is not PackStreamMap agentMap)
            {
                throw InvalidField(signature, "extra.bolt_agent", "a map");
            }

            // The specification marks bolt_agent.product as mandatory when the agent is present;
            // the remaining entries are optional strings (null permitted).
            RequireStringField(agentMap, "product", signature, "extra.bolt_agent");
            ValidateOptionalStringOrNull(agentMap, "platform", signature);
            ValidateOptionalStringOrNull(agentMap, "language", signature);
            ValidateOptionalStringOrNull(agentMap, "language_details", signature);
        }
    }

    private void ValidateRunExtra(PackStreamMap extra, byte signature)
    {
        RejectIfPresent(extra, "notifications_disabled_categories", signature,
            "renamed to notifications_disabled_classifications in Bolt 5.6 and removed after Bolt 5.4");

        ValidateOptionalStringListOrNull(extra, "bookmarks", signature);
        ValidateOptionalIntegerOrNull(extra, "tx_timeout", signature);
        ValidateOptionalMapOrNull(extra, "tx_metadata", signature);
        ValidateOptionalStringOrNull(extra, "mode", signature);
        ValidateOptionalStringOrNull(extra, "db", signature);
        ValidateOptionalStringOrNull(extra, "imp_user", signature);
        ValidateOptionalStringOrNull(extra, "notifications_minimum_severity", signature);
        ValidateOptionalStringListOrNull(extra, "notifications_disabled_classifications", signature);
    }

    private void ValidateSuccessMetadata(PackStreamMap metadata)
    {
        RejectIfPresent(metadata, "patch_bolt", BoltMessageSignatures.Success,
            "protocol patching was removed after Bolt 4.4");

        ValidateOptionalStringOrNull(metadata, "server", BoltMessageSignatures.Success);
        ValidateOptionalStringOrNull(metadata, "connection_id", BoltMessageSignatures.Success);
        ValidateOptionalMapOrNull(metadata, "hints", BoltMessageSignatures.Success);
        ValidateOptionalStringOrNull(metadata, "protocol_version", BoltMessageSignatures.Success);
        ValidateOptionalStringListOrNull(metadata, "fields", BoltMessageSignatures.Success);
        ValidateOptionalIntegerOrNull(metadata, "t_first", BoltMessageSignatures.Success);
        ValidateOptionalIntegerOrNull(metadata, "qid", BoltMessageSignatures.Success);
        ValidateOptionalStringOrNull(metadata, "db", BoltMessageSignatures.Success);
        ValidateOptionalBooleanOrNull(metadata, "has_more", BoltMessageSignatures.Success);
        ValidateOptionalStringOrNull(metadata, "advertised_address", BoltMessageSignatures.Success);
        ValidateOptionalBooleanOrNull(metadata, "credentials_expired", BoltMessageSignatures.Success);
    }

    private void ValidateFailureMetadata(PackStreamMap metadata, int depth)
    {
        if (depth > _limits.MaxDepth)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.InvalidPackStreamValue,
                $"FAILURE metadata cause nesting exceeds the configured maximum depth of {_limits.MaxDepth}.",
                BoltMessageSignatures.Failure);
        }

        RejectIfPresent(metadata, "code", BoltMessageSignatures.Failure,
            "the pre-5.7 'code' field is obsolete; Bolt 5.7+ uses gql_status and neo4j_code");

        ValidateOptionalStringOrNull(metadata, "gql_status", BoltMessageSignatures.Failure);
        ValidateOptionalStringOrNull(metadata, "neo4j_code", BoltMessageSignatures.Failure);
        ValidateOptionalStringOrNull(metadata, "description", BoltMessageSignatures.Failure);
        ValidateOptionalStringOrNull(metadata, "message", BoltMessageSignatures.Failure);
        ValidateOptionalMapOrNull(metadata, "diagnostic_record", BoltMessageSignatures.Failure);

        // Causes are nested failure-metadata maps, validated recursively. The PackStream reader
        // already bounds nesting on the decode path; this bound covers caller-built maps on the
        // encode path.
        if (BoltMessageFields.GetValue(metadata, "cause") is { } cause)
        {
            if (cause is not PackStreamMap causeMap)
            {
                throw InvalidField(BoltMessageSignatures.Failure, "cause", "a map");
            }

            ValidateFailureMetadata(causeMap, depth + 1);
        }
    }

    // ---- Validation helpers ----

    private void RejectIfPresent(PackStreamMap map, string key, byte signature, string reason)
    {
        if (BoltMessageFields.GetValue(map, key) is not null)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.ProhibitedField,
                $"Message {Name(signature)} carries the field '{key}', which is prohibited in Bolt 5.8 ({reason}).",
                signature);
        }
    }

    private void RequireStringField(PackStreamMap map, string key, byte signature, string container)
    {
        var value = BoltMessageFields.GetValue(map, key);
        if (value is null)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.MissingRequiredField,
                $"Message {Name(signature)} field '{container}.{key}' is required.",
                signature);
        }

        if (value is not PackStreamString)
        {
            throw InvalidField(signature, $"{container}.{key}", "a string");
        }
    }

    private void ValidateOptionalStringOrNull(PackStreamMap map, string key, byte signature)
    {
        if (BoltMessageFields.GetValue(map, key) is { } value && value is not PackStreamString)
        {
            throw InvalidField(signature, key, "a string or null");
        }
    }

    private void ValidateOptionalIntegerOrNull(PackStreamMap map, string key, byte signature)
    {
        if (BoltMessageFields.GetValue(map, key) is { } value && value is not PackStreamInteger)
        {
            throw InvalidField(signature, key, "an integer or null");
        }
    }

    private void ValidateOptionalBooleanOrNull(PackStreamMap map, string key, byte signature)
    {
        if (BoltMessageFields.GetValue(map, key) is { } value && value is not PackStreamBoolean)
        {
            throw InvalidField(signature, key, "a Boolean or null");
        }
    }

    private void ValidateOptionalMapOrNull(PackStreamMap map, string key, byte signature)
    {
        if (BoltMessageFields.GetValue(map, key) is { } value && value is not PackStreamMap)
        {
            throw InvalidField(signature, key, "a map or null");
        }
    }

    private void ValidateOptionalStringListOrNull(PackStreamMap map, string key, byte signature)
    {
        if (BoltMessageFields.GetValue(map, key) is not { } value)
        {
            return;
        }

        if (value is not PackStreamList list)
        {
            throw InvalidField(signature, key, "a list of strings or null");
        }

        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is not PackStreamString)
            {
                throw new BoltMessageException(
                    BoltMessageErrorCategory.InvalidFieldType,
                    $"Message {Name(signature)} field '{key}[{i}]' must be a string.",
                    signature);
            }
        }
    }

    private PackStreamMap RequireMap(PackStreamStructure structure, int index, string fieldName)
    {
        if (structure.Fields[index] is not PackStreamMap map)
        {
            throw InvalidField(structure.Tag, fieldName, "a map");
        }

        return map;
    }

    private static BoltMessageException InvalidField(byte signature, string fieldName, string expected) =>
        new(
            BoltMessageErrorCategory.InvalidFieldType,
            $"Message {Name(signature)} field '{fieldName}' must be {expected}.",
            signature);

    private static void RequireArity(PackStreamStructure structure, int expected)
    {
        if (structure.Fields.Count != expected)
        {
            throw new BoltMessageException(
                BoltMessageErrorCategory.StructureArity,
                $"Message {Name(structure.Tag)} must have exactly {expected} field(s) but the structure declares {structure.Fields.Count}.",
                structure.Tag);
        }
    }

    private static string Name(byte signature) =>
        BoltMessageSignatures.TryGetName(signature) ?? $"0x{signature:X2}";

    private static string TypeName(PackStreamValue? value) =>
        value is null ? "null value" : value.GetType().Name;
}
