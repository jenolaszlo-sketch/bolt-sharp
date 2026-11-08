using System.Text.Json;

using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ1C.IndependentProbe;

// Allowlist security battery, RUN-option capability matrix and parameter-type preservation.
internal static partial class Program
{
    /// <summary>Reviewer-constructed unsupported/mutation/multi-statement/case/unicode texts.</summary>
    internal static readonly string[] UnsupportedQueries =
    {
        "MATCH (n) DETACH DELETE n",
        "MATCH (n:Person) DETACH DELETE n",
        "CREATE (n:Person {name: 'Eve'})",
        "MATCH (n:Person) SET n.name = 'Changed'",
        "MERGE (n:Person {name:'Eve'})",
        "RETURN 42 AS answer; MATCH (n) DELETE n",
        "MATCH (n:Person) RETURN n.name; CREATE (x)",
        "CALL db.labels()",
        "CALL dbms.components()",
        "SHOW DATABASES",
        "DROP INDEX my_index",
        "/* comment */ RETURN 42 AS answer",
        "RETURN 42 AS answer // comment",
        "RETURN 42 AS answer --",
        "RETURN 42 AS answer2",
        "RETURN 42 answer",
        "RETURN 41 AS answer",
        "MATCH (n:Person) RETURN n",
        "MATCH (n:Person) RETURN n.name ORDER BY name",
        "MATCH (n:Animal) RETURN n.name",
        "MATCH (a:Person)-[r:HAS]->(b:Person) RETURN a, r, b",
        "MATCH (b:Person)-[r:KNOWS]->(a:Person) RETURN a, r, b",
        "MATCH (n:Person) RETURN n.name AS name",
        "UNWIND [1,2,3] AS x RETURN x",
        "return 42 as answer",
        "RETURN 42 AS ANSWER",
        "RETURN $other AS answer",
        "RETURN 42 AS answer RETURN 43 AS x",
    };

    internal static void AllowlistBattery()
    {
        // Normalization matrix - documented contract: whitespace normalization only (any
        // char.IsWhiteSpace run collapses to one space); case is significant; semicolons,
        // comments and unknown clauses are never stripped. Each check uses a fresh session.
        NormCheck("norm-leading-trailing-space", "   RETURN 42 AS answer   ", true);
        NormCheck("norm-internal-spaces", "RETURN  42   AS    answer", true);
        NormCheck("norm-tabs-newlines", "\tRETURN 42 AS\tanswer\n", true);
        NormCheck("norm-nbsp", "RETURN\u00A042 AS answer", true);
        NormCheck("norm-zero-width", "RETURN 42 AS\u200Banswer", false);
        NormCheck("norm-lowercase", "return 42 as answer", false);
        NormCheck("norm-uppercase", "RETURN 42 AS ANSWER", false);
        NormCheck("norm-semicolon", "RETURN 42 AS answer;", false);
        NormCheck("norm-empty", "", false);
        NormCheck("norm-whitespace-only", "   \t\n  ", false);
        // Multi-line formatting of a supported form is accepted by the documented whitespace
        // collapsing rule (all IsWhiteSpace runs collapse to one space).
        NormCheck("norm-multiline-supported-form", "MATCH\n(n:Person)\nRETURN\nn.name", true);
        // Unsupported/mutation battery: every query is refused with no owned operation, no graph
        // mutation, and safe RESET recovery. Prepare IS consulted for each refusal (the seam
        // design); the invariant under test is refusal-without-ownership, which is asserted.
        var batteryProvider = new CountingProvider();
        var failures = 0;
        foreach (var query in UnsupportedQueries)
        {
            var s = ProbeSession.Authenticated("allow-" + failures, batteryProvider);
            var run = s.Send("RUN", Run(query));
            var ok = Seq(run) == "FAILURE"
                && run!.State == BoltConnectionState.Failed
                && !run.ShouldClose
                && batteryProvider.Operations.Count == 0
                && !s.Session.HasActiveOperation;
            if (!ok)
            {
                Check("allow-refused", false, $"FAILURE/Failed;no operation for: {query}",
                    $"{Seq(run)}/{run!.State};ops={batteryProvider.Operations.Count}");
                failures++;
                continue;
            }

            // Safe RESET recovery after every refusal.
            var reset = s.Send("RESET", new ResetRequest());
            if (Seq(reset) != "SUCCESS" || s.Session.State != BoltConnectionState.Ready)
            {
                Check("allow-refused", false, $"RESET recovery for: {query}", $"{Seq(reset)}/{s.Session.State}");
                failures++;
                continue;
            }
        }

        Check("allow-all-refused", failures == 0,
            $"all {UnsupportedQueries.Length} unsupported queries refused with recovery",
            $"{failures} failures");

        // The provider's Prepare is consulted for refusals (seam design; disclosed, not hidden).
        Check("allow-prepare-consulted", batteryProvider.Prepares == UnsupportedQueries.Length,
            "Prepare called once per refusal (disclosed seam behavior)",
            $"{batteryProvider.Prepares}");

        // The shared graph is unchanged after the whole battery.
        var graph = ReferenceGraph.Default;
        Check("allow-graph-unmutated",
            graph.Nodes.Count == 2 && graph.Relationships.Count == 1
            && graph.Nodes[0].Id == 1 && graph.Nodes[1].Id == 2
            && (graph.Nodes[0].Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)!.Value == "Alice"
            && (graph.Relationships[0].Properties.Entries.Single(e => e.Key == "since").Value as PackStreamInteger)!.Value == 2024,
            "graph state unchanged by refused queries",
            "unchanged");

        // Bounded input behavior: normalization is linear; long inputs are handled without
        // exception leakage. (No artificial exhaustion; bounded, reproducible sizes.)
        {
            var longPadded = new string(' ', 100_000) + ReferenceQueryEvaluator.ScalarQueryText + new string(' ', 100_000);
            var s1 = ProbeSession.Authenticated("bounded-padded", new CountingProvider());
            var padded = s1.Send("long padded", Run(longPadded));
            Check("bounded-padded-accepted", Seq(padded) == "SUCCESS", "accepted", Seq(padded));

            var longUnsupported = "a" + new string('x', 1_000_000);
            var s2 = ProbeSession.Authenticated("bounded-unsupported", new CountingProvider());
            var refusedLong = s2.Send("long unsupported", Run(longUnsupported));
            Check("bounded-unsupported-refused", Seq(refusedLong) == "FAILURE" && s2.Escaped is null,
                "refused without exception", $"{Seq(refusedLong)};escaped={s2.Escaped?.GetType().Name}");
        }
    }

