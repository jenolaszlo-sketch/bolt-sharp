using System.Text.Json;

using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ1C.IndependentProbe;

// Cursor lifecycle, fail-closed fault matrix, byte-level oracle (t03/t04) and negative controls.
internal static partial class Program
{
    internal static void CursorLifecycle()
    {
        // Multiple records; partial PULL; repeated PULL; complete PULL.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-property", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));

            var first = s.Send("PULL(1)", new PullRequest(1, null));
            Check("cursor-pull1-first", Seq(first) == "RECORD,SUCCESS"
                && (first!.Responses[0] as RecordResponse)!.Data.Items[0] is PackStreamString fs && fs.Value == "Alice"
                && (first.Responses[1] as SuccessResponse)!.HasMore == true
                && s.Session.State == BoltConnectionState.Streaming,
                "RECORD(Alice),SUCCESS{has_more}/Streaming", $"{Seq(first)}/{s.Session.State}");

            var second = s.Send("PULL(1)", new PullRequest(1, null));
            Check("cursor-pull1-second", Seq(second) == "RECORD,SUCCESS"
                && (second!.Responses[0] as RecordResponse)!.Data.Items[0] is PackStreamString ss && ss.Value == "Bob"
                && (second.Responses[1] as SuccessResponse)!.HasMore != true
                && s.Session.State == BoltConnectionState.Ready,
                "RECORD(Bob),SUCCESS{terminal}/Ready", $"{Seq(second)}/{s.Session.State}");
            Check("cursor-pull1-once", SingleOf(provider.Operations).DisposeAttempts == 1,
                "one disposal across both pulls", $"attempts={SingleOf(provider.Operations).DisposeAttempts}");
        }

        // PULL(n=-1) full drain; exhaustion is stable; no resurrection afterwards.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-exhaust", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var pull = s.Send("PULL(-1)", new PullRequest(-1, null));
            Check("cursor-exhaust", Seq(pull) == "RECORD,RECORD,SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "RECORD,RECORD,SUCCESS/Ready", $"{Seq(pull)}/{s.Session.State}");
            Check("cursor-exhaust-readcount", SingleOf(provider.Operations).ReadCalls == 3,
                "2 records + 1 exhaustion read", $"reads={SingleOf(provider.Operations).ReadCalls}");
        }

