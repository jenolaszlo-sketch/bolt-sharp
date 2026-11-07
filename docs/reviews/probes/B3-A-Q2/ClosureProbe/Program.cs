using System.Text.Json;
using BoltSharp.Messages;
using F = B3AQ2.ClosureProbe.IndependentFixtures;

namespace B3AQ2.ClosureProbe;

/// <summary>
/// B3-A-Q2 focused requalification probe. Every case uses a fresh, independently generated Bolt
/// 5.8 HELLO fixture and asserts the exact outcome (accepted, or a specific
/// <see cref="BoltMessageErrorCategory"/>). Exit code 0 means every observed outcome matched the
/// reviewer's specification-derived expectation; 4 means at least one expectation was not met.
/// </summary>
internal static class Program
{
    private const string Sentinel = "SENTINEL-Q2-DO-NOT-LEAK";

    private static readonly List<CaseResult> Results = new();

    private static int Main(string[] args)
    {
        var outputDirectory = args.Length > 0 ? args[0] : "docs/reviews/evidence/B3-A-Q2/closure-probe";
        Directory.CreateDirectory(outputDirectory);

        ClosureCases();
        PositiveCases();
        HistoricalFixtureAnalysis();
        ConfidentialityCases();

        var mismatches = Results.Count(r => !r.ExpectationMet);
        var document = new
        {
            probe = "B3-A-Q2 focused requalification probe (F-01 closure)",
            reviewed_head = "83126a99be56fa141eec3342a21b863ca4ef45d2",
            remediation_commit = "d6686d14f3eaee42f83971d517d9077170a30fe2",
            production_assembly = typeof(BoltMessageCodec).Assembly.GetName().FullName,
            spec_source = "docs/reviews/evidence/B3-A-IMPL-01/spec/message.adoc (Bolt 5.8: 'bolt_agent … This field is mandatory.'; 'product … This field is mandatory.')",
            case_count = Results.Count,
            mismatches,
            cases = Results,
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputDirectory, "closure-results.json"), json + Environment.NewLine);
        Console.WriteLine(json);
        Console.Error.WriteLine($"cases={Results.Count} mismatches={mismatches}");
        return mismatches == 0 ? 0 : 4;
    }

    private static void Record(string id, string input, string expectation, string observed, bool met, string notes = "")
    {
        Results.Add(new CaseResult(id, input, expectation, observed, met, notes));
    }

    /// <summary>Decodes a request fixture and asserts the exact outcome.</summary>
    private static void Expect(string id, string input, byte[] payload, string expectedOutcome, string notes = "")
    {
        var codec = new BoltMessageCodec();
        try
        {
            var request = codec.DecodeRequest(payload);
            var observed = $"accepted ({request.GetType().Name})";
            Record(id, input, expectedOutcome, observed, expectedOutcome == "accepted", notes);
        }
        catch (BoltMessageException ex)
        {
            var observed = ex.Category.ToString();
            Record(id, input, expectedOutcome, observed, observed == expectedOutcome, notes);
        }
        catch (Exception ex)
        {
            Record(id, input, expectedOutcome, $"UNEXPECTED {ex.GetType().FullName}: {ex.Message}", false,
                "non-BoltMessageException escaped");
        }
    }

    // ---- §4 closure cases ----

    private static void ClosureCases()
    {
        Expect("Q2-A", "HELLO {user_agent} (no bolt_agent)",
            F.Hello(("user_agent", F.Str("X"))), "MissingRequiredField");

        Expect("Q2-B", "HELLO {user_agent, bolt_agent: null}",
            F.Hello(("user_agent", F.Str("X")), ("bolt_agent", F.Null())), "InvalidFieldType");

        Expect("Q2-C", "HELLO {user_agent, bolt_agent: \"agent\"} (not a map)",
            F.Hello(("user_agent", F.Str("X")), ("bolt_agent", F.Str("agent"))), "InvalidFieldType");

        Expect("Q2-D", "HELLO {user_agent, bolt_agent: {}} (empty map)",
            F.Hello(("user_agent", F.Str("X")), ("bolt_agent", F.Map())), "MissingRequiredField");

        Expect("Q2-E", "HELLO {user_agent, bolt_agent: {platform: \"win\"}} (no product)",
            F.Hello(("user_agent", F.Str("X")),
                ("bolt_agent", F.Map(("platform", F.Str("win"))))), "MissingRequiredField");

        Expect("Q2-F", "HELLO {user_agent, bolt_agent: {product: null}}",
            F.Hello(("user_agent", F.Str("X")),
                ("bolt_agent", F.Map(("product", F.Null())))), "InvalidFieldType");

        Expect("Q2-G", "HELLO {user_agent, bolt_agent: {product: 1}}",
            F.Hello(("user_agent", F.Str("X")),
                ("bolt_agent", F.Map(("product", F.Int(1))))), "InvalidFieldType");
    }

    // ---- §6 positive compatibility cases ----

    private static void PositiveCases()
    {
        Expect("Q2-H", "minimal schema-valid HELLO {user_agent, bolt_agent:{product}}",
            F.Hello(("user_agent", F.Str("Example/1.0")),
                ("bolt_agent", F.Map(("product", F.Str("neo4j-example/1.0"))))), "accepted");

        Expect("Q2-I", "HELLO with permitted optional agent entries",
            F.Hello(("user_agent", F.Str("Example/1.0")),
                ("bolt_agent", F.Map(
                    ("product", F.Str("neo4j-example/1.0")),
                    ("platform", F.Str("Windows 10")),
                    ("language", F.Str("C#/8.0")),
                    ("language_details", F.Str("runtime 8.0.31"))))), "accepted");

        Expect("Q2-J", "HELLO with valid routing metadata",
            F.Hello(("user_agent", F.Str("Example/1.0")),
                ("routing", F.Map(("address", F.Str("x.example.com:9001")))),
                ("bolt_agent", F.Map(("product", F.Str("neo4j-example/1.0"))))), "accepted");

        Expect("Q2-K", "HELLO with notification metadata and null optionals",
            F.Hello(("user_agent", F.Str("Example/1.0")),
                ("routing", F.Null()),
                ("notifications_minimum_severity", F.Str("WARNING")),
                ("notifications_disabled_classifications", F.List(F.Str("HINT"), F.Str("GENERIC"))),
                ("bolt_agent", F.Map(("product", F.Str("neo4j-example/1.0"))))), "accepted");

        Expect("Q2-L", "HELLO with a complete field set plus additional unknown metadata",
            F.Hello(("user_agent", F.Str("Example/4.2.0")),
                ("routing", F.Map(("address", F.Str("x.example.com:9001")))),
                ("notifications_minimum_severity", F.Str("WARNING")),
                ("notifications_disabled_classifications", F.List(F.Str("HINT"))),
                ("bolt_agent", F.Map(
                    ("product", F.Str("neo4j-example/1.0")),
                    ("platform", F.Null()))),
                ("custom_optional_key", F.Str("preserved"))), "accepted",
            "Unknown metadata stays preserved; no additional restriction beyond the spec's mandatory fields.");
    }

    // ---- §5 historical Q1 fixture analysis (C3/C13/C14) ----

    private static void HistoricalFixtureAnalysis()
    {
        // These are the exact Q1 probe C3/C13/C14 constructions, regenerated with this probe's own
        // encoder (both encoders are spec-faithful for these types) and decoded with this probe's
        // own decoder to establish, independently, whether bolt_agent is present.
        var c3 = F.Hello(("user_agent", F.Str("X")));

        var c13 = F.Hello(
            ("user_agent", F.Str("Example/4.2.0")),
            ("routing", F.Map(("address", F.Str("x.example.com:9001")))),
            ("notifications_minimum_severity", F.Str("WARNING")),
            ("notifications_disabled_classifications", F.List(F.Str("HINT"), F.Str("GENERIC"))));

        var c14 = F.Hello(
            ("user_agent", F.Str("X")),
            ("routing", F.Null()),
            ("notifications_minimum_severity", F.Null()),
            ("notifications_disabled_classifications", F.Null()));

        foreach (var (id, bytes, description) in new[]
                 {
                     ("C3", c3, "HELLO {user_agent}"),
                     ("C13", c13, "spec-example field set without bolt_agent"),
                     ("C14", c14, "null optionals without bolt_agent"),
                 })
        {
            var (decoded, consumed) = F.Decode(bytes);
            var structure = (F.PStruct)decoded;
            var map = (F.PMap)structure.Fields[0];
            var hasAgent = map.Entries.Any(e => e.Key == "bolt_agent");
            var expected = hasAgent ? "accepted" : "MissingRequiredField";
            var codec = new BoltMessageCodec();
            string observed;
            try
            {
                codec.DecodeRequest(bytes);
                observed = "accepted";
            }
            catch (BoltMessageException ex)
            {
                observed = ex.Category.ToString();
            }

            Record($"Q2-historical-{id}", description,
                $"bolt_agent absent (independently decoded) -> {expected}",
                $"{observed}; independently decoded consumed={consumed}/{bytes.Length}, bolt_agent present={hasAgent}",
                observed == expected && !hasAgent,
                "Expected remediation difference versus Q1 (Q1 predicted acceptance before the fix).");
        }
    }

    // ---- §8 confidentiality ----

    private static void ConfidentialityCases()
    {
        var codec = new BoltMessageCodec();

        // Missing product while the agent carries a sentinel platform value.
        try
        {
            codec.DecodeRequest(F.Hello(
                ("user_agent", F.Str(Sentinel)),
                ("bolt_agent", F.Map(("platform", F.Str(Sentinel))))));
            Record("Q2-S1", "HELLO with sentinel user_agent/platform but no product",
                "rejected, diagnostics contain no field values", "unexpectedly accepted", false);
        }
        catch (BoltMessageException ex)
        {
            var full = ex.ToString();
            var leak = full.Contains(Sentinel, StringComparison.Ordinal);
            Record("Q2-S1", "HELLO with sentinel user_agent/platform but no product",
                "rejected, diagnostics contain no field values",
                $"category={ex.Category}; sentinel present={leak}", ex.Category == BoltMessageErrorCategory.MissingRequiredField && !leak);
        }

        // Wrong-typed user_agent carrying a sentinel string elsewhere in the map.
        try
        {
            codec.DecodeRequest(F.Hello(
                ("user_agent", F.Int(1)),
                ("bolt_agent", F.Map(("product", F.Str(Sentinel))))));
            Record("Q2-S2", "HELLO with sentinel product but wrong-typed user_agent",
                "rejected, diagnostics contain no field values", "unexpectedly accepted", false);
        }
        catch (BoltMessageException ex)
        {
            var full = ex.ToString();
            var leak = full.Contains(Sentinel, StringComparison.Ordinal);
            Record("Q2-S2", "HELLO with sentinel product but wrong-typed user_agent",
                "rejected, diagnostics contain no field values",
                $"category={ex.Category}; sentinel present={leak}", ex.Category == BoltMessageErrorCategory.InvalidFieldType && !leak);
        }
    }
}

/// <summary>One Q2 probe case result.</summary>
internal sealed record CaseResult(
    string Id,
    string Input,
    string Expectation,
    string Observed,
    bool ExpectationMet,
    string Notes);
