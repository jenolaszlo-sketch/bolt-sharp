using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Messages;

using Xunit;

namespace BoltSharp.Tests.Sessions;

/// <summary>
/// B3-B-R1 regression tests for the two defects independently established by B3-B-Q1:
/// F-01 (provider cleanup failure escaping the API / leaving a non-terminal session) and
/// F-02 (out-of-domain TELEMETRY identifier error metadata).
/// </summary>
public sealed class CleanupAndTelemetryRemediationTests
{
    private static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    private static ScriptedOperation ThrowingOperation(params long[] rows) =>
        new(new[] { "x" }, rows.Select(ScriptedOperationProvider.Row).ToArray())
        {
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };

    private static string FailureCodeOf(BoltSessionResult result) =>
        Assert.IsType<PackStreamString>(BoltMessageFieldsTestAccess.GetValue(
            Assert.IsType<FailureResponse>(Assert.Single(result.Responses)).Metadata, "neo4j_code"))!.Value;

    // ---- F-01: terminal-state safety ----

    [Fact]
    public void Goodbye_WithThrowingDispose_ReachesDefunct_NoResponse_OneAttempt()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Goodbye());

        Assert.Empty(result.Responses);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.NotNull(session.LastCleanupFailure);
    }

    [Fact]
    public void Disconnect_WithThrowingDispose_ReachesDefunct_OneAttempt()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        session.Disconnect();

        Assert.Equal(BoltConnectionState.Defunct, session.State);
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.NotNull(session.LastCleanupFailure);
    }

    [Fact]
    public void Dispose_WithThrowingProviderDispose_IsTerminalAndIdempotent()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        session.Dispose();
        session.Dispose();

        Assert.Equal(BoltConnectionState.Defunct, session.State);
        Assert.Equal(1, operation.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => session.Receive(SessionHarness.Run()));
    }

    [Fact]
    public void GoodbyeThenDisconnect_DoesNotRepeatDisposal()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());
        session.Receive(SessionHarness.Goodbye());

        session.Disconnect();

        Assert.Equal(1, operation.DisposeCount);
        Assert.Equal(BoltConnectionState.Defunct, session.State);
    }

    [Fact]
    public void DisconnectThenDispose_DoesNotRepeatDisposal()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        session.Disconnect();
        session.Dispose();
        session.Dispose();

        Assert.Equal(1, operation.DisposeCount);
        Assert.Equal(BoltConnectionState.Defunct, session.State);
    }

    [Fact]
    public void GoodbyeTwice_DoesNotRepeatDisposal()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        session.Receive(SessionHarness.Goodbye());
        var second = session.Receive(SessionHarness.Goodbye());

        Assert.Equal(1, operation.DisposeCount);
        Assert.Empty(second.Responses);
        Assert.Equal(BoltConnectionState.Defunct, second.State);
    }

    [Fact]
    public void LateOperationAfterTerminalCleanup_IsRefused_WithoutExecution()
    {
        var provider = ScriptedOperationProvider.WithOperation(ThrowingOperation(1));
        var session = SessionHarness.Authenticated(provider);
        session.Receive(SessionHarness.Run());
        session.Receive(SessionHarness.Goodbye());

        var result = session.Receive(SessionHarness.Run());

        Assert.Empty(result.Responses);
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.Equal(1, provider.PrepareCount);
    }

    // ---- F-01: RUN with null Fields ----

    [Fact]
    public void Run_NullFields_CleanDispose_IsRecoverableFailed_OneAttempt_NoCursor()
    {
        var operation = new NullFieldsOperation();
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var result = session.Receive(SessionHarness.Run());

        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.Null(session.LastCleanupFailure);
    }

    [Fact]
    public void Run_NullFields_ThrowingDispose_IsDefunct_OneAttempt_NoCursor()
    {
        var operation = new NullFieldsOperation { ThrowOnDispose = true };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var result = session.Receive(SessionHarness.Run());

        // Cleanup could not complete safely: the session must be terminal, never falsely usable.
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.True(result.ShouldClose);
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        Assert.Equal(1, operation.DisposeCount);
        Assert.False(session.HasActiveOperation);
        Assert.NotNull(session.LastCleanupFailure);
    }

    [Fact]
    public void Run_NullFields_DoesNotSubstituteEmptyFields_AndNeverStreams()
    {
        var operation = new NullFieldsOperation();
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));

        var result = session.Receive(SessionHarness.Run());

        Assert.NotEqual(BoltConnectionState.Streaming, result.State);
        Assert.DoesNotContain(result.Responses, r => r is SuccessResponse { Fields: not null });
    }

    // ---- F-01: RESET and PULL-completion behaviour preserved ----

    [Fact]
    public void Reset_WithThrowingDispose_StillFailsClosed()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Reset());

        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
        Assert.Equal("Neo.DatabaseError.General.UnknownError", FailureCodeOf(result));
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void PullCompletion_WithThrowingDispose_IsFailureFailed_OneAttempt()
    {
        var operation = ThrowingOperation(1);
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.IsType<FailureResponse>(result.Responses[^1]);
        Assert.Equal(1, operation.DisposeCount);
        Assert.NotNull(session.LastCleanupFailure);
    }

    // ---- F-01: primary failure precedence ----

    [Fact]
    public void PrimaryProviderFailure_ThenCleanupFailure_KeepsPrimaryAndRecordsCleanup()
    {
        var operation = new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1))
        {
            ThrowOnReadNumber = 1,
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", FailureCodeOf(result));
        Assert.Equal(1, operation.DisposeCount);
        Assert.NotNull(session.LastCleanupFailure);
    }

    [Fact]
    public void RowShapeFailure_ThenCleanupFailure_KeepsRowShapePrimary()
    {
        var operation = new ScriptedOperation(
            new[] { "x" },
            new PackStreamValue?[] { new PackStreamInteger(1), new PackStreamInteger(2) })
        {
            DisposeOverride = () => throw new InvalidOperationException("provider dispose failure"),
        };
        var session = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(operation));
        session.Receive(SessionHarness.Run());

        var result = session.Receive(SessionHarness.Pull(-1));

        Assert.Equal(BoltConnectionState.Failed, result.State);
        var failure = Assert.IsType<FailureResponse>(result.Responses[^1]);
        Assert.Equal(
            "A result row does not match the announced result fields.",
            BoltMessageFieldsTestAccess.GetString(failure.Metadata, "description"));
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public void CleanupFailure_IsRecorded_AndAbsentOnCleanPaths()
    {
        var clean = new ScriptedOperation(new[] { "x" }, ScriptedOperationProvider.Row(1));
        var cleanSession = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(clean));
        cleanSession.Receive(SessionHarness.Run());
        cleanSession.Receive(SessionHarness.Pull(-1));
        Assert.Null(cleanSession.LastCleanupFailure);

        var dirty = ThrowingOperation(1);
        var dirtySession = SessionHarness.Authenticated(ScriptedOperationProvider.WithOperation(dirty));
        dirtySession.Receive(SessionHarness.Run());
        dirtySession.Receive(SessionHarness.Goodbye());
        Assert.NotNull(dirtySession.LastCleanupFailure);
    }

    // ---- F-02: TELEMETRY metadata ----

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9001)]
    [InlineData(long.MaxValue)]
    public void Telemetry_InvalidInteger_UsesSpecStatusAndCode(long api)
    {
        var session = SessionHarness.Authenticated();

        var result = session.Receive(SessionHarness.Telemetry(api));

        var failure = SessionHarness.SingleResponse<FailureResponse>(result);
        Assert.Equal(BoltConnectionState.Failed, result.State);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", failure.Neo4jCode);
        Assert.NotNull(failure.Description);
        Assert.NotNull(failure.DiagnosticRecord);
    }

    [Fact]
    public void Telemetry_InvalidIdentifier_ThenWorkIgnored_ResetRecovers()
    {
        var session = SessionHarness.Authenticated();

        session.Receive(SessionHarness.Telemetry(9001));           // FAILURE -> FAILED
        var ignored = session.Receive(SessionHarness.Run());
        SessionHarness.SingleResponse<IgnoredResponse>(ignored);
        Assert.Equal(BoltConnectionState.Failed, ignored.State);

        var reset = session.Receive(SessionHarness.Reset());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
        Assert.Equal(BoltConnectionState.Ready, reset.State);
    }

    [Fact]
    public void Telemetry_WrongWireType_IsRejectedByCodec_BeforeSession()
    {
        // A non-integer TELEMETRY field is structurally invalid and belongs to B3-A decoding.
        var codec = new BoltMessageCodec();
        var exception = Assert.Throws<BoltMessageException>(
            () => codec.DecodeRequest(Hex("B15486647269766572"))); // TELEMETRY "driver"

        Assert.Equal(BoltMessageErrorCategory.InvalidFieldType, exception.Category);

        // The session's byte path never sees a typed request for it.
        var session = SessionHarness.Authenticated();
        var result = session.ReceiveBytes(Hex("B15486647269766572"));
        Assert.Equal(BoltConnectionState.Defunct, result.State);
        Assert.IsType<FailureResponse>(Assert.Single(result.Responses));
    }

    private sealed class NullFieldsOperation : IBoltOperation
    {
        public bool ThrowOnDispose { get; init; }

        public int DisposeCount { get; private set; }

        public IReadOnlyList<string> Fields => null!;

        public IReadOnlyList<PackStreamValue?>? ReadRow() => null;

        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("provider dispose failure");
            }
        }
    }
}
