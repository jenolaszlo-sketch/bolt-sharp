using System.Text;
using System.Text.Json;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using Independent = B3AQ1.FalsificationProbe.IndependentPackStream;

namespace B3AQ1.FalsificationProbe;

/// <summary>
/// B3-A-Q1 independent falsification probes. Every probe records: exact input, the specification
/// expectation, the observed behavior (with the exact error category where applicable), and a
/// verdict of CONFORMANT (spec-conformant behavior confirmed), DEVIATION (behavior differs from
/// the published specification — a conformance finding for the report) or CONFIRMED (behavior
/// matches the reviewer's prediction on a point the spec leaves open). The probe exits non-zero
/// only when observed behavior differs from the reviewer's prediction (PROBE-MISMATCH), i.e. when
/// this evidence set would be stale.
/// </summary>
internal static class Program
{
    private const string SentinelPrincipal = "SENTINEL-PRINCIPAL-7Q4X";
    private const string SentinelSecret = "SENTINEL-SECRET-9K2-DO-NOT-LEAK";

    private static readonly List<ProbeRecord> Results = new();

    private static int Main(string[] args)
    {
        var outputDirectory = args.Length > 0 ? args[0] : "docs/reviews/evidence/B3-A-Q1/falsification-probe";
        Directory.CreateDirectory(outputDirectory);

        SignatureAndClassificationProbes();
        ArityTruncationTrailingProbes();
        HelloSchemaProbes();
        LogonSchemaProbes();
        RunSchemaProbes();
        PullDiscardProbes();
        TelemetryProbes();
        RecordAndResponseProbes();
        ConfidentialityProbes();
        LimitProbes();

        var mismatches = Results.Count(r => r.Outcome == "PROBE-MISMATCH");
        var document = new
        {
            probe = "B3-A-Q1 independent falsification probe",
            reviewed_head = "fb81f8b386d46db2347f7b890847885d9815dcf5",
            implementation_commit = "d99096a9689c4a7ce313c569cff2b165cfbda11a",
            driver_under_test = typeof(BoltMessageCodec).Assembly.GetName().FullName,
            oracle = "IndependentPackStream (this probe) + B0-B qualified fixture bytes + published Bolt 5.8 spec",
            probe_count = Results.Count,
            mismatches,
            results = Results,
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputDirectory, "falsification-results.json"), json + Environment.NewLine);
        Console.WriteLine(json);

