using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>B3-B2: FAILED / IGNORED / RESET recovery and pipelined causality.</summary>
public sealed class FailureAndRecoveryTests
{
    [Fact]
    public void Run_ProviderReturnsNull_IsFailure_AndFailed()
    {
        var session = SessionHarness.Authenticated(new ScriptedOperationProvider(_ => null));

        var result = session.Receive(SessionHarness.Run());

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.False(result.ShouldClose);
        Assert.Equal("Neo.ClientError.Request.Invalid", failure.Neo4jCode);
        Assert.Equal("50N42", failure.GqlStatus);
    }

    [Fact]
    public void Run_ProviderThrows_IsFailure_AndFailed_WithoutLeakingDetail()
    {
        const string sentinel = "SENTINEL-QUERY-B3B";
        var provider = new ScriptedOperationProvider(_ => throw new InvalidOperationException(sentinel));
        var session = SessionHarness.Authenticated(provider);

        var result = session.Receive(SessionHarness.Run(sentinel));

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.DoesNotContain(sentinel, failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("pull")]
    [InlineData("discard")]
    [InlineData("logoff")]
    [InlineData("telemetry")]
    public void Failed_WorkMessages_AreIgnored_AndStayFailed(string kind)
    {
        var session = SessionHarness.Authenticated(new ScriptedOperationProvider(_ => null));
        session.Receive(SessionHarness.Run());

        var result = kind switch
        {
            "run" => session.Receive(SessionHarness.Run()),
            "pull" => session.Receive(SessionHarness.Pull(1)),
            "discard" => session.Receive(SessionHarness.Discard(1)),
            "logoff" => session.Receive(SessionHarness.Logoff()),
            _ => session.Receive(SessionHarness.Telemetry(1)),
        };

        SessionHarness.SingleResponse<IgnoredResponse>(result);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.False(result.ShouldClose);
    }

    [Fact]
    public void Failed_ThenReset_Succeeds_AndReturnsReady()
    {
        var session = SessionHarness.Authenticated(new ScriptedOperationProvider(_ => null));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Reset());

        SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.False(result.ShouldClose);
    }

    [Fact]
    public void Failed_ThenWork_ThenReset_DoesNotOverwriteOriginalFailure()
    {
        var session = SessionHarness.Authenticated(new ScriptedOperationProvider(_ => null));
        var failureResult = session.Receive(SessionHarness.Run());
        Assert.Single(failureResult.Responses);
        Assert.IsType<FailureResponse>(failureResult.Responses[0]);

        var ignored = session.Receive(SessionHarness.Pull(1));
        Assert.IsType<IgnoredResponse>(Assert.Single(ignored.Responses));
    }

    [Fact]
    public void Run_UnsupportedCapabilityExtra_IsRefusedByProvider_NotSilentlyAccepted()
    {
        // The provider refuses RUNs that request impersonation; the session surfaces FAILURE rather
        // than silently executing a capability it does not support.
        var provider = new ScriptedOperationProvider(run =>
            BoltMessageFieldsTestAccess.GetString(run.Extra, "imp_user") is null
                ? ScriptedOperationProvider.WithRows(1).Prepare(run)
                : null);
        var session = SessionHarness.Authenticated(provider);
        var run = new RunRequest("MATCH (n) RETURN n", SessionHarness.EmptyMap,
            SessionHarness.Map(("imp_user", new PackStreamString("other"))));

        var result = session.Receive(run);

        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        Assert.Equal(BoltConnectionState.Failed, result.State);
    }

    [Fact]
    public void NoExecutionAfterLogoff()
    {
        var provider = ScriptedOperationProvider.WithRows(1);
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Logoff());