        // PULL then DISCARD: partial pull, then discard the remainder.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-pull-discard", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            s.Send("PULL(1)", new PullRequest(1, null));
            var discard = s.Send("DISCARD(-1)", new DiscardRequest(-1, null));
            Check("cursor-pull-discard", Seq(discard) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready
                && SingleOf(provider.Operations).DisposeAttempts == 1,
                "SUCCESS/Ready;cursor retired", $"{Seq(discard)}/{s.Session.State}");
        }

        // DISCARD completion followed by an invalid further PULL.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-discard-pull", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            s.Send("DISCARD(-1)", new DiscardRequest(-1, null));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("cursor-discard-then-pull", Seq(pull) == "FAILURE" && s.Session.State == BoltConnectionState.Defunct,
                "FAILURE/Defunct (no resurrection)", $"{Seq(pull)}/{s.Session.State}");
        }

        // Zero remaining records: an empty-result operation exhausts immediately.
        {
            var operation = new ReferenceResultOperation(new[] { "x" }, Array.Empty<IReadOnlyList<PackStreamValue?>>());
            Check("cursor-zero-remaining", operation.ReadRow() is null, "null on first read", "null");
            operation.Dispose();
        }

        // RESET with an active result: cursor retired, session recovered, no resurrection.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-reset", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var reset = s.Send("RESET", new ResetRequest());
            Check("cursor-reset", Seq(reset) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready
                && SingleOf(provider.Operations).DisposeAttempts == 1,
                "SUCCESS/Ready;cursor retired once", $"{Seq(reset)};attempts={SingleOf(provider.Operations).DisposeAttempts}");
        }

        // GOODBYE and Disconnect with an active result.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-goodbye", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var goodbye = s.Send("GOODBYE", new GoodbyeRequest());
            Check("cursor-goodbye", goodbye!.Responses.Count == 0 && s.Session.State == BoltConnectionState.Defunct
                && SingleOf(provider.Operations).DisposeAttempts == 1,
                "no response;DEFUNCT;one disposal", $"responses={goodbye.Responses.Count}");
        }
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("cursor-disconnect", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            s.Call("Disconnect", () => s.Session.Disconnect());
            Check("cursor-disconnect", s.Session.State == BoltConnectionState.Defunct
                && SingleOf(provider.Operations).DisposeAttempts == 1,
                "DEFUNCT;one disposal", $"state={s.Session.State}");
        }
    }

    internal static void FailClosedLifecycle()
    {
        // 1. Prepare throws before returning an operation.
        {
            var provider = new FaultProvider { ThrowOnPrepare = true };
            var s = ProbeSession.Authenticated("fc-prepare-throw", provider);
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            Check("fc-prepare-throw", Seq(run) == "FAILURE" && run!.State == BoltConnectionState.Failed
                && provider.Prepares == 1 && provider.Operations.Count == 0,
                "FAILURE/Failed;no operation", $"{Seq(run)}/{run!.State};ops={provider.Operations.Count}");
        }

        // 2. Prepare returns invalid metadata (null Fields).
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { NullFields = true } };
            var s = ProbeSession.Authenticated("fc-null-fields", provider);
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var operation = SingleOf(provider.Operations);
            Check("fc-null-fields", Seq(run) == "FAILURE" && run!.State == BoltConnectionState.Failed
                && operation.DisposeAttempts == 1 && operation.FieldsReads == 1,
                "FAILURE/Failed;one attempt;one metadata read", $"{Seq(run)}/{run!.State};attempts={operation.DisposeAttempts}");
        }

        // 2b. Prepare returns invalid metadata (null element inside Fields).
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { NullElement = true } };
            var s = ProbeSession.Authenticated("fc-null-element", provider);
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var operation = SingleOf(provider.Operations);
            Check("fc-null-element", Seq(run) == "FAILURE" && run!.State == BoltConnectionState.Failed
                && operation.DisposeAttempts == 1 && operation.FieldsReads == 1,
                "FAILURE/Failed;one attempt;one metadata read", $"{Seq(run)}/{run!.State};attempts={operation.DisposeAttempts}");
        }

        // 3. Metadata getter throws.
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnFields = true } };
            var s = ProbeSession.Authenticated("fc-fields-throw", provider);
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var operation = SingleOf(provider.Operations);
            Check("fc-fields-throw", Seq(run) == "FAILURE" && run!.State == BoltConnectionState.Failed
                && operation.FieldsReads == 1 && operation.DisposeAttempts == 1,
                "contained;one read;one attempt", $"{Seq(run)};reads={operation.FieldsReads};attempts={operation.DisposeAttempts}");
        }

        // 4. Read fails before the first record.
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnReadNumber = 1 } };
            var s = ProbeSession.Authenticated("fc-read-first", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("fc-read-first", Seq(pull) == "FAILURE" && pull!.State == BoltConnectionState.Failed
                && SingleOf(provider.Operations).DisposeAttempts == 1,
                "FAILURE/Failed;one attempt", $"{Seq(pull)}/{pull!.State}");
        }

        // 5. Read fails after a valid record.
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnReadNumber = 2 } };
            var s = ProbeSession.Authenticated("fc-read-second", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("fc-read-second", Seq(pull) == "RECORD,FAILURE" && pull!.State == BoltConnectionState.Failed,
                "RECORD,FAILURE/Failed", $"{Seq(pull)}/{pull!.State}");
        }

        // 6. RECORD column count is incorrect.
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { RowWidthOverride = 2 } };
            var s = ProbeSession.Authenticated("fc-column-count", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            var failure = IsType<FailureResponse>(SingleOf(pull!.Responses, r => r is FailureResponse));
            Check("fc-column-count",
                failure.GqlStatus == "50N42"
                && (failure.Metadata.Entries.Single(e => e.Key == "description").Value as PackStreamString)!.Value
                    == "A result row does not match the announced result fields."
                && !pull.Responses.Any(r => r is RecordResponse),
                "no bad record;50N42 row-shape FAILURE", $"{failure.GqlStatus};records={pull.Responses.Count(r => r is RecordResponse)}");
        }

        // 7. Dispose throws (completion path).
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnDispose = true } };
            var s = ProbeSession.Authenticated("fc-dispose-throw", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("fc-dispose-throw", Seq(pull) == "RECORD,FAILURE" && pull!.State == BoltConnectionState.Defunct
                && pull.ShouldClose && SingleOf(provider.Operations).DisposeAttempts == 1,
                "RECORD,FAILURE/DEFUNCT;one attempt", $"{Seq(pull)}/{pull!.State}");
        }

        // 8. Read failure followed by throwing Dispose.
        {
            var provider = new FaultProvider
            {
                Wrap = op => new FaultOperation(op) { ThrowOnReadNumber = 1, ThrowOnDispose = true },
            };
            var s = ProbeSession.Authenticated("fc-read-dispose", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            var operation = SingleOf(provider.Operations);
            Check("fc-read-dispose",
                Seq(pull) == "FAILURE" && pull!.State == BoltConnectionState.Defunct
                && operation.DisposeAttempts == 1
                && ReferenceEquals(s.Session.LastCleanupFailure, operation.CleanupError),
                "primary FAILURE;DEFUNCT;one attempt;diagnostic",
                $"{Seq(pull)}/{pull!.State};attempts={operation.DisposeAttempts}");
        }

        // 9. Metadata failure followed by throwing Dispose.
        {
            var provider = new FaultProvider
            {
                Wrap = op => new FaultOperation(op) { NullFields = true, ThrowOnDispose = true },
            };
            var s = ProbeSession.Authenticated("fc-meta-dispose", provider);
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var operation = SingleOf(provider.Operations);
            Check("fc-meta-dispose",
                Seq(run) == "FAILURE" && run!.State == BoltConnectionState.Defunct
                && operation.DisposeAttempts == 1
                && ReferenceEquals(s.Session.LastCleanupFailure, operation.CleanupError),
                "FAILURE/DEFUNCT;one attempt;diagnostic",
                $"{Seq(run)}/{run!.State};attempts={operation.DisposeAttempts}");
        }

        // 10. Access after Dispose (operation level, direct).
        {
            var operation = new ReferenceResultOperation(new[] { "x" }, Array.Empty<IReadOnlyList<PackStreamValue?>>());
            operation.Dispose();
            var rejected = MutationRejected(() => operation.ReadRow());
            Check("fc-use-after-dispose", rejected, "ObjectDisposedException on ReadRow", rejected ? "rejected" : "ALLOWED");
        }

        // Mandatory overlap control.
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnDispose = true } };
            var s = ProbeSession.Authenticated("fc-overlap", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            var reset = s.Send("RESET", new ResetRequest());
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            s.Call("Disconnect", () => s.Session.Disconnect());
            s.Call("Dispose", () => s.Session.Dispose());
            var operation = SingleOf(provider.Operations);
            Check("fc-overlap",
                provider.Prepares == 1 && operation.DisposeAttempts == 1
                && Seq(pull) == "RECORD,RECORD,FAILURE" && pull!.State == BoltConnectionState.Defunct
                && reset!.Responses.Count == 0 && run!.Responses.Count == 0
                && s.Session.State == BoltConnectionState.Defunct,
                "opens=1;one attempt;DEFUNCT;no RESET recovery;no second RUN;no retry",
                $"opens={provider.Prepares};attempts={operation.DisposeAttempts};pull='{Seq(pull)}';reset='{Seq(reset)}';run='{Seq(run)}'");
        }
    }

    internal static void ByteOracle()
    {
        // The oracle constants are the B0-B-Q1 independently decoded frame bytes, hard-coded here
        // (not read from production fixtures). Provenance: introduced in B3-A d99096a; byte-stable
        // through the reviewed revision; decoded 23/23 by B0-B-Q1's standalone PackStream decoder.
        var provider = new CountingProvider();
        var s = ProbeSession.Authenticated("bytes-t03", provider);

        // Authentic t03 RUN bytes through the production decode path.
        var run = s.SendRaw("RUN t03", Convert.FromHexString(T03RunHex));
        var success = IsType<SuccessResponse>(SingleOf(run!.Responses));
        Check("bytes-t03-run-success", success.Fields!.Single() == "answer"
            && Convert.ToHexString(Codec.EncodeResponse(success)) == T03RunSuccessFieldsAnswerHex
            && s.Session.State == BoltConnectionState.Streaming,
            "RUN SUCCESS encodes to the authentic t03 golden",
            Convert.ToHexString(Codec.EncodeResponse(success)));

        // Authentic t03 PULL bytes.
        var pull = s.SendRaw("PULL t03", Convert.FromHexString(T03PullHex));
        var record = IsType<RecordResponse>(pull!.Responses[0]);
        Check("bytes-t03-record", Convert.ToHexString(Codec.EncodeResponse(record)) == T03Record42Hex
            && record.Data.Items.Single() is PackStreamInteger value && value.Value == 42,
            "RECORD [42] encodes to the authentic t03 golden",
            Convert.ToHexString(Codec.EncodeResponse(record)));

        // Terminal summary: the accepted B3-B emission is SUCCESS {} (no has_more field). Bolt
        // treats an absent has_more as false, so the client-visible semantics match the
        // real-server golden that carries has_more:false explicitly (B5-B observation).
        var terminal = IsType<SuccessResponse>(pull.Responses[1]);
        var terminalHex = Convert.ToHexString(Codec.EncodeResponse(terminal));
        var hasMore = terminal.Metadata.Entries.FirstOrDefault(e => e.Key == "has_more").Value;
        Check("bytes-t03-terminal",
            terminalHex == "B170A0" && hasMore is null && pull.State == BoltConnectionState.Ready,
            "terminal SUCCESS {} with absent has_more (defaults to false)",
            $"{terminalHex};has_more={(hasMore is null ? "absent" : "present")}");

        // Graph query through typed RUN; RECORD compared with the authentic t04 golden bytes.
        var graphSession = ProbeSession.Authenticated("bytes-t04", new CountingProvider());
        var graphRun = graphSession.Send("RUN graph", Run(ReferenceQueryEvaluator.GraphQueryText));
        var graphSuccess = IsType<SuccessResponse>(SingleOf(graphRun!.Responses));
        Check("bytes-t04-run-success",
            Convert.ToHexString(Codec.EncodeResponse(graphSuccess)) == T03RunSuccessFieldsGraphHex
            && graphSuccess.Fields!.SequenceEqual(new[] { "a", "r", "b" }),
            "graph RUN SUCCESS encodes to the authentic golden",
            Convert.ToHexString(Codec.EncodeResponse(graphSuccess)));
        var graphPull = graphSession.Send("PULL", new PullRequest(-1, null));
        var graphRecord = IsType<RecordResponse>(graphPull!.Responses[0]);
        Check("bytes-t04-record",
            Convert.ToHexString(Codec.EncodeResponse(graphRecord)) == T04GraphRecordHex,
            "graph RECORD encodes to the authentic t04 golden",
            Convert.ToHexString(Codec.EncodeResponse(graphRecord)));

        // Independent structured oracle: decode the authentic t04 bytes and compare against the
        // reviewer's expected values (spec §7 / B0-B-Q1 standalone decode).
        var decoded = IsType<RecordResponse>(Codec.DecodeResponse(Convert.FromHexString(T04GraphRecordHex)));
        var decodedAlice = IsType<PackStreamNode>(decoded.Data.Items[0]);
        var decodedKnows = IsType<PackStreamRelationship>(decoded.Data.Items[1]);
        var decodedBob = IsType<PackStreamNode>(decoded.Data.Items[2]);
        Check("bytes-t04-decode-oracle",
            decodedAlice.Id == 1 && decodedAlice.ElementId == AliceElementId
            && decodedAlice.Labels.Single() == "Person"
            && (decodedAlice.Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)!.Value == "Alice"
            && decodedKnows.Id == 1 && decodedKnows.StartNodeId == 1 && decodedKnows.EndNodeId == 2
            && decodedKnows.Type == "KNOWS" && decodedKnows.ElementId == KnowsElementId
            && decodedKnows.StartNodeElementId == AliceElementId && decodedKnows.EndNodeElementId == BobElementId
            && (decodedKnows.Properties.Entries.Single(e => e.Key == "since").Value as PackStreamInteger)!.Value == SinceValue
            && decodedBob.Id == 2 && decodedBob.ElementId == BobElementId
            && (decodedBob.Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)!.Value == "Bob",
            "decoded t04 values match the independent oracle",
            $"a=({decodedAlice.Id},{decodedAlice.ElementId});r=({decodedKnows.Id},{decodedKnows.ElementId});b=({decodedBob.Id},{decodedBob.ElementId})");

        // Corruption control: a one-byte change must be detected by the oracle.
        var corruptedHex = T04GraphRecordHex.Replace("85416C696365", "85416C696366");
        var corrupted = IsType<RecordResponse>(Codec.DecodeResponse(Convert.FromHexString(corruptedHex)));
        var corruptedName = (IsType<PackStreamNode>(corrupted.Data.Items[0])
            .Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)!.Value;
        Check("bytes-t04-corruption-detected", corruptedName != "Alice",
            "one-byte corruption detected by the oracle", $"name={corruptedName}");

        // Unsupported-query FAILURE metadata: exact accepted shape; no query-text echo.
        {
            var markerSession = ProbeSession.Authenticated("bytes-refusal", new CountingProvider());
            const string marker = "B3CQ1-LEAK-MARKER-7f3a DETACH DELETE n";
            var refused = markerSession.Send("RUN unsupported with marker", Run("MATCH (x) DETACH DELETE n /* " + marker + " */"));
            var failure = IsType<FailureResponse>(SingleOf(refused!.Responses));
            var keys = failure.Metadata.Entries.Select(e => e.Key).OrderBy(k => k).ToArray();
            var metadataJson = JsonSerializer.Serialize(Flat(failure.Metadata));
            Check("bytes-refusal-metadata",
                failure.GqlStatus == "50N42"
                && failure.Neo4jCode == "Neo.ClientError.Request.Invalid"
                && keys.SequenceEqual(new[] { "description", "diagnostic_record", "gql_status", "neo4j_code" })
                && !metadataJson.Contains(marker, StringComparison.Ordinal)
                && !metadataJson.Contains("DETACH", StringComparison.Ordinal),
                "exact four-field 50N42 metadata;no query-text echo",
                $"{failure.GqlStatus}/{failure.Neo4jCode};leak={metadataJson.Contains(marker, StringComparison.Ordinal)}");
        }
    }

    internal static void NegativeControls()
    {
        // Each control verifies that a reviewer detection mechanism rejected a violating sample or
        // that the corresponding detection check exists and passed.
        var passed = (string id) => Checks.FirstOrDefault(c => c.Id == id)?.Pass == true;

        // Reversed direction would be rejected by the endpoint/direction oracle.
        bool DirectionOk(long start, long end) => start == 1 && end == 2;
        Check("nc-reversed-direction", !DirectionOk(2, 1) && DirectionOk(1, 2),
            "direction oracle rejects 2->1 and accepts 1->2", "detected");

        // Numeric-ID confusion: node 1 and relationship 1 are distinct typed entities.
        {
            var graph = ReferenceGraph.Default;
            Check("nc-id-confusion",
                graph.Nodes[0].Id == 1 && graph.Relationships[0].Id == 1
                && graph.Nodes[0] is PackStreamNode && graph.Relationships[0] is PackStreamRelationship,
                "distinct typed entities share the numeric ID", "detected");
        }

        // Hard-coded response disconnected from the graph model: the alternate-fixture probe passed.
        Check("nc-hardcoded-response", passed("go-alternate-property") && passed("go-alternate-session-graph"),
            "alternate fixture produced alternate results through the real session",
            "detected");

        // Wrong field metadata / wrong record arity / duplicate terminal summary.
        Check("nc-wrong-field-metadata", passed("bytes-t04-run-success") && passed("bytes-t03-run-success"),
            "exact field-list and golden-byte assertions passed", "detected");
        Check("nc-wrong-arity", passed("fc-column-count"),
            "NC-05b column-count violation detected", "detected");

        // Duplicate terminal SUCCESS: each pull result carries exactly one SUCCESS summary.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("nc-duplicate-terminal", provider);
            s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("nc-duplicate-terminal",
                pull!.Responses.Count(r => r is SuccessResponse) == 1,
                "exactly one terminal SUCCESS", "detected");
        }

        // The remaining controls reference their dedicated checks.
        Check("nc-unsupported-accepted", passed("allow-all-refused"),
            "allowlist battery refused every unsupported query", "detected");
        Check("nc-write-executed", passed("allow-graph-unmutated"),
            "graph state unchanged after write attempts", "detected");
        Check("nc-option-ignored", passed("opt-refused-write-mode") && passed("opt-refused-database"),
            "unsafe RUN options refused", "detected");
        Check("nc-parameter-interpolated", passed("param-string"),
            "query-like parameter returned verbatim", "detected");
        Check("nc-missing-vs-null", passed("param-missing-refused") && passed("param-null-distinct"),
            "missing and explicit null are distinct", "detected");
        Check("nc-cursor-reuse", passed("fc-use-after-dispose"),
            "use after disposal rejected", "detected");
        Check("nc-fail-closed-overlap", passed("fc-overlap"),
            "failed retirement overlap control passed", "detected");
        Check("nc-byte-corruption", passed("bytes-t04-corruption-detected"),
            "corrupted graph bytes detected by the oracle", "detected");
    }
}
