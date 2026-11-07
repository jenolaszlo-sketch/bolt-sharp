using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>
/// B3-B3: scripted RUN/PULL/DISCARD lifecycle, bounded results, cursor ownership, and the
/// session-level NC-05b / NC-08 contextual validations.
/// </summary>
public sealed class ResultAndSequenceTests
{
    private static long ValueOf(RecordResponse record) =>
        Assert.IsType<PackStreamInteger>(record.Data.Items[0]).Value;

    [Fact]
    public void Run_Succeeds_WithFields_AndEntersStreaming()
    {
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithRows(1, 2));

        var result = session.Receive(SessionHarness.Run());

        var success = SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.Equal(new[] { "x" }, success.Fields);
        Assert.Equal(BoltConnectionState.Streaming, result.State);
        Assert.True(session.HasActiveOperation);
    }

    [Fact]
    public void EmptyResult_Pull_ReturnsSummaryOnly_AndReady()
    {
        var session = SessionHarness.Streaming();

        var result = session.Receive(SessionHarness.Pull(-1));

        var summary = SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.False(summary.HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void SingleRow_PullAll_EmitsOneRecord_ThenReady()
    {
        var session = SessionHarness.Streaming(7);

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(2, result.Responses.Count);
        Assert.Equal(7, ValueOf(Assert.IsType<RecordResponse>(result.Responses[0])));
        Assert.False(Assert.IsType<SuccessResponse>(result.Responses[1]).HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, result.State);
    }

    [Fact]
    public void FinitePull_LeavesStreaming_WithHasMoreTrue()
    {
        var session = SessionHarness.Streaming(1, 2, 3);

        var result = session.Receive(SessionHarness.Pull(1));

        Assert.Equal(1, ValueOf(Assert.IsType<RecordResponse>(result.Responses[0])));
        Assert.True(Assert.IsType<SuccessResponse>(result.Responses[1]).HasMore);
        Assert.Equal(BoltConnectionState.Streaming, result.State);
        Assert.True(session.HasActiveOperation);
    }

    [Fact]
    public void MultiplePulls_PreserveRecordOrder()
    {
        var session = SessionHarness.Streaming(1, 2, 3);
        var values = new List<long>();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var result = session.Receive(SessionHarness.Pull(1));
            values.Add(ValueOf(Assert.IsType<RecordResponse>(result.Responses[0])));
        }

        Assert.Equal(new long[] { 1, 2, 3 }, values);
        Assert.Equal(BoltConnectionState.Ready, session.State);
    }

    [Fact]
    public void PullMinusOne_IsBoundedByMaxRecordsPerResponse()
    {
        var provider = ScriptedOperationProvider.WithRows(1, 2, 3, 4, 5);
        var session = SessionHarness.Authenticated(provider, new BoltSessionOptions
        {
            Verifier = TestCredentialVerifier.AcceptAll(),
            MaxRecordsPerResponse = 2,
        });
        session.Receive(SessionHarness.Run());

        var first = session.Receive(SessionHarness.Pull(-1));
        Assert.Equal(3, first.Responses.Count); // 2 records + summary
        Assert.True(Assert.IsType<SuccessResponse>(first.Responses[2]).HasMore);
        Assert.Equal(BoltConnectionState.Streaming, first.State);

        var second = session.Receive(SessionHarness.Pull(-1));
        Assert.Equal(3, second.Responses.Count);
        Assert.True(Assert.IsType<SuccessResponse>(second.Responses[2]).HasMore);

        var third = session.Receive(SessionHarness.Pull(-1));
        Assert.Equal(2, third.Responses.Count); // 1 record + summary
        Assert.False(Assert.IsType<SuccessResponse>(third.Responses[1]).HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, third.State);
    }

    [Fact]
    public void PullZero_EmitsNoRecords_AndReportsRemaining()
    {
        var session = SessionHarness.Streaming(1, 2);

        var result = session.Receive(SessionHarness.Pull(0));

        var summary = SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.True(summary.HasMore);
        Assert.Equal(BoltConnectionState.Streaming, result.State);
    }

    [Fact]
    public void Discard_Partial_LeavesStreaming_NoRecordsEmitted()
    {
        var session = SessionHarness.Streaming(1, 2, 3);

        var result = session.Receive(SessionHarness.Discard(1));

        var summary = SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.True(summary.HasMore);
        Assert.DoesNotContain(result.Responses, r => r is RecordResponse);
        Assert.Equal(BoltConnectionState.Streaming, result.State);
    }

    [Fact]
    public void Discard_All_ReleasesCursor_AndReady()
    {
        var operation = new ScriptedOperation(new[] { "x" },
            ScriptedOperationProvider.Row(1), ScriptedOperationProvider.Row(2));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Discard(-1));

        var summary = SessionHarness.SingleResponse<SuccessResponse>(result);
        Assert.False(summary.HasMore ?? false);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void Cursor_IsDisposedExactlyOnce_OnCompletion()
    {
        var operation = new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());
        session.Receive(SessionHarness.Pull(-1));
        session.Receive(SessionHarness.Pull(-1)); // after completion this is a protocol violation
        session.Disconnect();

        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void ProviderReadFailure_MidStream_IsFailure_Failed_AndDisposesCursor()
    {
        var operation = new ScriptedOperation(new[] { "x" },
            ScriptedOperationProvider.Row(1), ScriptedOperationProvider.Row(2))
        {
            ThrowOnReadNumber = 2,
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        var failure = Assert.IsType<FailureResponse>(result.Responses[^1]);
        Assert.Single(result.Responses, r => r is RecordResponse);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
    }

    // ---- NC-05b: contextual column-count validation ----

    [Fact]
    public void Nc05b_WrongColumnCount_IsFailure_NoRecordEmitted_CursorDisposed()
    {
        var operation = new ScriptedOperation(
            new[] { "x" },
            new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) }); // two values, one field
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.DoesNotContain(result.Responses, r => r is RecordResponse);
        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void Nc05b_CorrectColumnCount_EmitsEveryRecord()
    {
        var operation = new ScriptedOperation(
            new[] { "a", "b" },
            new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) },
            new PackStreamValue?[] { new PackStreamInteger(3), new PackStreamInteger(4) });
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(2, result.Responses.Count(r => r is RecordResponse));
        Assert.Equal(BoltConnectionState.Ready, result.State);
    }

    // ---- NC-08: terminal response sequencing ----

    [Fact]
    public void Nc08_NoSecondTerminalSummary_DoublePullAfterCompletion()
    {
        var session = SessionHarness.Streaming(1);
        var first = session.Receive(SessionHarness.Pull(-1));
        Assert.Equal(2, first.Responses.Count);
        Assert.False(Assert.IsType<SuccessResponse>(first.Responses[1]).HasMore ?? false);

        // A completed result must not become active again: the second PULL is a protocol violation,
        // not another SUCCESS, and emits no RECORD.
        var second = session.Receive(SessionHarness.Pull(-1));
        Assert.IsType<FailureResponse>(Assert.Single(second.Responses));
        Assert.Equal(BoltConnectionState.Defunct, second.State);
    }

    [Fact]
    public void Nc08_ExactlyOneTerminalSummaryPerRequest()
    {
        var session = SessionHarness.Streaming(1, 2, 3);

        var finite = session.Receive(SessionHarness.Pull(1));
        Assert.Single(finite.Responses, r => r is SuccessResponse);

        var rest = session.Receive(SessionHarness.Pull(-1));
        Assert.Single(rest.Responses, r => r is SuccessResponse);
    }

    [Fact]
    public void Nc08_NoRecordForDiscard()
    {
        var session = SessionHarness.Streaming(1, 2, 3);

        var result = session.Receive(SessionHarness.Discard(-1));

        Assert.DoesNotContain(result.Responses, r => r is RecordResponse);
        Assert.Single(result.Responses, r => r is SuccessResponse);
    }

    [Fact]
    public void NoRecordBeforeSuccessfulRun()
    {
        var session = SessionHarness.Authenticated();

        // PULL in READY is a protocol violation and can never emit a RECORD.
        var result = session.Receive(SessionHarness.Pull(1));

        Assert.DoesNotContain(result.Responses, r => r is RecordResponse);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
    }
}
