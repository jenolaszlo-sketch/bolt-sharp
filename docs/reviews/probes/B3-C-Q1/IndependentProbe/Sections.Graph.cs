using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

namespace B3BQ1C.IndependentProbe;

// Independent reference-graph oracle: identities, immutability probes, execution purity and the
// alternate-fixture derivation falsification. Expected values come from the spec (§7) and the
// B0-B-Q1 standalone decode — not from production code.
internal static partial class Program
{
    internal static void GraphOracle()
    {
        var graph = ReferenceGraph.Default;

        // Structure: exactly the intended nodes and relationships.
        Check("go-node-count", graph.Nodes.Count == 2, "2 nodes", $"{graph.Nodes.Count}");
        Check("go-relationship-count", graph.Relationships.Count == 1, "1 relationship", $"{graph.Relationships.Count}");

        var alice = graph.Nodes[0];
        var bob = graph.Nodes[1];
        var knows = graph.Relationships[0];

        // Node identities, labels and properties (reviewer oracle values).
        Check("go-alice-identity",
            alice.Id == 1 && alice.ElementId == AliceElementId
            && alice.Labels.SequenceEqual(new[] { "Person" })
            && ValueOf(alice.Properties.Entries.Single(e => e.Key == "name").Value)?.ToString() == "Alice",
            "Node(id=1, n:1, [Person], name=Alice)",
            $"Node(id={alice.Id}, {alice.ElementId}, [{string.Join(",", alice.Labels)}], {ValueOf(alice.Properties.Entries.Single(e => e.Key == "name").Value)})");
        Check("go-bob-identity",
            bob.Id == 2 && bob.ElementId == BobElementId
            && bob.Labels.SequenceEqual(new[] { "Person" })
            && ValueOf(bob.Properties.Entries.Single(e => e.Key == "name").Value)?.ToString() == "Bob",
            "Node(id=2, n:2, [Person], name=Bob)",
            $"Node(id={bob.Id}, {bob.ElementId}, [{string.Join(",", bob.Labels)}], {ValueOf(bob.Properties.Entries.Single(e => e.Key == "name").Value)})");

        // Relationship identity, direction, endpoints and property.
        Check("go-rel-identity",
            knows.Id == 1 && knows.ElementId == KnowsElementId && knows.Type == "KNOWS"
            && knows.StartNodeId == 1 && knows.EndNodeId == 2
            && knows.StartNodeElementId == AliceElementId && knows.EndNodeElementId == BobElementId
            && (knows.Properties.Entries.Single(e => e.Key == "since").Value as PackStreamInteger)!.Value == SinceValue,
            "Relationship(id=1, 1->2, KNOWS, r:1, n:1->n:2, since=2024)",
            $"Relationship(id={knows.Id}, {knows.StartNodeId}->{knows.EndNodeId}, {knows.Type}, {knows.ElementId}, {knows.StartNodeElementId}->{knows.EndNodeElementId}, since={(knows.Properties.Entries.Single(e => e.Key == "since").Value as PackStreamInteger)?.Value})");

        // Direction: start is Alice, end is Bob; no reverse relationship exists.
        Check("go-direction",
            knows.StartNodeId == alice.Id && knows.EndNodeId == bob.Id && graph.Relationships.Count == 1,
            "Alice->Bob;no reverse relationship",
            $"start={knows.StartNodeId};end={knows.EndNodeId};rels={graph.Relationships.Count}");

        // Numeric-ID collision: node 1 and relationship 1 coexist as distinct typed entities.
        Check("go-id-collision-distinct",
            alice.Id == 1 && knows.Id == 1
            && alice is PackStreamNode && knows is PackStreamRelationship
            && !ReferenceEquals(alice, knows),
            "node id 1 and relationship id 1 are distinct typed entities",
            $"node={alice.GetType().Name}(1);rel={knows.GetType().Name}(1)");

        // Immutability probes through every available access path.
        Check("go-immutable-labels",
            MutationRejected(() => ((IList<string>)alice.Labels).Add("Injected")),
            "label mutation rejected",
            "rejected");
        Check("go-immutable-properties",
            MutationRejected(() => ((ICollection<KeyValuePair<string, PackStreamValue?>>)alice.Properties.Entries).Add(
                new KeyValuePair<string, PackStreamValue?>("injected", new PackStreamString("x")))),
            "property mutation rejected",
            "rejected");
        Check("go-immutable-node-collection",
            MutationRejected(() => ((IList<PackStreamNode>)graph.Nodes).Add(graph.Nodes[0])),
            "graph node collection mutation rejected",
            "rejected");
        Check("go-immutable-relationship-collection",
            MutationRejected(() => ((IList<PackStreamRelationship>)graph.Relationships).Add(knows)),
            "graph relationship collection mutation rejected",
            "rejected");

        // Repeated access returns the same instances and values (stable identities).
        Check("go-stable-identities",
            ReferenceEquals(graph.Nodes[0], alice) && ReferenceEquals(graph.Relationships[0], knows)
            && ReferenceEquals(ReferenceGraph.Default.Nodes[0], alice),
            "same instances across repeated access",
            "stable");
    }

