using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>
/// B3-C5 (Level B, mandatory): the real reference backend driven through the actual B3-B
/// session engine. These tests establish provider calls, response values, state transitions and
/// disposal counts — they never bypass the session to call the evaluator directly.
/// </summary>
public sealed class ReferenceSessionIntegrationTests
{
    // ---- Successful conversations ----

    [Fact]
    public void ScalarConversation_Returns42_WithExactlyOnceRetirement()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        var success = SessionHarness.SingleResponse<SuccessResponse>(run);
        Assert.Equal(new[] { "answer" }, success.Fields);
        Assert.Equal(BoltConnectionState.Streaming, run.State);
        Assert.Equal(1, provider.PrepareCount);
        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.FieldsCount); // single metadata acquisition (B3-B-R2)
        Assert.Equal(0, operation.DisposeCount);

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.Equal(2, pull.Responses.Count);
        Assert.Equal(42, BackendTestSupport.IntegerOf(pull.Responses[0]));
        var summary = Assert.IsType<SuccessResponse>(pull.Responses[1]);
        Assert.False(summary.HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, pull.State);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void PropertyConversation_ReturnsAliceThenBob()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));
        Assert.Equal(new[] { "n.name" }, SessionHarness.SingleResponse<SuccessResponse>(run).Fields);

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.Equal(3, pull.Responses.Count);
        Assert.Equal("Alice", BackendTestSupport.StringOf(pull.Responses[0]));
        Assert.Equal("Bob", BackendTestSupport.StringOf(pull.Responses[1]));
        Assert.False(Assert.IsType<SuccessResponse>(pull.Responses[2]).HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, pull.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
    }

    [Fact]
    public void PropertyQuery_PullOneTwice_ReturnsRecordsAsExpected()
    {
        // Task acceptance: "PULL(1) twice returns records as expected for test rows".
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var first = session.Receive(new PullRequest(1, null));

        Assert.Equal("Alice", BackendTestSupport.StringOf(first.Responses[0]));
        Assert.True(Assert.IsType<SuccessResponse>(first.Responses[1]).HasMore);
        Assert.Equal(BoltConnectionState.Streaming, first.State);
        Assert.Equal(0, Assert.Single(provider.Operations).DisposeCount);

        var second = session.Receive(new PullRequest(1, null));

        Assert.Equal("Bob", BackendTestSupport.StringOf(second.Responses[0]));
        Assert.False(Assert.IsType<SuccessResponse>(second.Responses[1]).HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, second.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
    }

    [Fact]
    public void OrderedPropertyConversation_ReturnsAscendingNames()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.OrderedPropertyQueryText));
        Assert.Equal(new[] { "name" }, SessionHarness.SingleResponse<SuccessResponse>(run).Fields);

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.Equal("Alice", BackendTestSupport.StringOf(pull.Responses[0]));
        Assert.Equal("Bob", BackendTestSupport.StringOf(pull.Responses[1]));
        Assert.Equal(BoltConnectionState.Ready, pull.State);
    }

    [Fact]
    public void GraphConversation_ReturnsNodeRelationshipNode_FromGraphInstances()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.GraphQueryText));

        Assert.Equal(new[] { "a", "r", "b" }, SessionHarness.SingleResponse<SuccessResponse>(run).Fields);

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.Equal(2, pull.Responses.Count);
        var record = Assert.IsType<RecordResponse>(pull.Responses[0]);
        Assert.Equal(3, record.Data.Items.Count);

        var alice = Assert.IsType<PackStreamNode>(record.Data.Items[0]);
        var knows = Assert.IsType<PackStreamRelationship>(record.Data.Items[1]);
        var bob = Assert.IsType<PackStreamNode>(record.Data.Items[2]);

        // The values are the graph's own instances: generated from the graph model.
        Assert.Same(ReferenceGraph.Default.Nodes[0], alice);
        Assert.Same(ReferenceGraph.Default.Relationships[0], knows);
        Assert.Same(ReferenceGraph.Default.Nodes[1], bob);

        Assert.Equal(1, alice.Id);
        Assert.Equal(new[] { "Person" }, alice.Labels);
        Assert.Equal("Alice", BoltSharp.Tests.Messages.BoltMessageFieldsTestAccess.GetString(alice.Properties, "name"));
        Assert.Equal("n:1", alice.ElementId);

        Assert.Equal(1, knows.Id);
        Assert.Equal("KNOWS", knows.Type);
        Assert.Equal(1, knows.StartNodeId);
        Assert.Equal(2, knows.EndNodeId);
        Assert.Equal("r:1", knows.ElementId);
        Assert.Equal("n:1", knows.StartNodeElementId);
        Assert.Equal("n:2", knows.EndNodeElementId);
        Assert.Equal(2024, BoltSharp.Tests.Messages.BoltMessageFieldsTestAccess.GetInteger(knows.Properties, "since"));

        Assert.Equal(2, bob.Id);
        Assert.Equal("n:2", bob.ElementId);
        Assert.Equal("Bob", BoltSharp.Tests.Messages.BoltMessageFieldsTestAccess.GetString(bob.Properties, "name"));

        Assert.False(Assert.IsType<SuccessResponse>(pull.Responses[1]).HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, pull.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
    }

    [Fact]
    public void ParameterConversation_PassesTypedValues_WithoutInterpolation()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ParameterQueryText,
            BackendTestSupport.Map(("value", new PackStreamInteger(42)))));
        Assert.Equal(new[] { "answer" }, SessionHarness.SingleResponse<SuccessResponse>(run).Fields);

        var pull = session.Receive(new PullRequest(-1, null));
        Assert.Equal(42, BackendTestSupport.IntegerOf(pull.Responses[0]));
        Assert.Equal(BoltConnectionState.Ready, pull.State);
    }

    [Fact]
    public void ParameterConversation_QueryLikeStringValue_IsNeverExecuted()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        const string payload = "MATCH (n) DETACH DELETE n";

        session.Receive(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ParameterQueryText,
            BackendTestSupport.Map(("value", new PackStreamString(payload)))));

        var pull = session.Receive(new PullRequest(-1, null));

        // The record contains the raw typed string; the session never treated it as query text.
        Assert.Equal(payload, BackendTestSupport.StringOf(pull.Responses[0]));
        Assert.Equal(BoltConnectionState.Ready, pull.State);
    }

    [Fact]
    public void ParameterConversation_ExplicitNull_RetainsNull()
    {
        var session = BackendTestSupport.Authenticated(new CountingProvider());

        session.Receive(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ParameterQueryText,
            BackendTestSupport.Map(("value", null))));

        var pull = session.Receive(new PullRequest(-1, null));
        var record = Assert.IsType<RecordResponse>(pull.Responses[0]);
        Assert.Null(Assert.Single(record.Data.Items));
    }

    [Fact]
    public void ParameterConversation_ListValue_PreservesStructure()
    {
        var session = BackendTestSupport.Authenticated(new CountingProvider());
        var list = new PackStreamList(new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) });

        session.Receive(BackendTestSupport.Run(
            ReferenceQueryEvaluator.ParameterQueryText,
            BackendTestSupport.Map(("value", list))));

        var pull = session.Receive(new PullRequest(-1, null));
        var record = Assert.IsType<RecordResponse>(pull.Responses[0]);
        var returned = Assert.IsType<PackStreamList>(Assert.Single(record.Data.Items));
        Assert.Equal(2, returned.Items.Count);
        Assert.Equal(1, Assert.IsType<PackStreamInteger>(returned.Items[0]).Value);
        Assert.Equal(2, Assert.IsType<PackStreamInteger>(returned.Items[1]).Value);
    }

    [Fact]
    public void ParameterConversation_MissingParameter_FailsRecoverably()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ParameterQueryText));

        var failure = SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal("50N42", failure.GqlStatus);
        Assert.Equal("Neo.ClientError.Request.Invalid", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Failed, run.State);
        Assert.False(run.ShouldClose);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Empty(provider.Operations); // no operation was acquired or abandoned

        var reset = session.Receive(new ResetRequest());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
        Assert.Equal(BoltConnectionState.Ready, reset.State);
    }

    // ---- Negative flows ----

    [Fact]
    public void RunBeforeLogon_IsRefused_WithoutProviderExecution()
    {
        var provider = new CountingProvider();
        var session = SessionHarness.Session(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        var failure = SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal("08N06", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Defunct, run.State);
        Assert.Equal(0, provider.PrepareCount);
    }

    [Theory]
    [InlineData("MATCH (n) DETACH DELETE n")]
    [InlineData("CREATE (n:Person)")]
    [InlineData("MERGE (n:Person)")]
    [InlineData("MATCH (n:Person) SET n.name = 'x'")]
    [InlineData("RETURN 42 AS answer; MATCH (n) DELETE n")]
    [InlineData("MATCH (n:Person) RETURN n")]
    [InlineData("UNWIND [1,2] AS x RETURN x")]
    public void UnsupportedQuery_AfterAuth_FailsRecoverably_AndSessionRecovers(string query)
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(query));

        var failure = SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.True(failure.GqlStatus == "50N42", $"{query}: {failure.GqlStatus}");
        Assert.Equal(BoltConnectionState.Failed, run.State);
        Assert.False(run.ShouldClose);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Empty(provider.Operations); // explicit refusal: nothing executed, nothing owned

        var reset = session.Receive(new ResetRequest());
        SessionHarness.SingleResponse<SuccessResponse>(reset);

        var supported = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));
        Assert.Equal(BoltConnectionState.Streaming, supported.State);
        Assert.Equal(2, provider.PrepareCount);
        Assert.Single(provider.Operations); // only the supported RUN acquired an operation
    }

    [Fact]
    public void UnsupportedRunCapabilities_AreRefused_WithoutProviderOwnership()
    {
        var cases = new (string Name, PackStreamMap Extra)[]
        {
            ("write-mode", BackendTestSupport.Map(("mode", new PackStreamString("w")))),
            ("rw-mode", BackendTestSupport.Map(("mode", new PackStreamString("rw")))),
            ("database", BackendTestSupport.Map(("db", new PackStreamString("neo4j")))),
            ("impersonation", BackendTestSupport.Map(("imp_user", new PackStreamString("alice")))),
            ("timeout", BackendTestSupport.Map(("tx_timeout", new PackStreamInteger(1)))),
            ("unknown", BackendTestSupport.Map(("unknown_option", new PackStreamString("x")))),
        };

        foreach (var (name, extra) in cases)
        {
            var provider = new CountingProvider();
            var session = BackendTestSupport.Authenticated(provider);

            var run = session.Receive(BackendTestSupport.Run(
                ReferenceQueryEvaluator.ScalarQueryText, extra: extra));

            var failure = SessionHarness.SingleResponse<FailureResponse>(run);
            Assert.True(failure.GqlStatus == "50N42", $"{name}: {failure.GqlStatus}");
            Assert.True(run.State == BoltConnectionState.Failed, $"{name}: {run.State}");
            Assert.Empty(provider.Operations);
        }
    }

    [Fact]
    public void PullBeforeRun_IsProtocolViolation_WithoutProviderExecution()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);

        var pull = session.Receive(new PullRequest(-1, null));

        SessionHarness.SingleResponse<FailureResponse>(pull);
        Assert.Equal(BoltConnectionState.Defunct, pull.State);
        Assert.Equal(0, provider.PrepareCount);
    }

    [Fact]
    public void SecondRunWhileStreaming_IsRefused_AndRetiresTheCursor()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var second = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        var failure = SessionHarness.SingleResponse<FailureResponse>(second);
        Assert.Equal("08N06", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Defunct, second.State);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void PartialPull_ThenDiscard_AdvancesAndReleasesTheCursor()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var partial = session.Receive(new PullRequest(1, null));
        Assert.Equal("Alice", BackendTestSupport.StringOf(partial.Responses[0]));
        Assert.True(Assert.IsType<SuccessResponse>(partial.Responses[1]).HasMore);
        Assert.Equal(BoltConnectionState.Streaming, partial.State);
        Assert.Equal(0, Assert.Single(provider.Operations).DisposeCount);

        var discard = session.Receive(new DiscardRequest(-1, null));

        Assert.DoesNotContain(discard.Responses, r => r is RecordResponse);
        var summary = SessionHarness.SingleResponse<SuccessResponse>(discard);
        Assert.False(summary.HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, discard.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void ResetAfterRecoverableFailure_DisposesCursor_AndAllowsNewRun()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { ThrowOnReadNumber = 1 } };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var failed = session.Receive(new PullRequest(-1, null));

        var failure = SessionHarness.SingleResponse<FailureResponse>(failed);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Failed, failed.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeAttempts);

        var reset = session.Receive(new ResetRequest());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
        Assert.Equal(BoltConnectionState.Ready, reset.State);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));
        Assert.Equal(BoltConnectionState.Streaming, run.State);
        Assert.Equal(2, provider.PrepareCount);
    }

    [Fact]
    public void CleanupFailure_ThenResetNeverRecovers_NoSecondOperation()
    {
        // The accepted B3-B contract, re-established with the real reference backend:
        // RUN -> PULL -> disposal failure -> RESET -> RUN must never open operation #2.
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { ThrowOnDispose = true } };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var failed = session.Receive(new PullRequest(-1, null));

        Assert.Equal(3, failed.Responses.Count);
        Assert.IsType<RecordResponse>(failed.Responses[0]);
        Assert.IsType<RecordResponse>(failed.Responses[1]);
        var failure = Assert.IsType<FailureResponse>(failed.Responses[2]);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.DoesNotContain(failed.Responses, r => r is SuccessResponse);
        Assert.Equal(BoltConnectionState.Defunct, failed.State);
        Assert.True(failed.ShouldClose);
        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.DisposeAttempts);

        var reset = session.Receive(new ResetRequest());
        Assert.Empty(reset.Responses);
        Assert.Equal(BoltConnectionState.Defunct, reset.State);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));
        Assert.Empty(run.Responses);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, operation.DisposeAttempts);

        session.Disconnect();
        session.Dispose();
        Assert.Equal(1, operation.DisposeAttempts);
    }

    [Fact]
    public void GoodbyeWithActiveCursor_DisposesOnce_WithoutFabricatedResponse()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var goodbye = session.Receive(new GoodbyeRequest());

        Assert.Empty(goodbye.Responses);
        Assert.Equal(BoltConnectionState.Defunct, goodbye.State);
        Assert.True(goodbye.ShouldClose);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void DisconnectWithActiveCursor_DisposesOnce_AndIsIdempotent()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        session.Disconnect();
        session.Disconnect();

        Assert.Equal(BoltConnectionState.Defunct, session.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void SessionDispose_RetiresCursor_AndReceiveThrows()
    {
        var provider = new CountingProvider();
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        session.Dispose();
        session.Dispose();

        Assert.Equal(BoltConnectionState.Defunct, session.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => session.Receive(new PullRequest(-1, null)));
    }
}
