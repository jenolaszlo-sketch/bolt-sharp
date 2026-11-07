using System.Collections;

using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>
/// B3-B-R2 adversarial regression tests for the two resource-lifecycle defects established by
/// B3-B-Q2: F01 (unsafe recovery after failed cleanup) and F02 (metadata acquisition outside the
/// ownership boundary). Required contract: every acquired operation has exactly one session owner,
/// receives at most one disposal attempt, and a failed retirement permanently closes the connection
/// (DEFUNCT) without false recovery or replacement acquisition.
/// </summary>
public sealed class ResourceLifecycleRemediationTests
{
    private static ScriptedOperation ThrowingOperation(params long[] rows) =>
        new(new[] { "x" }, rows.Select(ScriptedOperationProvider.Row).ToArray())
        {
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };

    private static IReadOnlyList<PackStreamValue?> TwoValueRow() =>
        new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) };

    private static string DescriptionOf(FailureResponse failure) =>
        BoltMessageFieldsTestAccess.GetString(failure.Metadata, "description")!;

    // ---- R2-F01: failed retirement is irreversible ----

    [Fact]
    public void PullCompletion_ThrowingDispose_NoSuccessDefunct_OneAttempt()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(2, result.Responses.Count);
        Assert.IsType<RecordResponse>(result.Responses[0]);
        var failure = Assert.IsType<FailureResponse>(result.Responses[1]);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Equal("provider dispose failure", session.LastCleanupFailure?.Message);
    }

    [Fact]
    public void PullReadFailure_ThrowingDispose_PreservesPrimary_AndRecordsCleanup_Defunct()
    {
        var operation = new ScriptedOperation(
            new[] { "x" },
            ScriptedOperationProvider.Row(1),
            ScriptedOperationProvider.Row(2))
        {
            ThrowOnReadNumber = 2,
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        // Primary provider failure: RECORD prefix preserved, then exactly one FAILURE.
        Assert.IsType<RecordResponse>(result.Responses[0]);
        var failure = Assert.IsType<FailureResponse>(result.Responses[^1]);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Single(result.Responses, r => r is FailureResponse);
        Assert.DoesNotContain(result.Responses, r => r is SuccessResponse);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);

        // The failed retirement is a separate, observable fact.
        Assert.Equal(1, operation.DisposeCount);
        Assert.Equal("provider dispose failure", session.LastCleanupFailure?.Message);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void PullBadColumnCount_ThrowingDispose_NoRecord_RowShapePrimary_Defunct()
    {
        var operation = new ScriptedOperation(new[] { "x" }, TwoValueRow())
        {
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal("A result row does not match the announced result fields.", DescriptionOf(failure));
        Assert.Equal("50N42", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(1, operation.DisposeCount);
        Assert.Equal("provider dispose failure", session.LastCleanupFailure?.Message);
    }

    [Fact]
    public void DiscardCompletion_ThrowingDispose_NoFalseSuccess_Defunct()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Discard(-1));

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void DiscardPartial_ThenThrowingCompletion_Defunct_NoRetry()
    {
        var operation = new ScriptedOperation(
            new[] { "x" },
            ScriptedOperationProvider.Row(1),
            ScriptedOperationProvider.Row(2))
        {
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var partial = session.Receive(SessionHarness.Discard(1));
        Assert.True(SessionHarness.SingleResponse<SuccessResponse>(partial).HasMore);
        Assert.Equal(BoltConnectionState.Streaming, partial.State);
        Assert.Equal(0, operation.DisposeCount); // partial discard does not retire yet

        var completion = session.Receive(SessionHarness.Discard(-1));

        var failure = SessionHarness.SingleResponse<FailureResponse>(completion);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Defunct, completion.State);
        Assert.True(completion.ShouldClose);
        Assert.Equal(1, operation.DisposeCount);

        session.Disconnect();
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void FailedCleanup_ThenResetLogonRun_NeverRecovers_NeverReopens()
    {
        var verifier = TestCredentialVerifier.AcceptAll();
        var operation = ThrowingOperation(1);
        var provider = ScriptedOperationProvider.WithOperation(operation);
        var session = SessionHarness.Session(provider, new BoltSessionOptions { Verifier = verifier });
        session.Receive(SessionHarness.Hello());
        session.Receive(SessionHarness.Logon());
        session.Receive(SessionHarness.Run());
        session.Receive(SessionHarness.Pull(-1)); // failed retirement -> DEFUNCT

        Assert.True(session.IsDefunct);

        var reset = session.Receive(SessionHarness.Reset());
        Assert.Empty(reset.Responses);
        Assert.Equal(BoltConnectionState.Defunct, reset.State);

        var logon = session.Receive(SessionHarness.Logon());
        Assert.Empty(logon.Responses);
        Assert.Equal(BoltConnectionState.Defunct, logon.State);

        var run = session.Receive(SessionHarness.Run());
        Assert.Empty(run.Responses);
        Assert.Equal(BoltConnectionState.Defunct, run.State);

        var latePull = session.Receive(SessionHarness.Pull(-1));
        Assert.Empty(latePull.Responses);

        Assert.Equal(1, verifier.Calls);        // no re-authentication attempt
        Assert.Equal(1, provider.PrepareCount); // no replacement acquisition
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void FailedCleanup_ThenTerminalMessages_NoSecondAttempt()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());
        session.Receive(SessionHarness.Pull(-1));
        Assert.Equal(1, operation.DisposeCount);

        var goodbye = session.Receive(SessionHarness.Goodbye());
        Assert.Empty(goodbye.Responses);
        session.Disconnect();
        session.Dispose();
        session.Dispose();

        Assert.Equal(1, operation.DisposeCount);
        Assert.Equal(BoltConnectionState.Defunct, session.State);
        Assert.Throws<ObjectDisposedException>(() => session.Receive(SessionHarness.Run()));
    }

    [Fact]
    public void ReadFailure_CleanDispose_RemainsRecoverable_ResetAndRunPreserved()
    {
        var provider = new ScriptedOperationProvider(_ =>
            new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1)) { ThrowOnReadNumber = 1 });
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Run());

        var failed = session.Receive(SessionHarness.Pull(-1));
        SessionHarness.SingleResponse<FailureResponse>(failed);
        Assert.Equal(BoltConnectionState.Failed, failed.State);
        Assert.False(failed.ShouldClose);
        Assert.Null(session.LastCleanupFailure);

        var reset = session.Receive(SessionHarness.Reset());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
        Assert.Equal(BoltConnectionState.Ready, reset.State);

        var second = session.Receive(SessionHarness.Run());
        SessionHarness.SingleResponse<SuccessResponse>(second);
        Assert.Equal(BoltConnectionState.Streaming, second.State);
        Assert.Equal(2, provider.PrepareCount);
    }

    [Fact]
    public void PullCompletion_CleanDispose_CompletesNormally()
    {
        var operation = new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(2, result.Responses.Count);
        Assert.IsType<RecordResponse>(result.Responses[0]);
        Assert.IsType<SuccessResponse>(result.Responses[1]);
        Assert.Equal(BoltConnectionState.Ready, result.State);
        Assert.Equal(1, operation.DisposeCount);
        Assert.Null(session.LastCleanupFailure);
    }

    [Fact]
    public void RowShapeFailure_CleanDispose_RemainsRecoverable()
    {
        var provider = new ScriptedOperationProvider(_ => new ScriptedOperation(new[] { "x" }, TwoValueRow()));
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Run());

        var failed = session.Receive(SessionHarness.Pull(-1));
        var failure = SessionHarness.SingleResponse<FailureResponse>(failed);
        Assert.Equal("A result row does not match the announced result fields.", DescriptionOf(failure));
        Assert.Equal(BoltConnectionState.Failed, failed.State);
        Assert.Null(session.LastCleanupFailure);

        var reset = session.Receive(SessionHarness.Reset());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
        Assert.Equal(BoltConnectionState.Ready, reset.State);

        var second = session.Receive(SessionHarness.Run());
        SessionHarness.SingleResponse<SuccessResponse>(second);
        Assert.Equal(2, provider.PrepareCount);
    }

    // ---- R2-F02: metadata acquisition inside the ownership boundary ----

    [Fact]
    public void Metadata_FirstGetterThrows_CleanDispose_ContainedFailureFailed_OneAttempt()
    {
        var operation = new InstrumentedOperation(() => throw new InvalidOperationException("metadata getter failure"));
        var provider = ScriptedOperationProvider.WithOperation(operation);
        var session = SessionHarness.Authenticated(provider);

        var result = session.Receive(SessionHarness.Run());

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.False(result.ShouldClose);
        Assert.Equal(1, operation.FieldsReads);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Null(session.LastCleanupFailure);

        // Clean retirement keeps the failure recoverable: RESET is permitted and a later RUN
        // reacquires through the provider.
        var reset = session.Receive(SessionHarness.Reset());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
        Assert.Equal(BoltConnectionState.Ready, reset.State);

        var second = session.Receive(SessionHarness.Run());
        SessionHarness.SingleResponse<FailureResponse>(second);
        Assert.Equal(2, provider.PrepareCount);
        Assert.Equal(2, operation.DisposeCount);
        Assert.Equal(2, operation.FieldsReads);
    }

    [Fact]
    public void Metadata_FirstGetterThrows_ThrowingDispose_Defunct_OneAttempt_NoReuse()
    {
        var operation = new InstrumentedOperation(() => throw new InvalidOperationException("metadata getter failure"))
        {
            ThrowOnDispose = true,
        };
        var provider = ScriptedOperationProvider.WithOperation(operation);
        var session = SessionHarness.Authenticated(provider);

        var result = session.Receive(SessionHarness.Run());

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(1, operation.FieldsReads);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Equal("provider dispose failure", session.LastCleanupFailure?.Message);

        var reset = session.Receive(SessionHarness.Reset());
        Assert.Empty(reset.Responses);
        Assert.Equal(BoltConnectionState.Defunct, reset.State);

        var run = session.Receive(SessionHarness.Run());
        Assert.Empty(run.Responses);
        Assert.Equal(1, provider.PrepareCount);

        session.Receive(SessionHarness.Goodbye());
        session.Disconnect();
        session.Dispose();
        Assert.Equal(1, operation.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => session.Receive(SessionHarness.Run()));
    }

    [Fact]
    public void Metadata_NullElement_CleanDispose_Failed_NoStreaming_OneAttempt()
    {
        var operation = new InstrumentedOperation(() => new string[] { null! });
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var result = session.Receive(SessionHarness.Run());

        SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.NotEqual(BoltConnectionState.Streaming, result.State);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.Equal(1, operation.FieldsReads);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Null(session.LastCleanupFailure);
    }

    [Fact]
    public void Metadata_NullElement_ThrowingDispose_Defunct_OneAttempt()
    {
        var operation = new InstrumentedOperation(() => new string[] { null! }) { ThrowOnDispose = true };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var result = session.Receive(SessionHarness.Run());

        SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.NotEqual(BoltConnectionState.Streaming, result.State);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(1, operation.FieldsReads);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Equal("provider dispose failure", session.LastCleanupFailure?.Message);
    }

    [Fact]
    public void Metadata_GetterReadExactlyOnce_AcrossFullLifecycle()
    {
        var operation = new InstrumentedOperation(
            () => new[] { "x" },
            ScriptedOperationProvider.Row(1),
            ScriptedOperationProvider.Row(2));
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var run = session.Receive(SessionHarness.Run());
        Assert.Equal(new[] { "x" }, SessionHarness.SingleResponse<SuccessResponse>(run).Fields);
        Assert.Equal(1, operation.FieldsReads);

        var first = session.Receive(SessionHarness.Pull(1));
        Assert.IsType<RecordResponse>(first.Responses[0]);
        Assert.True(Assert.IsType<SuccessResponse>(first.Responses[1]).HasMore);
        Assert.Equal(1, operation.FieldsReads);

        var rest = session.Receive(SessionHarness.Pull(-1));
        Assert.IsType<RecordResponse>(rest.Responses[0]);
        Assert.Equal(BoltConnectionState.Ready, rest.State);
        Assert.Equal(1, operation.FieldsReads);

        session.Disconnect();
        Assert.Equal(1, operation.FieldsReads);
    }

    [Fact]
    public void Metadata_ChangingBetweenReads_ColumnCheckUsesRunTimeSnapshot()
    {
        var read = 0;
        var operation = new InstrumentedOperation(
            () => ++read == 1 ? new[] { "x" } : new[] { "y", "z" },
            TwoValueRow());
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var run = session.Receive(SessionHarness.Run());
        Assert.Equal(new[] { "x" }, SessionHarness.SingleResponse<SuccessResponse>(run).Fields);
        Assert.Equal(1, operation.FieldsReads);

        // The two-value row must fail against the RUN-time snapshot (one field). A re-read of the
        // provider would have seen two fields and silently emitted the record.
        var pull = session.Receive(SessionHarness.Pull(-1));
        var failure = SessionHarness.SingleResponse<FailureResponse>(pull);
        Assert.Equal("A result row does not match the announced result fields.", DescriptionOf(failure));
        Assert.Equal(1, operation.FieldsReads);
        Assert.Equal(BoltConnectionState.Failed, pull.State);
    }

    [Fact]
    public void Metadata_ThrowingListMembers_AreContained()
    {
        var countThrow = new InstrumentedOperation(() => new ThrowingList(new[] { "x" }, throwOnCount: true));
        var countSession = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(countThrow));
        var countResult = countSession.Receive(SessionHarness.Run());
        SessionHarness.SingleResponse<FailureResponse>(countResult);
        Assert.Equal(BoltConnectionState.Failed, countResult.State);
        Assert.Equal(1, countThrow.FieldsReads);
        Assert.Equal(1, countThrow.DisposeCount);

        var indexThrow = new InstrumentedOperation(() => new ThrowingList(new[] { "x" }, throwOnIndexer: true));
        var indexSession = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(indexThrow));
        var indexResult = indexSession.Receive(SessionHarness.Run());
        SessionHarness.SingleResponse<FailureResponse>(indexResult);
        Assert.Equal(BoltConnectionState.Failed, indexResult.State);
        Assert.Equal(1, indexThrow.FieldsReads);
        Assert.Equal(1, indexThrow.DisposeCount);
    }

    [Fact]
    public void Metadata_ValidEmptyFields_RunSucceeds_Unchanged()
    {
        var operation = new InstrumentedOperation(() => Array.Empty<string>());
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var run = session.Receive(SessionHarness.Run());

        var success = SessionHarness.SingleResponse<SuccessResponse>(run);
        Assert.NotNull(success.Fields);
        Assert.Empty(success.Fields!);
        Assert.Equal(BoltConnectionState.Streaming, run.State);
        Assert.Equal(1, operation.FieldsReads);

        var pull = session.Receive(SessionHarness.Pull(-1));
        SessionHarness.SingleResponse<SuccessResponse>(pull);
        Assert.Equal(BoltConnectionState.Ready, pull.State);
        Assert.Equal(1, operation.DisposeCount);
    }

    /// <summary>
    /// A scripted operation whose field metadata is produced by a delegate so tests can inject
    /// getter failures, changing values, null elements or throwing list members.
    /// </summary>
    private sealed class InstrumentedOperation : IBoltOperation
    {
        private readonly Func<IReadOnlyList<string>> _fields;
        private readonly Queue<IReadOnlyList<PackStreamValue?>> _rows;

        public InstrumentedOperation(
            Func<IReadOnlyList<string>> fields,
            params IReadOnlyList<PackStreamValue?>[] rows)
        {
            _fields = fields;
            _rows = new Queue<IReadOnlyList<PackStreamValue?>>(rows);
        }

        public int FieldsReads { get; private set; }

        public int DisposeCount { get; private set; }

        public bool ThrowOnDispose { get; set; }

        public IReadOnlyList<string> Fields
        {
            get
            {
                FieldsReads++;
                return _fields();
            }
        }

        public IReadOnlyList<PackStreamValue?>? ReadRow() =>
            _rows.Count > 0 ? _rows.Dequeue() : null;

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("provider dispose failure");
            }
        }
    }

    /// <summary>A read-only string list with a configurable throwing member (count or indexer).</summary>
    private sealed class ThrowingList : IReadOnlyList<string>
    {
        private readonly IReadOnlyList<string> _items;
        private readonly bool _throwOnCount;
        private readonly bool _throwOnIndexer;

        public ThrowingList(IReadOnlyList<string> items, bool throwOnCount = false, bool throwOnIndexer = false)
        {
            _items = items;
            _throwOnCount = throwOnCount;
            _throwOnIndexer = throwOnIndexer;
        }

        public int Count
        {
            get
            {
                if (_throwOnCount)
                {
                    throw new InvalidOperationException("metadata count failure");
                }

                return _items.Count;
            }
        }

        public string this[int index]
        {
            get
            {
                if (_throwOnIndexer)
                {
                    throw new InvalidOperationException("metadata indexer failure");
                }

                return _items[index];
            }
        }

        public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