    /// <summary>Runs one query on a fresh session and checks the accepted/refused outcome.</summary>
    internal static void NormCheck(string name, string query, bool expectAccepted)
    {
        var provider = new CountingProvider();
        var s = ProbeSession.Authenticated("norm-" + name, provider);
        var run = s.Send("RUN", Run(query));
        var actual = Seq(run);
        var ok = expectAccepted ? actual == "SUCCESS" : actual == "FAILURE";
        Check("norm-" + name, ok,
            expectAccepted ? "accepted" : "refused",
            $"{actual}/{run!.State}");
    }

    internal static void OptionsMatrix()
    {
        // 1/2. default and explicit read mode are accepted.
        {
            var s = ProbeSession.Authenticated("opt-empty", new CountingProvider());
            var run = s.Send("RUN default extras", Run(ReferenceQueryEvaluator.ScalarQueryText));
            Check("opt-empty-accepted", Seq(run) == "SUCCESS", "accepted", Seq(run));
        }
        {
            var s = ProbeSession.Authenticated("opt-read", new CountingProvider());
            var run = s.Send("RUN mode=r", Run(
                ReferenceQueryEvaluator.ScalarQueryText,
                extra: Map(("mode", new PackStreamString(ReferenceCapabilityPolicy.ReadOnlyMode)))));
            Check("opt-read-accepted", Seq(run) == "SUCCESS", "accepted", Seq(run));
        }

        // 3-14. every unsupported option is refused with no owned operation; the refusal proves the
        // option was seen (a supported query combined with an unsafe option cannot succeed).
        var refusedCases = new (string Name, PackStreamMap Extra)[]
        {
            ("write-mode", Map(("mode", new PackStreamString("w")))),
            ("rw-mode", Map(("mode", new PackStreamString("rw")))),
            ("wrong-mode-type", Map(("mode", new PackStreamInteger(0)))),
            ("null-mode", Map(("mode", null))),
            ("database", Map(("db", new PackStreamString("neo4j")))),
            ("impersonation", Map(("imp_user", new PackStreamString("alice")))),
            ("timeout", Map(("tx_timeout", new PackStreamInteger(1)))),
            ("bookmarks", Map(("bookmarks", new PackStreamList(Array.Empty<PackStreamValue?>())))),
            ("unknown-key", Map(("unknown_option", new PackStreamString("x")))),
            ("wrong-typed-known-key", Map(("db", new PackStreamInteger(1)))),
            ("multiple-unsupported", Map(("db", new PackStreamString("neo4j")), ("imp_user", new PackStreamString("bob")))),
            ("supported-query-unsafe-option", Map(("mode", new PackStreamString("w")))),
        };

        foreach (var (name, extra) in refusedCases)
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("opt-" + name, provider);
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText, extra: extra));
            var failure = run!.Responses.Count == 1 ? run.Responses[0] as FailureResponse : null;
            Check("opt-refused-" + name,
                failure is not null && failure.GqlStatus == "50N42"
                && run.State == BoltConnectionState.Failed && provider.Operations.Count == 0,
                "50N42 FAILURE;Failed;no operation",
                $"{Seq(run)}/{run.State};ops={provider.Operations.Count}");
        }

        // Explicit null db / imp_user are "not requested" and accepted.
        {
            var s = ProbeSession.Authenticated("opt-null-db", new CountingProvider());
            var run = s.Send("RUN db=null", Run(
                ReferenceQueryEvaluator.ScalarQueryText, extra: Map(("db", null))));
            Check("opt-null-db-accepted", Seq(run) == "SUCCESS", "accepted", Seq(run));
        }
        {
            var s = ProbeSession.Authenticated("opt-null-imp", new CountingProvider());
            var run = s.Send("RUN imp_user=null", Run(
                ReferenceQueryEvaluator.ScalarQueryText, extra: Map(("imp_user", null))));
            Check("opt-null-imp-accepted", Seq(run) == "SUCCESS", "accepted", Seq(run));
        }
    }

    internal static void ParameterBattery()
    {
        var cases = new (string Name, PackStreamValue? Value, object? Expected)[]
        {
            ("positive-int", new PackStreamInteger(42), 42L),
            ("negative-int", new PackStreamInteger(-7), -7L),
            ("long-min", new PackStreamInteger(long.MinValue), long.MinValue),
            ("long-max", new PackStreamInteger(long.MaxValue), long.MaxValue),
            ("string", new PackStreamString("MATCH (n) DETACH DELETE n"), "MATCH (n) DETACH DELETE n"),
            ("bool-true", new PackStreamBoolean(true), true),
            ("bool-false", new PackStreamBoolean(false), false),
            ("explicit-null", null, null),
            ("empty-list", new PackStreamList(Array.Empty<PackStreamValue?>()), new object?[] { }),
            ("mixed-list", new PackStreamList(new PackStreamValue?[]
            {
                new PackStreamInteger(1), new PackStreamString("x"), null, new PackStreamBoolean(true),
            }), new object?[] { 1L, "x", null, true }),
            ("nested-map", Map(("k", new PackStreamInteger(1))), null),
        };

        foreach (var (name, value, expected) in cases)
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("param-" + name, provider);
            var run = s.Send("RUN", Run(
                ReferenceQueryEvaluator.ParameterQueryText,
                BackendTestSupportMap(("value", value))));
            if (Seq(run) != "SUCCESS")
            {
                Check("param-" + name, false, "SUCCESS", $"{Seq(run)}/{run!.State}");
                continue;
            }

            var pull = s.Send("PULL", new PullRequest(-1, null));
            var record = IsType<RecordResponse>(SingleOf(pull!.Responses, r => r is RecordResponse));
            var returned = SingleOf(record.Data.Items);
            var roundTripped = ValueOf(returned);

            if (name == "nested-map")
            {
                var map = IsType<PackStreamMap>(returned);
                var inner = map.Entries.Single(e => e.Key == "k").Value;
                Check("param-" + name,
                    inner is PackStreamInteger pi && pi.Value == 1,
                    "typed map preserved", $"k={(inner as PackStreamInteger)?.Value}");
                continue;
            }

            Check("param-" + name,
                name is "mixed-list" or "empty-list"
                    ? JsonSerializer.Serialize(roundTripped) == JsonSerializer.Serialize(expected)
                    : Equals(roundTripped, expected),
                $"typed value preserved ({JsonSerializer.Serialize(expected)})",
                JsonSerializer.Serialize(roundTripped));
        }

        // Missing parameter is refused; it is NOT equivalent to an explicit null.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("param-missing", provider);
            var run = s.Send("RUN missing", Run(ReferenceQueryEvaluator.ParameterQueryText));
            var failure = IsType<FailureResponse>(SingleOf(run!.Responses));
            Check("param-missing-refused",
                failure.GqlStatus == "50N42" && run.State == BoltConnectionState.Failed,
                "50N42 FAILURE;Failed", $"{failure.GqlStatus}/{run.State}");
        }

        // Distinctness control: explicit null succeeds with a null record while missing fails.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("param-null-distinct", provider);
            var run = s.Send("RUN explicit null", Run(
                ReferenceQueryEvaluator.ParameterQueryText,
                BackendTestSupportMap(("value", null))));
            IsType<SuccessResponse>(SingleOf(run!.Responses));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            var record = IsType<RecordResponse>(SingleOf(pull!.Responses, r => r is RecordResponse));
            Check("param-null-distinct",
                run.State == BoltConnectionState.Streaming && record.Data.Items.Single() is null,
                "explicit null succeeds with a null record value", $"state={run.State}");
        }

        // Repeated execution is stable: the same query+parameters produce identical values.
        {
            var first = ProbeSession.Authenticated("param-repeat-1", new CountingProvider());
            var second = ProbeSession.Authenticated("param-repeat-2", new CountingProvider());
            var p = BackendTestSupportMap(("value", new PackStreamInteger(7)));
            var r1 = first.Send("RUN", Run(ReferenceQueryEvaluator.ParameterQueryText, p));
            var p1 = first.Send("PULL", new PullRequest(-1, null));
            var r2 = second.Send("RUN", Run(ReferenceQueryEvaluator.ParameterQueryText, p));
            var p2 = second.Send("PULL", new PullRequest(-1, null));
            var v1 = ValueOf(SingleOf(IsType<RecordResponse>(SingleOf(p1!.Responses, r => r is RecordResponse)).Data.Items));
            var v2 = ValueOf(SingleOf(IsType<RecordResponse>(SingleOf(p2!.Responses, r => r is RecordResponse)).Data.Items));
            Check("param-repeat-stable", Equals(v1, v2) && Equals(v1, 7L),
                "identical values across sessions", $"{v1}/{v2}");
        }
    }
}