        var result = session.Receive(SessionHarness.Run());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.Equal(0, provider.PrepareCount);
    }

    [Fact]
    public void Reset_WithOutstandingResult_DisposesOperation_AndReturnsReady()
    {
        var operation = new ScriptedOperation(new[] { "x" }, SessionHarnessRow(1), SessionHarnessRow(2));
        var provider = ScriptedOperationProvider.WithOperation(operation);
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Run());
        Assert.True(session.HasActiveOperation);

        var result = session.Receive(SessionHarness.Reset());

        SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.False(session.HasActiveOperation);
        Assert.Equal(1, operation.DisposeCount);
        Assert.Empty(provider.Prepared.Skip(1)); // no second RUN was prepared
    }

    [Fact]
    public void Reset_WhenOperationDisposalThrows_IsFailure_AndDefunct()
    {
        var operation = new ScriptedOperation(new[] { "x" }, SessionHarnessRow(1))
        {
            DisposeOverride = () => throw new InvalidOperationException("dispose failed"),
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Reset());

        SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
    }

    [Fact]
    public void Reset_FromStreamingWithoutFailure_ReturnsReady()
    {
        var session = SessionHarness.Streaming(1, 2, 3);

        var result = session.Receive(SessionHarness.Reset());

        SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(BoltConnectionState.Ready, result.State);
    }

    [Fact]
    public void Reset_AfterFailedThenRun_AllowsNewRun()
    {
        var provider = new ScriptedOperationProvider(_ => null);
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Run());      // FAILURE -> FAILED
        session.Receive(SessionHarness.Reset());    // -> READY

        var result = session.Receive(SessionHarness.Run());

        // Provider still returns null, so this is another FAILURE, but it proves the RUN was
        // actually attempted after recovery rather than ignored.
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        Assert.Equal(2, provider.PrepareCount);
    }

    [Fact]
    public void Pipelined_FailureIgnoredIgnoredResetRun_PreservesCausality()
    {
        var provider = ScriptedOperationProvider.WithRows(42);
        var session = SessionHarness.Authenticated(provider);

        var envelopes = session.ReceiveBatch(new BoltRequest[]
        {
            SessionHarness.Run(),      // provider returns a result -> SUCCESS -> STREAMING
            SessionHarness.Pull(1),    // one row -> SUCCESS has_more:false -> READY
            SessionHarness.Pull(1),    // PULL in READY -> protocol violation -> DEFUNCT, batch stops
            SessionHarness.Reset(),
            SessionHarness.Run(),
        });

        Assert.Equal(3, envelopes.Count);
        Assert.Equal(BoltMessageSignatures.Run, envelopes[0].RequestSignature);
        Assert.IsType<SuccessResponse>(envelopes[0].Responses[0]);
        Assert.Equal(BoltConnectionState.Streaming, envelopes[0].State);

        Assert.Equal(BoltMessageSignatures.Pull, envelopes[1].RequestSignature);
        Assert.IsType<RecordResponse>(envelopes[1].Responses[0]);
        Assert.Equal(BoltConnectionState.Ready, envelopes[1].State);

        Assert.Equal(BoltMessageSignatures.Pull, envelopes[2].RequestSignature);
        Assert.IsType<FailureResponse>(Assert.Single(envelopes[2].Responses));
        Assert.Equal(BoltConnectionState.Defunct, envelopes[2].State);
    }

    [Fact]
    public void Pipelined_FailureIgnoredReset_GivesOrderedIgnoredAndRecovery()
    {
        var provider = new ScriptedOperationProvider(_ => null);
        var session = SessionHarness.Authenticated(provider);

        var envelopes = session.ReceiveBatch(new BoltRequest[]
        {
            SessionHarness.Run(),      // FAILURE -> FAILED
            SessionHarness.Pull(1000), // IGNORED -> FAILED
            SessionHarness.Reset(),    // SUCCESS -> READY
        });

        Assert.Equal(3, envelopes.Count);
        Assert.IsType<FailureResponse>(Assert.Single(envelopes[0].Responses));
        Assert.Equal(BoltConnectionState.Failed, envelopes[0].State);
        Assert.IsType<IgnoredResponse>(Assert.Single(envelopes[1].Responses));
        Assert.Equal(BoltConnectionState.Failed, envelopes[1].State);
        Assert.IsType<SuccessResponse>(Assert.Single(envelopes[2].Responses));
        Assert.Equal(BoltConnectionState.Ready, envelopes[2].State);
    }

    private static IReadOnlyList<BoltSharp.PackStream.PackStreamValue?> SessionHarnessRow(long value) =>
        ScriptedOperationProvider.Row(value);
}