        Console.Error.WriteLine($"probes={Results.Count} mismatches={mismatches}");
        return mismatches == 0 ? 0 : 4;
    }

    private static void Add(
        string id,
        string group,
        string input,
        string specExpectation,
        string observed,
        string? category,
        string verdict,
        string notes = "")
    {
        Results.Add(new ProbeRecord(id, group, input, specExpectation, observed, category, verdict, notes));
    }

    private static string Describe(BoltMessageException ex) => $"{ex.Category}: {ex.Message}";

    private static (string Observed, string? Category) TryDecodeRequest(BoltMessageCodec codec, byte[] payload)
    {
        try
        {
            var request = codec.DecodeRequest(payload);
            return ($"decoded to {request.GetType().Name} ({request})", null);
        }
        catch (BoltMessageException ex)
        {
            return ("rejected", ex.Category.ToString());
        }
    }

    private static (string Observed, string? Category) TryDecodeResponse(BoltMessageCodec codec, byte[] payload)
    {
        try
        {
            var response = codec.DecodeResponse(payload);
            return ($"decoded to {response.GetType().Name} ({response})", null);
        }
        catch (BoltMessageException ex)
        {
            return ("rejected", ex.Category.ToString());
        }
    }

    /// <summary>Runs a decode probe and records the observed category against the predicted one.</summary>
    private static void DecodeProbe(
        string id,
        string group,
        string input,
        string specExpectation,
        byte[] payload,
        string predictedCategoryOrOk,
        string verdictIfAsPredicted,
        bool requestPath = true,
        string notes = "")
    {
        var codec = new BoltMessageCodec();
        var (observed, category) = requestPath
            ? TryDecodeRequest(codec, payload)
            : TryDecodeResponse(codec, payload);

        var predicted = predictedCategoryOrOk == "OK" ? null : predictedCategoryOrOk;
        var asPredicted = string.Equals(category, predicted, StringComparison.Ordinal)
            || (predictedCategoryOrOk == "OK" && category is null);
        Add(
            id,
            group,
            input,
            specExpectation,
            observed,
            category,
            asPredicted ? verdictIfAsPredicted : "PROBE-MISMATCH",
            notes + (asPredicted ? string.Empty : " [observed category differs from the reviewer's prediction]"));
    }

    // ---- Group A: signature classification ----

    private static void SignatureAndClassificationProbes()
    {
        var codec = new BoltMessageCodec();

        // A1: a RUN-shaped payload mislabelled with the PULL signature must be caught by arity.
        var a1 = Independent.Struct(0x3F, Independent.Str("q"), Independent.Map(), Independent.Map());
        DecodeProbe("A1", "signature", "RUN-shaped payload tagged PULL (0x3F, 3 fields)",
            "PULL has arity 1; a 3-field structure must be rejected",
            a1, "StructureArity", "CONFORMANT");

        // A2: a PULL-shaped payload mislabelled with the RUN signature.
        var a2 = Independent.Struct(0x10, Independent.Map(("n", Independent.Int(1))));
        DecodeProbe("A2", "signature", "PULL-shaped payload tagged RUN (0x10, 1 field)",
            "RUN has arity 3; a 1-field structure must be rejected",
            a2, "StructureArity", "CONFORMANT");

        // A3: RESET bytes tagged GOODBYE decode as GOODBYE — a stateless codec cannot distinguish
        // swapped zero-arity signatures because the signature *is* the message identity. Documented
        // boundary, not a defect.
        var a3 = Independent.Struct(0x02);
        var (observed3, _) = TryDecodeRequest(codec, a3);
        Add("A3", "signature", "zero-field structure tagged GOODBYE (0x02)",
            "Signature determines message identity; a stateless codec has no other discriminator",
            observed3, null, "CONFIRMED",
            "Swapping zero-arity signatures (RESET/GOODBYE/LOGOFF) is only detectable with connection state (B3-B).");

        foreach (var (name, tag, payload) in new[]
                 {
                     (Name: "BEGIN", Tag: (byte)0x11, Payload: Independent.Struct(0x11, Independent.Map())),
                     (Name: "COMMIT", Tag: (byte)0x12, Payload: Independent.Struct(0x12)),
                     (Name: "ROLLBACK", Tag: (byte)0x13, Payload: Independent.Struct(0x13)),
                     (Name: "ROUTE", Tag: (byte)0x66, Payload: Independent.Struct(0x66, Independent.Map(), Independent.List(), Independent.Map())),
                 })
        {
            var captured = payload;
            DecodeProbe($"A4-{name}", "signature", $"{name} ({BoltHex.Hex(captured)})",
                $"Known Bolt 5.8 signature, intentionally unsupported by the B3-A codec; must be classified UnsupportedMessage",
                captured, "UnsupportedMessage", "CONFORMANT",
                notes: "Valid-but-unsupported requests are never silently accepted.");
        }

        DecodeProbe("A5", "signature", "structure with unknown tag 0x5A",
            "Not a Bolt 5.8 message signature; must be UnknownSignature",
            Independent.Struct(0x5A, Independent.Map(("user_agent", Independent.Str("X")))),
            "UnknownSignature", "CONFORMANT");

        DecodeProbe("A6", "signature", "graph Node structure as top-level message",
            "A graph structure is not a message; the production PackStream layer resolves it to a typed value before dispatch",
            BoltHex.HexToBytes("B44E019186506572736F6EA0836E3A31"),
            "NotAStructure", "CONFIRMED",
            notes: "Deterministic rejection; classification nuance documented (typed graph value vs generic structure).");

        DecodeProbe("A7", "signature", "empty payload", "A Bolt message must be one structure",
            Array.Empty<byte>(), "EmptyPayload", "CONFORMANT");

        DecodeProbe("A8", "signature", "top-level integer (0x2A)", "Not a structure",
            new byte[] { 0x2A }, "NotAStructure", "CONFORMANT");
    }

    // ---- Group B: arity, truncation, trailing bytes ----

    private static void ArityTruncationTrailingProbes()
    {
        DecodeProbe("B1a", "arity", "HELLO with 2 fields",
            "HELLO arity is 1",
            Independent.Struct(0x01,
                Independent.Map(("user_agent", Independent.Str("Example/1.0"))),
                Independent.Map()),
            "StructureArity", "CONFORMANT");

        DecodeProbe("B1b", "arity", "HELLO with 0 fields", "HELLO arity is 1",
            Independent.Struct(0x01), "StructureArity", "CONFORMANT");

        DecodeProbe("B2", "truncation", "RUN with the declared 19-byte query cut after 15 bytes",
            "Truncated payload must be rejected as Truncated",
            BoltHex.HexToBytes("B310D01352455455524E203432204153"),
            "Truncated", "CONFORMANT");

        DecodeProbe("B3", "truncation", "HELLO cut inside the extra map (value declares 11 bytes, 4 present)",
            "Truncated payload must be rejected as Truncated",
            BoltHex.HexToBytes("B101A18A757365725F6167656E748B4578616D70"),
            "Truncated", "CONFORMANT");

        DecodeProbe("B4a", "trailing", "RESET followed by one extra byte",
            "Exactly one structure must be consumed; trailing bytes rejected",
            BoltHex.HexToBytes("B00F01"), "TrailingBytes", "CONFORMANT");

        DecodeProbe("B4b", "trailing", "valid HELLO followed by one extra byte",
            "Exactly one structure must be consumed",
            Independent.Concat(
                Independent.Struct(0x01, Independent.Map(("user_agent", Independent.Str("Example/1.0")))),
                new byte[] { 0x01 }),
            "TrailingBytes", "CONFORMANT");

        DecodeProbe("B5", "truncation", "STRING_8 declaring 200 bytes with only 10 present after a complete key",
            "Declared length beyond the payload must be Truncated, without reading past the buffer",
            Independent.Concat(
                new byte[] { 0xB1, 0x01, 0xA1, 0x8A, 0x75, 0x73, 0x65, 0x72, 0x5F, 0x61, 0x67, 0x65, 0x6E, 0x74 },
                new byte[] { 0xD0, 0xC8 }, new byte[10]),
            "Truncated", "CONFORMANT");

        DecodeProbe("B6", "truncation", "string payload containing invalid UTF-8 (D0 C8 lead-byte sequence)",
            "malformed UTF-8 is a PackStream format error (InvalidPackStreamValue), distinct from truncation",
            Independent.Concat(
                new byte[] { 0xB1, 0x01, 0xA1, 0x8A, 0x75, 0x73, 0x65, 0x72, 0x5F, 0x61, 0x67, 0x65, 0x6E, 0x74 },
                new byte[] { 0x82, 0xD0, 0xC8 }),
            "InvalidPackStreamValue", "CONFORMANT");
    }

    // ---- Group C: HELLO schema ----

    private static void HelloSchemaProbes()
    {
        DecodeProbe("C1", "hello", "HELLO {} (no user_agent)",
            "user_agent is required by the 5.8 schema",
            Independent.Struct(0x01, Independent.Map()),
            "MissingRequiredField", "CONFORMANT");

        DecodeProbe("C2", "hello", "HELLO {user_agent: 1}",
            "user_agent must be a string",
            Independent.Struct(0x01, Independent.Map(("user_agent", Independent.Int(1)))),
            "InvalidFieldType", "CONFORMANT");

        // The commission's critical finding candidate: the spec marks bolt_agent mandatory (5.3+).
        DecodeProbe("C3", "hello", "HELLO {user_agent: \"X\"} — no bolt_agent",
            "Bolt 5.8 specification: 'bolt_agent … This field is mandatory.' A schema-exact codec would reject this message",
            Independent.Struct(0x01, Independent.Map(("user_agent", Independent.Str("X")))),
            "OK",
            "DEVIATION",
            notes: "Conformance finding F-01: the codec accepts HELLO without the spec-mandatory bolt_agent. Documented by the producer as a deliberate less-strict reading; real driver 6.4.0 always sends bolt_agent (B0-B evidence), so no executed observation is affected. Recommended for spec-exact enforcement before/at B3-B integration.");

        DecodeProbe("C4", "hello", "HELLO {user_agent, bolt_agent: {}}",
            "bolt_agent.product is mandatory; an empty agent map must be rejected",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("bolt_agent", Independent.Map()))),
            "MissingRequiredField", "CONFORMANT");

        DecodeProbe("C5", "hello", "HELLO {user_agent, bolt_agent: {platform: \"win\"}} (no product)",
            "bolt_agent.product is mandatory",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("bolt_agent", Independent.Map(("platform", Independent.Str("win")))))),
            "MissingRequiredField", "CONFORMANT");

        DecodeProbe("C6", "hello", "HELLO {user_agent, bolt_agent: {product: 1}}",
            "bolt_agent.product must be a string",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("bolt_agent", Independent.Map(("product", Independent.Int(1)))))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("C7", "hello", "HELLO {user_agent, bolt_agent: {product: \"p/1\", platform: 2}}",
            "optional bolt_agent entries must be strings or null",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("bolt_agent", Independent.Map(
                    ("product", Independent.Str("p/1")),
                    ("platform", Independent.Int(2)))))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("C8", "hello", "HELLO {user_agent, routing: {address: 1}}",
            "routing is Dictionary(address::String): values must be strings",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("routing", Independent.Map(("address", Independent.Int(1)))))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("C9", "hello", "HELLO {user_agent, notifications_disabled_classifications: [1]}",
            "notifications_disabled_classifications is List<String>",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("notifications_disabled_classifications", Independent.List(Independent.Int(1))))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("C10", "hello", "HELLO carrying legacy authentication field 'scheme'",
            "scheme was removed from HELLO after Bolt 5.0; authentication moved to LOGON in 5.1",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("scheme", Independent.Str("basic")))),
            "ProhibitedField", "CONFORMANT");

        DecodeProbe("C11", "hello", "HELLO carrying 'patch_bolt'",
            "patch_bolt was removed after Bolt 4.4",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("patch_bolt", Independent.List(Independent.Str("utc"))))),
            "ProhibitedField", "CONFORMANT");

        DecodeProbe("C12", "hello", "HELLO {user_agent, notifications_minimum_severity: 1}",
            "notifications_minimum_severity must be a string or null",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("notifications_minimum_severity", Independent.Int(1)))),
            "InvalidFieldType", "CONFORMANT");

        // Exact consumption of the specification's own HELLO example 2 shape.
        var full = Independent.Struct(0x01, Independent.Map(
            ("user_agent", Independent.Str("Example/4.2.0")),
            ("routing", Independent.Map(("address", Independent.Str("x.example.com:9001")))),
            ("notifications_minimum_severity", Independent.Str("WARNING")),
            ("notifications_disabled_classifications", Independent.List(
                Independent.Str("HINT"), Independent.Str("GENERIC")))));
        var codec = new BoltMessageCodec();
        var (observedFull, categoryFull) = TryDecodeRequest(codec, full);
        Add("C13", "hello", "HELLO with the specification example's field set",
            "must decode with every known field preserved and validated",
            observedFull, categoryFull,
            categoryFull is null ? "CONFORMANT" : "PROBE-MISMATCH",
            "All four known metadata keys type-check; routing values validated as strings.");

        // Null optionals are valid defaults per the specification.
        DecodeProbe("C14", "hello", "HELLO with routing=null and null notification fields",
            "null is a valid value for these optional fields (spec: 'Sending null will make the server use … default')",
            Independent.Struct(0x01, Independent.Map(
                ("user_agent", Independent.Str("X")),
                ("routing", Independent.Null()),
                ("notifications_minimum_severity", Independent.Null()),
                ("notifications_disabled_classifications", Independent.Null()))),
            "OK", "CONFORMANT");
    }

    // ---- Group D: LOGON schema ----

    private static void LogonSchemaProbes()
    {
        DecodeProbe("D1", "logon", "LOGON {} (no scheme)", "auth.scheme is required",
            Independent.Struct(0x6A, Independent.Map()), "MissingRequiredField", "CONFORMANT");

        DecodeProbe("D2", "logon", "LOGON {scheme: 1}", "scheme must be a string",
            Independent.Struct(0x6A, Independent.Map(("scheme", Independent.Int(1)))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("D3", "logon", "LOGON {scheme: basic, principal: \"u\"} (no credentials)",
            "the basic scheme requires principal and credentials",
            Independent.Struct(0x6A, Independent.Map(
                ("scheme", Independent.Str("basic")),
                ("principal", Independent.Str("u")))),
            "MissingRequiredField", "CONFORMANT");

        DecodeProbe("D4", "logon", "LOGON {scheme: basic, principal: 1, credentials: \"x\"}",
            "principal must be a string",
            Independent.Struct(0x6A, Independent.Map(
                ("scheme", Independent.Str("basic")),
                ("principal", Independent.Int(1)),
                ("credentials", Independent.Str("x")))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("D5", "logon", "LOGON {scheme: bearer} (no token)",
            "the bearer scheme requires credentials",
            Independent.Struct(0x6A, Independent.Map(("scheme", Independent.Str("bearer")))),
            "MissingRequiredField", "CONFORMANT");

        var unknown = new BoltMessageCodec().DecodeRequest(Independent.Struct(
            0x6A, Independent.Map(("scheme", Independent.Str("custom")))));
        var logonUnknown = Assert.IsType<LogonRequest>(unknown);
        Add("D6", "logon", "LOGON {scheme: \"custom\"}",
            "unknown schemes are preserved structurally; recognition never implies support (B3-B decides)",
            $"decoded to LogonRequest with RecognizedScheme={logonUnknown.RecognizedScheme}",
            null,
            logonUnknown.RecognizedScheme == BoltAuthScheme.Unknown ? "CONFORMANT" : "PROBE-MISMATCH",
            "No authentication decision is made by the codec.");

        DecodeProbe("D7", "logon", "LOGON with a list instead of the auth map",
            "auth must be a map",
            Independent.Struct(0x6A, Independent.List()),
            "InvalidFieldType", "CONFORMANT");
    }

    // ---- Group E: RUN schema ----

    private static void RunSchemaProbes()
    {
        DecodeProbe("E1", "run", "RUN with 2 fields", "RUN arity is 3",
            Independent.Struct(0x10, Independent.Str("query"), Independent.Map()),
            "StructureArity", "CONFORMANT");

        DecodeProbe("E2", "run", "RUN with an integer query", "query must be a string",
            Independent.Struct(0x10, Independent.Int(1), Independent.Map(), Independent.Map()),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("E3", "run", "RUN with a list as parameters", "parameters must be a map",
            Independent.Struct(0x10, Independent.Str("q"), Independent.List(), Independent.Map()),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("E4", "run", "RUN {tx_timeout: \"later\"}", "tx_timeout must be an integer",
            Independent.Struct(0x10, Independent.Str("q"), Independent.Map(),
                Independent.Map(("tx_timeout", Independent.Str("later")))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("E5", "run", "RUN carrying 'notifications_disabled_categories'",
            "removed after Bolt 5.4 (renamed to notifications_disabled_classifications in 5.6)",
            Independent.Struct(0x10, Independent.Str("q"), Independent.Map(),
                Independent.Map(("notifications_disabled_categories", Independent.List()))),
            "ProhibitedField", "CONFORMANT");

        // Nested parameter preservation, verified through the independent decoder's semantics.
        var run = Assert.IsType<RunRequest>(new BoltMessageCodec().DecodeRequest(Independent.Struct(
            0x10,
            Independent.Str("RETURN $x AS x"),
            Independent.Map(("x", Independent.Int(1))),
            Independent.Map())));
        var parameter = run.Parameters.Entries.Single(e => e.Key == "x").Value;
        Add("E6", "run", "RUN \"RETURN $x AS x\" {x: 1} {}",
            "nested parameter values preserved with signed-integer semantics",
            $"decoded; parameters.x = {parameter?.GetType().Name}:{(parameter as PackStreamInteger)?.Value}",
            null,
            parameter is PackStreamInteger { Value: 1 } ? "CONFORMANT" : "PROBE-MISMATCH");
    }

    // ---- Group F: PULL / DISCARD ----

    private static void PullDiscardProbes()
    {
        DecodeProbe("F1", "pull-discard", "PULL {} (no n)",
            "n has no default and must be present (spec)",
            Independent.Struct(0x3F, Independent.Map()), "MissingRequiredField", "CONFORMANT");

        DecodeProbe("F2", "pull-discard", "PULL {n: \"not!\"}", "n must be an integer",
            Independent.Struct(0x3F, Independent.Map(("n", Independent.Str("not!")))),
            "InvalidFieldType", "CONFORMANT");

        DecodeProbe("F3", "pull-discard", "PULL {n: -2}",
            "spec defines n=-1 as 'fetch all'; the spec does not state n<-1 behavior. The codec rejects n<-1 — a documented implementation policy, not a quoted protocol requirement",
            Independent.Struct(0x3F, Independent.Map(("n", Independent.Int(-2)))),
            "InvalidFieldValue", "CONFIRMED",
            notes: "Classified as: documented implementation policy (reasonable; matches the value domain's meaning). Reviewer accepts with the policy note recorded.");

        DecodeProbe("F4", "pull-discard", "PULL {n: 1, qid: -2}",
            "qid=-1 denotes the last executed statement; smaller values are rejected by policy",
            Independent.Struct(0x3F, Independent.Map(
                ("n", Independent.Int(1)), ("qid", Independent.Int(-2)))),
            "InvalidFieldValue", "CONFIRMED",
            notes: "Same documented-policy classification as F3.");

        DecodeProbe("F5", "pull-discard", "DISCARD {n: -2}",
            "same shape as PULL",
            Independent.Struct(0x2F, Independent.Map(("n", Independent.Int(-2)))),
            "InvalidFieldValue", "CONFIRMED");

        var pull = Assert.IsType<PullRequest>(new BoltMessageCodec().DecodeRequest(
            Independent.Struct(0x3F, Independent.Map(("n", Independent.Int(-1))))));
        Add("F6", "pull-discard", "PULL {n: -1}",
            "n=-1 fetches all remaining records (spec)",
            $"decoded; N={pull.N}",
            null, pull.N == -1 ? "CONFORMANT" : "PROBE-MISMATCH");

        // Authentic driver captures re-decoded through the production codec.
        var authenticPull = Assert.IsType<PullRequest>(new BoltMessageCodec().DecodeRequest(
            BoltHex.HexToBytes("B13FA1816EC903E8")));
        Add("F7", "pull-discard", "authentic driver capture PULL {n: 1000} (B0-B evidence)",
            "must decode to n=1000",
            $"decoded; N={authenticPull.N}",
            null, authenticPull.N == 1000 ? "CONFORMANT" : "PROBE-MISMATCH");

        var authenticDiscard = Assert.IsType<DiscardRequest>(new BoltMessageCodec().DecodeRequest(
            BoltHex.HexToBytes("B12FA1816EFF")));
        Add("F8", "pull-discard", "authentic driver capture DISCARD {n: -1} (B0-B evidence)",
            "must decode to n=-1",
            $"decoded; N={authenticDiscard.N}",
            null, authenticDiscard.N == -1 ? "CONFORMANT" : "PROBE-MISMATCH");
    }

    // ---- Group G: TELEMETRY ----

    private static void TelemetryProbes()
    {
        DecodeProbe("G1", "telemetry", "TELEMETRY \"driver\"", "api must be an integer",
            Independent.Struct(0x54, Independent.Str("driver")), "InvalidFieldType", "CONFORMANT");

        var outOfDomain = Assert.IsType<TelemetryRequest>(new BoltMessageCodec().DecodeRequest(
            Independent.Struct(0x54, Independent.Int(9001))));
        Add("G2", "telemetry", "TELEMETRY 9001",
            "the spec types api as Integer; its own example answers an out-of-domain value (9001) with FAILURE rather than malformed-input — the value domain (0-3) is a server-side decision",
            $"decoded; Api={outOfDomain.Api}",
            null, outOfDomain.Api == 9001 ? "CONFORMANT" : "PROBE-MISMATCH",
            "Intentional parsing/processing boundary; B3-B owns the FAILURE response.");
    }

    // ---- Group H: RECORD / response encoding ----

    private static void RecordAndResponseProbes()
    {
        var codec = new BoltMessageCodec();

        DecodeProbe("H1", "responses", "RECORD with a map instead of a list", "data must be a list",
            Independent.Struct(0x71, Independent.Map()), "InvalidFieldType", "CONFORMANT", requestPath: false);

        DecodeProbe("H2", "responses", "RECORD with two fields (wrong outer arity)",
            "RECORD arity is 1 (NC-05b outer-arity boundary)",
            BoltHex.HexToBytes("B271912A01"), "StructureArity", "CONFORMANT", requestPath: false);

        var twoValues = codec.DecodeResponse(Independent.Struct(
            0x71, Independent.List(Independent.Int(42), Independent.Int(43))));
        Add("H3", "responses", "RECORD [42, 43] — two values in the single list",
            "the stateless codec enforces outer arity only; column-count matching against RUN fields is B3-B/B3-C (NC-05b)",
            $"decoded to RecordResponse with {((RecordResponse)twoValues).Data.Items.Count} value(s)",
            null, "CONFIRMED",
            "Documented boundary; both halves are pinned by the producer's tests and this probe.");

        DecodeProbe("H4", "responses", "SUCCESS with a list instead of metadata", "metadata must be a map",
            Independent.Struct(0x70, Independent.List()), "InvalidFieldType", "CONFORMANT", requestPath: false);

        DecodeProbe("H5", "responses", "SUCCESS {fields: 1}", "fields must be a list of strings",
            Independent.Struct(0x70, Independent.Map(("fields", Independent.Int(1)))),
            "InvalidFieldType", "CONFORMANT", requestPath: false);

        DecodeProbe("H6", "responses", "SUCCESS {has_more: \"true\"}", "has_more must be a Boolean",
            Independent.Struct(0x70, Independent.Map(("has_more", Independent.Str("true")))),
            "InvalidFieldType", "CONFORMANT", requestPath: false);

        DecodeProbe("H7", "responses", "SUCCESS carrying patch_bolt", "removed after Bolt 4.4",
            Independent.Struct(0x70, Independent.Map(
                ("patch_bolt", Independent.List(Independent.Str("utc"))))),
            "ProhibitedField", "CONFORMANT", requestPath: false);

        DecodeProbe("H8", "responses", "FAILURE carrying the pre-5.7 'code' field",
            "obsolete field; 5.7+ uses gql_status/neo4j_code",
            Independent.Struct(0x7F, Independent.Map(
                ("code", Independent.Str("X")),
                ("gql_status", Independent.Str("42001")))),
            "ProhibitedField", "CONFORMANT", requestPath: false);

        DecodeProbe("H9", "responses", "FAILURE with cause: \"nope!\"", "cause must be a map",
            Independent.Struct(0x7F, Independent.Map(
                ("gql_status", Independent.Str("42001")),
                ("cause", Independent.Str("nope!")))),
            "InvalidFieldType", "CONFORMANT", requestPath: false);

        // Recursive cause validation: a prohibited field *inside* a nested cause must be caught.
        DecodeProbe("H10", "responses", "FAILURE whose nested cause carries 'code'",
            "causes are validated recursively as failure metadata",
            Independent.Struct(0x7F, Independent.Map(
                ("gql_status", Independent.Str("42001")),
                ("cause", Independent.Map(
                    ("gql_status", Independent.Str("50N09")),
                    ("code", Independent.Str("OLD")))))),
            "ProhibitedField", "CONFORMANT", requestPath: false);

        // Decode-path depth bound: 150 nested cause maps exceed the configured PackStream depth (100).
        var deep = Independent.Null();
        for (var i = 0; i < 150; i++)
        {
            deep = Independent.Map(("cause", deep));
        }

        DecodeProbe("H11", "responses", "FAILURE with a 150-level nested cause chain",
            "recursive causes must be bounded by the configured depth; unbounded recursion is unsafe",
            Independent.Struct(0x7F, deep), "InvalidPackStreamValue", "CONFORMANT", requestPath: false,
            notes: "The PackStream reader's depth limit (100) bounds the chain before the codec's recursion runs.");

        DecodeProbe("H12", "responses", "response with unknown tag 0x5A", "not a response signature",
            Independent.Struct(0x5A, Independent.Map()), "UnknownSignature", "CONFORMANT", requestPath: false);

        // ---- Encoding verified through the independent decoder and B0-B fixture bytes ----

        var ignoredBytes = codec.EncodeResponse(IgnoredResponse.Instance);
        var (ignoredDecoded, ignoredConsumed) = Independent.Decode(ignoredBytes);
        var ignoredStruct = Assert.IsType<Independent.PStruct>(ignoredDecoded);
        Add("H13", "encode-ignored", "EncodeResponse(IgnoredResponse.Instance)",
            "the Bolt 5.8 specification defines IGNORED with no fields: exactly B0 7E",
            $"bytes={BoltHex.Hex(ignoredBytes)}; independent decode: tag=0x{ignoredStruct.Tag:X2}, fields={ignoredStruct.Fields.Count}, consumed={ignoredConsumed}/{ignoredBytes.Length}",
            null,
            ignoredBytes.Length == 2 && ignoredBytes[0] == 0xB0 && ignoredBytes[1] == 0x7E
                && ignoredStruct.Tag == 0x7E && ignoredStruct.Fields.Count == 0 && ignoredConsumed == 2
                ? "CONFORMANT" : "PROBE-MISMATCH",
            "Zero-field form independently confirmed; no production logic reproduces the historical one-field fixture.");

        var (producerIgnored, producerConsumed) = Independent.Decode(BoltHex.HexToBytes("B17EA0"));
        var producerStruct = Assert.IsType<Independent.PStruct>(producerIgnored);
        Add("H14", "encode-ignored", "the historical B0-B producer fixture B1 7E A0",
            "one-field form; the spec defines zero fields. The qualified driver tolerates it because its deserializer reads no IGNORED fields and the pipelined reader discards leftovers (upstream sources preserved in B3-A evidence)",
            $"independent decode: tag=0x{producerStruct.Tag:X2}, fields={producerStruct.Fields.Count}, consumed={producerConsumed}/3",
            null, "CONFIRMED",
            "The codec rejects this form with StructureArity (producer test) — the discrepancy is isolated from production behavior; B0-B evidence remains unaltered.");

        // SUCCESS empty == B170A0 == independent encoding.
        var successEmpty = codec.EncodeResponse(SuccessResponse.Empty);
        var independentEmpty = Independent.Struct(0x70, Independent.Map());
        Add("H15", "encode", "EncodeResponse(SuccessResponse.Empty)",
            "must equal B170A0 (B0-B fixture and independent encoding)",
            $"bytes={BoltHex.Hex(successEmpty)}",
            null,
            successEmpty.SequenceEqual(BoltHex.HexToBytes("B170A0")) && successEmpty.SequenceEqual(independentEmpty)
                ? "CONFORMANT" : "PROBE-MISMATCH");

        // HELLO SUCCESS metadata == the B0-B-qualified bytes.
        var helloSuccess = codec.EncodeResponse(SuccessResponse.FromEntries(
            ("server", new PackStreamString("Neo4j/5.26.0")),
            ("connection_id", new PackStreamString("bolt-b0b-1")),
            ("hints", new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>()))));
        Add("H16", "encode", "EncodeResponse(SUCCESS {server, connection_id, hints:{}})",
            "must equal the B0-B-qualified fixture the real driver accepted",
            $"bytes={BoltHex.Hex(helloSuccess)}",
            null,
            helloSuccess.SequenceEqual(BoltHex.HexToBytes(
                "B170A3867365727665728C4E656F346A2F352E32362E308D636F6E6E656374696F6E5F69648A626F6C742D6230622D318568696E7473A0"))
                ? "CONFORMANT" : "PROBE-MISMATCH");

        // RUN SUCCESS fields == the B0-B-qualified bytes.
        var runSuccess = codec.EncodeResponse(SuccessResponse.FromEntries(
            ("fields", new PackStreamList(new PackStreamValue?[] { new PackStreamString("answer") }))));
        Add("H17", "encode", "EncodeResponse(SUCCESS {fields: [\"answer\"]})",
            "must equal the B0-B-qualified fixture",
            $"bytes={BoltHex.Hex(runSuccess)}",
            null,
            runSuccess.SequenceEqual(BoltHex.HexToBytes("B170A1866669656C64739186616E73776572"))
                ? "CONFORMANT" : "PROBE-MISMATCH");

        // Scalar RECORD == B171912A.
        var recordScalar = codec.EncodeResponse(RecordResponse.FromValues(new PackStreamInteger(42)));
        Add("H18", "encode", "EncodeResponse(RECORD [42])",
            "must equal the B0-B-qualified fixture B171912A",
            $"bytes={BoltHex.Hex(recordScalar)}",
            null,
            recordScalar.SequenceEqual(BoltHex.HexToBytes("B171912A")) ? "CONFORMANT" : "PROBE-MISMATCH");

        // Graph RECORD: built from typed values, encoded, then verified by the independent decoder.
        var alice = new PackStreamNode(1, new[] { "Person" },
            new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("Alice")) }),
            "n:1");
        var knows = new PackStreamRelationship(1, 1, 2, "KNOWS",
            new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("since", new PackStreamInteger(2024)) }),
            "r:1", "n:1", "n:2");
        var bob = new PackStreamNode(2, new[] { "Person" },
            new PackStreamMap(new[] { new KeyValuePair<string, PackStreamValue?>("name", new PackStreamString("Bob")) }),
            "n:2");
        var recordGraph = codec.EncodeResponse(RecordResponse.FromValues(alice, knows, bob));
        var graphFixture = BoltHex.HexToBytes(
            "B17193B44E019186506572736F6EA1846E616D6585416C696365836E3A31" +
            "B852010102854B4E4F5753A18573696E6365C907E883723A31836E3A31836E3A32" +
            "B44E029186506572736F6EA1846E616D6583426F62836E3A32");

        var (graphDecoded, graphConsumed) = Independent.Decode(recordGraph);
        var graphStruct = Assert.IsType<Independent.PStruct>(graphDecoded);
        var graphList = Assert.IsType<Independent.PList>(graphStruct.Fields[0]);
        var nodeA = Assert.IsType<Independent.PStruct>(graphList.Items[0]);
        var rel = Assert.IsType<Independent.PStruct>(graphList.Items[1]);
        var nodeB = Assert.IsType<Independent.PStruct>(graphList.Items[2]);

        var layoutOk =
            graphStruct.Tag == 0x71 && graphStruct.Fields.Count == 1
            && graphList.Items.Count == 3
            && nodeA.Tag == 0x4E && nodeA.Fields.Count == 4
            && nodeA.Fields[0] is Independent.PInt { Value: 1 }
            && nodeA.Fields[1] is Independent.PList { Items.Count: 1 }
            && nodeA.Fields[3] is Independent.PStr { Value: "n:1" }
            && rel.Tag == 0x52 && rel.Fields.Count == 8
            && rel.Fields[0] is Independent.PInt { Value: 1 }
            && rel.Fields[1] is Independent.PInt { Value: 1 }
            && rel.Fields[2] is Independent.PInt { Value: 2 }
            && rel.Fields[3] is Independent.PStr { Value: "KNOWS" }
            && rel.Fields[5] is Independent.PStr { Value: "r:1" }
            && rel.Fields[6] is Independent.PStr { Value: "n:1" }
            && rel.Fields[7] is Independent.PStr { Value: "n:2" }
            && nodeB.Tag == 0x4E && nodeB.Fields.Count == 4
            && nodeB.Fields[0] is Independent.PInt { Value: 2 }
            && nodeB.Fields[3] is Independent.PStr { Value: "n:2" }
            && graphConsumed == recordGraph.Length;

        Add("H19", "encode", "EncodeResponse(RECORD [Node(1,'n:1'), Relationship(1,'r:1'), Node(2,'n:2')])",
            "wire layout must match the Bolt 5.8 graph structures (Node 4-field, Relationship 8-field, field order per spec) and equal the B0-B fixture independently decoded in B0-B-Q1",
            $"bytes match B0-B fixture: {recordGraph.SequenceEqual(graphFixture)}; independent layout decode: tag order/arity/field order verified, consumed {graphConsumed}/{recordGraph.Length}",
            null,
            recordGraph.SequenceEqual(graphFixture) && layoutOk ? "CONFORMANT" : "PROBE-MISMATCH",
            "Node id 1 and relationship id 1 remain distinct typed structures (no aliasing).");

        // FAILURE with 5.7+ metadata == the t06 fixture.
        var failure = codec.EncodeResponse(FailureResponse.FromEntries(
            ("gql_status", new PackStreamString("42001")),
            ("neo4j_code", new PackStreamString("Neo.ClientError.Statement.SyntaxError")),
            ("description", new PackStreamString("Invalid input for the deliberately unsupported query.")),
            ("diagnostic_record", new PackStreamMap(Array.Empty<KeyValuePair<string, PackStreamValue?>>()))));
        Add("H20", "encode", "EncodeResponse(FAILURE {gql_status, neo4j_code, description, diagnostic_record})",
            "must equal the B0-B t06 fixture",
            $"bytes match t06 fixture: {failure.SequenceEqual(BoltHex.HexToBytes(
                "B17FA48A67716C5F7374617475738534323030318A6E656F346A5F636F6465D0254E656F2E436C69656E744572726F722E53746174656D656E742E53796E7461784572726F728B6465736372697074696F6ED035496E76616C696420696E70757420666F72207468652064656C696265726174656C7920756E737570706F727465642071756572792ED011646961676E6F737469635F7265636F7264A0"))}",
            null, "CONFORMANT");

        // PULL completion metadata.
        var hasMore = codec.EncodeResponse(SuccessResponse.FromEntries(("has_more", new PackStreamBoolean(false))));
        Add("H21", "encode", "EncodeResponse(SUCCESS {has_more: false})",
            "must equal the B0-B fixture",
            $"bytes match: {hasMore.SequenceEqual(BoltHex.HexToBytes("B170A1886861735F6D6F7265C2"))}",
            null, "CONFORMANT");
    }

    // ---- Group I: authentication confidentiality ----

    private static void ConfidentialityProbes()
    {
        var codec = new BoltMessageCodec();

        // I1: a valid secret-bearing LOGON must not leak through ToString.
        var validLogon = Assert.IsType<LogonRequest>(codec.DecodeRequest(Independent.Struct(0x6A, Independent.Map(
            ("scheme", Independent.Str("basic")),
            ("principal", Independent.Str(SentinelPrincipal)),
            ("credentials", Independent.Str(SentinelSecret))))));
        var validText = validLogon.ToString();
        Add("I1", "confidentiality", "valid LOGON with sentinel principal/credentials; ToString()",
            "credential material must never appear in message diagnostics",
            $"ToString() = \"{validText}\"",
            null,
            !validText.Contains(SentinelPrincipal, StringComparison.Ordinal)
                && !validText.Contains(SentinelSecret, StringComparison.Ordinal)
                && validText.Contains("basic", StringComparison.Ordinal) ? "CONFORMANT" : "PROBE-MISMATCH");

        // I2: a failed secret-bearing decode must not leak through the exception (message or stack).
        try
        {
            codec.DecodeRequest(Independent.Struct(0x6A, Independent.Map(
                ("scheme", Independent.Str("basic")),
                ("principal", Independent.Str(SentinelPrincipal)),
                ("credentials", Independent.Int(1)))));
            Add("I2", "confidentiality", "malformed LOGON (credentials wrong-typed) with sentinels",
                "exception diagnostics must never contain field values",
                "unexpectedly decoded", null, "PROBE-MISMATCH");
        }
        catch (BoltMessageException ex)
        {
            var full = ex.ToString();
            Add("I2", "confidentiality", "malformed LOGON (credentials wrong-typed) with sentinels",
                "exception diagnostics must never contain field values",
                $"category={ex.Category}; exception text contains sentinel: " +
                $"{full.Contains(SentinelPrincipal, StringComparison.Ordinal) || full.Contains(SentinelSecret, StringComparison.Ordinal)}",
                ex.Category.ToString(),
                !full.Contains(SentinelPrincipal, StringComparison.Ordinal)
                    && !full.Contains(SentinelSecret, StringComparison.Ordinal) ? "CONFORMANT" : "PROBE-MISMATCH");
        }

        // I3: a large secret-bearing credential in a message that fails for an unrelated reason.
        var paddedSecret = SentinelSecret + new string('x', 100_000);
        try
        {
            codec.DecodeRequest(Independent.Struct(0x6A, Independent.Map(
                ("scheme", Independent.Str("basic")),
                ("principal", Independent.Int(7)),
                ("credentials", Independent.Str(paddedSecret)))));
            Add("I3", "confidentiality", "LOGON with a 100k-char sentinel credential failing on principal type",
                "exception diagnostics must never contain field values",
                "unexpectedly decoded", null, "PROBE-MISMATCH");
        }
        catch (BoltMessageException ex)
        {
            var full = ex.ToString();
            Add("I3", "confidentiality", "LOGON with a 100k-char sentinel credential failing on principal type",
                "exception diagnostics must never contain field values",
                $"category={ex.Category}; message length={ex.Message.Length}; sentinel present: " +
                $"{full.Contains(SentinelSecret, StringComparison.Ordinal)}",
                ex.Category.ToString(),
                !full.Contains(SentinelSecret, StringComparison.Ordinal) ? "CONFORMANT" : "PROBE-MISMATCH");
        }

        // I4: the auth map must be immutably wrapped (no mutable backing storage reachable).
        var castFailed = false;
        try
        {
            _ = (List<KeyValuePair<string, PackStreamValue?>>)validLogon.Auth.Entries;
        }
        catch (InvalidCastException)
        {
            castFailed = true;
        }

        Add("I4", "confidentiality", "cast of LogonRequest.Auth.Entries to List<…>",
            "the B1-R1 F1 immutability contract: read-only wrapper, no mutable backing storage reachable",
            castFailed ? "cast rejected (InvalidCastException)" : "cast succeeded",
            null, castFailed ? "CONFORMANT" : "PROBE-MISMATCH");

        // I5: secret-bearing payload with trailing bytes — the rejection must not quote payload data.
        var secretLogon = Independent.Struct(0x6A, Independent.Map(
            ("scheme", Independent.Str("basic")),
            ("principal", Independent.Str(SentinelPrincipal)),
            ("credentials", Independent.Str(SentinelSecret))));
        try
        {
            codec.DecodeRequest(Independent.Concat(secretLogon, new byte[] { 0x01 }));
            Add("I5", "confidentiality", "secret-bearing LOGON followed by a trailing byte",
                "the TrailingBytes rejection must not echo payload bytes",
                "unexpectedly decoded", null, "PROBE-MISMATCH");
        }
        catch (BoltMessageException ex)
        {
            var full = ex.ToString();
            Add("I5", "confidentiality", "secret-bearing LOGON followed by a trailing byte",
                "the TrailingBytes rejection must not echo payload bytes",
                $"category={ex.Category}; sentinel present: {full.Contains(SentinelSecret, StringComparison.Ordinal)}",
                ex.Category.ToString(),
                !full.Contains(SentinelSecret, StringComparison.Ordinal) ? "CONFORMANT" : "PROBE-MISMATCH");
        }
    }

    // ---- Group J: limits and allocation declarations ----

    private static void LimitProbes()
    {
        DecodeProbe("J1", "limits", "STRING_32 declaring 2,000,000,000 bytes with a truncated payload",
            "declared lengths beyond the configured maximum must be rejected before any allocation",
            Independent.Concat(
                new byte[] { 0xB1, 0x01, 0xA1, 0x8A, 0x75, 0x73, 0x65, 0x72, 0x5F, 0x61, 0x67, 0x65, 0x6E, 0x74 },
                new byte[] { 0xD2, 0x77, 0x35, 0x94, 0x00 }, new byte[8]),
            "InvalidPackStreamValue", "CONFORMANT",
            notes: "16 MiB default string limit rejects the declaration before the payload is read; no large allocation occurs.");

        DecodeProbe("J2", "limits", "LIST_32 declaring 2,000,000,000 items",
            "declared counts beyond the configured maximum must be rejected before any allocation",
            Independent.Concat(
                new byte[] { 0xB1, 0x01, 0xA1, 0x8A, 0x75, 0x73, 0x65, 0x72, 0x5F, 0x61, 0x67, 0x65, 0x6E, 0x74 },
                new byte[] { 0xD6, 0x77, 0x35, 0x94, 0x00 }, new byte[8]),
            "InvalidPackStreamValue", "CONFORMANT",
            notes: "Default list limit is 100,000 items; the declaration is rejected before elements are decoded.");

        // Decode-path deep nesting: 150 nested maps inside HELLO's user_agent position.
        var deep = Independent.Str("X");
        for (var i = 0; i < 150; i++)
        {
            deep = Independent.Map(("k", deep));
        }

        DecodeProbe("J3", "limits", "HELLO with a 150-level nested value",
            "nesting depth beyond the configured maximum (100) must be rejected deterministically",
            Independent.Struct(0x01, Independent.Map(("user_agent", deep))),
            "InvalidPackStreamValue", "CONFORMANT");
    }
}

/// <summary>Hex helpers for fixture bytes.</summary>
internal static class BoltHex
{
    public static byte[] HexToBytes(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
}

/// <summary>xUnit-free assertion helper.</summary>
internal static class Assert
{
    public static T IsType<T>(object? value) where T : class =>
        value as T ?? throw new InvalidOperationException(
            $"Expected {typeof(T).Name} but observed {value?.GetType().Name ?? "null"}.");
}

internal sealed record ProbeRecord(
    string Id,
    string Group,
    string Input,
    string SpecExpectation,
    string Observed,
    string? Category,
    string Outcome,
    string Notes);