    internal static void SessionFlows()
    {
        // Flow A - scalar.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("flow-a-scalar", provider);

            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            Check("flow-a-run", Seq(run) == "SUCCESS" && s.Session.State == BoltConnectionState.Streaming
                && (run!.Responses[0] as SuccessResponse)!.Fields!.Single() == "answer"
                && provider.Prepares == 1,
                "SUCCESS(fields=[answer])/Streaming;opens=1",
                $"{Seq(run)}/{s.Session.State};opens={provider.Prepares}");
            var op = SingleOf(provider.Operations);
            Check("flow-a-single-metadata-read", op.FieldsReads == 1,
                "one metadata acquisition", $"reads={op.FieldsReads}");

            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("flow-a-pull", Seq(pull) == "RECORD,SUCCESS" && s.Session.State == BoltConnectionState.Ready
                && (pull!.Responses[0] as RecordResponse)!.Data.Items.Single() is PackStreamInteger i && i.Value == 42,
                "RECORD([42]),SUCCESS/Ready",
                $"{Seq(pull)}/{s.Session.State}");
            Check("flow-a-disposal", op.DisposeAttempts == 1 && !s.Session.HasActiveOperation,
                "exactly one disposal", $"attempts={op.DisposeAttempts}");
        }

        // Flow B - graph.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("flow-b-graph", provider);

            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.GraphQueryText));
            Check("flow-b-run", Seq(run) == "SUCCESS"
                && (run!.Responses[0] as SuccessResponse)!.Fields!.SequenceEqual(new[] { "a", "r", "b" }),
                "SUCCESS(fields=[a,r,b])", $"{Seq(run)}");

            var pull = s.Send("PULL", new PullRequest(-1, null));
            var record = IsType<RecordResponse>(pull!.Responses[0]);
            var a = IsType<PackStreamNode>(record.Data.Items[0]);
            var r = IsType<PackStreamRelationship>(record.Data.Items[1]);
            var b = IsType<PackStreamNode>(record.Data.Items[2]);
            Check("flow-b-graph-values",
                a.Id == 1 && a.ElementId == AliceElementId && a.Labels.Single() == "Person"
                && (a.Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)!.Value == "Alice"
                && r.Id == 1 && r.Type == "KNOWS" && r.StartNodeId == 1 && r.EndNodeId == 2
                && r.ElementId == KnowsElementId && r.StartNodeElementId == AliceElementId && r.EndNodeElementId == BobElementId
                && (r.Properties.Entries.Single(e => e.Key == "since").Value as PackStreamInteger)!.Value == SinceValue
                && b.Id == 2 && b.ElementId == BobElementId
                && (b.Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)!.Value == "Bob"
                && Seq(pull) == "RECORD,SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "typed graph values match the independent oracle",
                $"a=({a.Id},{a.ElementId});r=({r.Id},{r.Type},{r.StartNodeId}->{r.EndNodeId},{r.ElementId});b=({b.Id},{b.ElementId})");
        }

        // Flow C - parameters.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("flow-c-params", provider);

            var run = s.Send("RUN", Run(
                ReferenceQueryEvaluator.ParameterQueryText,
                BackendTestSupportMap(("value", new PackStreamInteger(42)))));
            Check("flow-c-run", Seq(run) == "SUCCESS"
                && (run!.Responses[0] as SuccessResponse)!.Fields!.Single() == "answer",
                "SUCCESS(fields=[answer])", $"{Seq(run)}");

            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("flow-c-pull", Seq(pull) == "RECORD,SUCCESS"
                && (pull!.Responses[0] as RecordResponse)!.Data.Items.Single() is PackStreamInteger i && i.Value == 42,
                "RECORD([42]),SUCCESS", $"{Seq(pull)}");
        }

        // Flow D - unsupported query, RESET, supported query.
        {
            var provider = new CountingProvider();
            var s = ProbeSession.Authenticated("flow-d-unsupported", provider);

            var unsupported = s.Send("RUN unsupported", Run("MATCH (n) DETACH DELETE n"));
            var failure = IsType<FailureResponse>(SingleOf(unsupported!.Responses));
            Check("flow-d-refused",
                failure.GqlStatus == "50N42" && unsupported.State == BoltConnectionState.Failed
                && !unsupported.ShouldClose && provider.Operations.Count == 0,
                "50N42 FAILURE;Failed;no operation owned",
                $"{failure.GqlStatus}/{unsupported.State};ops={provider.Operations.Count}");

            var reset = s.Send("RESET", new ResetRequest());
            Check("flow-d-reset", Seq(reset) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "SUCCESS/Ready", $"{Seq(reset)}/{s.Session.State}");

            var supported = s.Send("RUN supported", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));
            Check("flow-d-supported-after-recovery",
                Seq(supported) == "SUCCESS" && Seq(pull) == "RECORD,SUCCESS"
                && provider.Prepares == 2 && s.Session.State == BoltConnectionState.Ready,
                "supported query succeeds after recovery;opens=2",
                $"{Seq(supported)};{Seq(pull)};opens={provider.Prepares}");
        }

        // Flow E - cleanup failure (fail-closed).
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnDispose = true } };
            var s = new ProbeSession("flow-e-cleanup", provider);
            s.Session.Receive(Hello());
            s.Session.Receive(Logon());
            s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            var pull = s.Send("PULL", new PullRequest(-1, null));

            Check("flow-e-defunct",
                Seq(pull) == "RECORD,FAILURE" && s.Session.State == BoltConnectionState.Defunct && pull!.ShouldClose
                && !pull.Responses.Any(r => r is SuccessResponse),
                "RECORD,FAILURE/DEFUNCT;no SUCCESS;close",
                $"{Seq(pull)}/{s.Session.State}");
            var operation = SingleOf(provider.Operations);
            Check("flow-e-one-attempt", operation.DisposeAttempts == 1,
                "exactly one disposal attempt", $"attempts={operation.DisposeAttempts}");

            var reset = s.Send("RESET", new ResetRequest());
            Check("flow-e-reset-refused", reset!.Responses.Count == 0 && s.Session.State == BoltConnectionState.Defunct,
                "no response;still DEFUNCT", $"{Seq(reset)}/{s.Session.State}");
            var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
            Check("flow-e-run-refused", run!.Responses.Count == 0 && provider.Prepares == 1,
                "no second operation", $"{Seq(run)};opens={provider.Prepares}");
        }

        // Positive control: successful retirement after a recoverable error keeps FAILED/RESET recovery.
        {
            var provider = new FaultProvider { Wrap = op => new FaultOperation(op) { ThrowOnReadNumber = 1 } };
            var s = new ProbeSession("flow-positive", provider);
            s.Session.Receive(Hello());
            s.Session.Receive(Logon());
            s.Send("RUN", Run(ReferenceQueryEvaluator.PropertyQueryText));
            var failed = s.Send("PULL", new PullRequest(-1, null));
            Check("flow-positive-failed", Seq(failed) == "FAILURE" && s.Session.State == BoltConnectionState.Failed,
                "FAILURE/Failed", $"{Seq(failed)}/{s.Session.State}");
            var reset = s.Send("RESET", new ResetRequest());
            Check("flow-positive-reset", Seq(reset) == "SUCCESS" && s.Session.State == BoltConnectionState.Ready,
                "SUCCESS/Ready", $"{Seq(reset)}/{s.Session.State}");
        }
    }

    internal static PackStreamMap BackendTestSupportMap(params (string K, PackStreamValue? V)[] pairs) => Map(pairs);

    /// <summary>Runs a mutation attempt and returns true when the runtime rejected it. The review
    /// expects rejection for every exposed graph access path; a silent success would falsify the
    /// immutability claim.</summary>
    internal static bool MutationRejected(Action attempt)
    {
        try
        {
            attempt();
            return false; // mutation succeeded: immutability claim violated
        }
        catch (Exception)
        {
            return true; // rejected
        }
    }

    /// <summary>
    /// Falsification control: a different immutable graph (public constructor) must produce
    /// correspondingly different results through the same evaluation pipeline and the real
    /// session. This detects a hard-coded response disconnected from the graph model.
    /// </summary>
    internal static void AlternateFixture()
    {
        var alternate = new ReferenceGraph(
            new[]
            {
                new PackStreamNode(5, new[] { "Person" }, Map(("name", new PackStreamString("Carol"))), "n:5"),
                new PackStreamNode(6, new[] { "Person" }, Map(("name", new PackStreamString("Dave"))), "n:6"),
            },
            new[]
            {
                new PackStreamRelationship(3, 5, 6, "KNOWS",
                    Map(("since", new PackStreamInteger(1999))), "r:3", "n:5", "n:6"),
            });

        var evaluator = new ReferenceQueryEvaluator(alternate);
        var propertyResult = evaluator.Evaluate(ReferenceQueryEvaluator.PropertyQueryText, Map());
        Check("go-alternate-property",
            propertyResult.IsSupported && propertyResult.Records.Count == 2
            && Value(propertyResult.Records[0]) == "Carol" && Value(propertyResult.Records[1]) == "Dave",
            "property results derive from the alternate graph (Carol, Dave)",
            $"{Value(propertyResult.Records[0])},{Value(propertyResult.Records[1])}");

        var graphResult = evaluator.Evaluate(ReferenceQueryEvaluator.GraphQueryText, Map());
        if (graphResult.Records.Count != 1)
        {
            Check("go-alternate-graph", false, "one graph record", $"{graphResult.Records.Count} records");
            return;
        }

        var record = graphResult.Records[0];
        var node = IsType<PackStreamNode>(record[0]);
        var relationship = IsType<PackStreamRelationship>(record[1]);
        Check("go-alternate-graph",
            node.Id == 5 && node.ElementId == "n:5"
            && relationship.Id == 3 && relationship.ElementId == "r:3"
            && relationship.StartNodeId == 5 && relationship.EndNodeId == 6
            && (relationship.Properties.Entries.Single(e => e.Key == "since").Value as PackStreamInteger)!.Value == 1999,
            "graph results derive from the alternate graph",
            $"node=({node.Id},{node.ElementId});rel=({relationship.Id},{relationship.ElementId})");

        // Through the real session with a provider over the alternate graph.
        var provider = new AlternateProvider(alternate);
        var s = new ProbeSession("go-alternate-session", provider);
        s.Session.Receive(Hello());
        s.Session.Receive(Logon());
        var run = s.Send("RUN", Run(ReferenceQueryEvaluator.ScalarQueryText));
        Check("go-alternate-session-scalar", Seq(run) == "SUCCESS", "session pipeline works with alternate graph", $"{Seq(run)}");
        var scalarPull = s.Send("PULL", new PullRequest(-1, null));
        Check("go-alternate-session-scalar-pull", Seq(scalarPull) == "RECORD,SUCCESS"
            && (IsType<RecordResponse>(scalarPull!.Responses[0])).Data.Items[0] is PackStreamInteger vi && vi.Value == 42,
            "scalar result derives from the alternate graph (42)", $"{Seq(scalarPull)}");
        var graphRun = s.Send("RUN graph", Run(ReferenceQueryEvaluator.GraphQueryText));
        var pull = s.Send("PULL", new PullRequest(-1, null));
        var alternateRecord = IsType<RecordResponse>(pull!.Responses[0]).Data.Items;
        var alternateNode = IsType<PackStreamNode>(alternateRecord[0]);
        Check("go-alternate-session-graph",
            alternateNode.Id == 5 && alternateNode.ElementId == "n:5"
            && (alternateNode.Properties.Entries.Single(e => e.Key == "name").Value as PackStreamString)?.Value == "Carol",
            "session-integrated graph result derives from the alternate graph",
            $"node=({alternateNode.Id},{alternateNode.ElementId})");
    }

    private static string? Value(IReadOnlyList<PackStreamValue?> record) =>
        IsType<PackStreamString>(SingleOf(record)).Value;
}

/// <summary>Provider over an alternate graph (reviewer falsification control).</summary>
internal sealed class AlternateProvider : ProviderBase
{
    public readonly ReferenceOperationProvider Inner;

    public AlternateProvider(ReferenceGraph graph) => Inner = new ReferenceOperationProvider(graph);

    public override IBoltOperation? Prepare(RunRequest run)
    {
        CountPrepare();
        return Inner.Prepare(run);
    }
}
