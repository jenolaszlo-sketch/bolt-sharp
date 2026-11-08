using BoltSharp.Backend;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Backend;

/// <summary>
/// B3-C4 / B3-C failure injection: the ten documented fault modes applied to the real reference
/// backend while the accepted B3-B lifecycle contract remains enforced (exactly-once retirement;
/// failed retirement is terminal; no fabricated records).
/// </summary>
public sealed class ReferenceFailureTests
{
    [Fact]
    public void PrepareFailure_BeforeReturn_IsRecoverable_WithoutOwnership()
    {
        var provider = new FaultyProvider { ThrowOnPrepare = true };
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        var failure = SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Failed, run.State);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Empty(provider.Operations);

        var reset = session.Receive(new ResetRequest());
        SessionHarness.SingleResponse<SuccessResponse>(reset);
    }

    [Fact]
    public void InvalidMetadata_NullFields_IsRetiredExactlyOnce()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { NullFields = true } };
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal(BoltConnectionState.Failed, run.State);
        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.DisposeAttempts);
        Assert.Null(session.LastCleanupFailure);
    }

    [Fact]
    public void InvalidMetadata_NullElement_IsRetiredExactlyOnce()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { NullElement = true } };
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal(BoltConnectionState.Failed, run.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeAttempts);
    }

    [Fact]
    public void MetadataGetterFailure_IsContained_WithSingleAccess()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { ThrowOnFields = true } };
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal(BoltConnectionState.Failed, run.State);
        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.FieldsReads); // single acquisition; the getter is never re-read
        Assert.Equal(1, operation.DisposeAttempts);
        Assert.False(session.HasActiveOperation);
    }

    [Fact]
    public void FirstRecordReadFailure_IsFailureFailed()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { ThrowOnReadNumber = 1 } };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var pull = session.Receive(new PullRequest(-1, null));

        var failure = SessionHarness.SingleResponse<FailureResponse>(pull);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Failed, pull.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeAttempts);
    }

    [Fact]
    public void FailureAfterOneRecord_PreservesTheRecordPrefix()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { ThrowOnReadNumber = 2 } };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.Equal(2, pull.Responses.Count);
        Assert.Equal("Alice", BackendTestSupport.StringOf(pull.Responses[0]));
        var failure = Assert.IsType<FailureResponse>(pull.Responses[1]);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Failed, pull.State);
        Assert.DoesNotContain(pull.Responses, r => r is SuccessResponse);
    }

    [Fact]
    public void InvalidRecordColumnCount_EmitsNoBadRecord_RowShapeFailure()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { RowWidthOverride = 2 } };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.DoesNotContain(pull.Responses, r => r is RecordResponse);
        var failure = SessionHarness.SingleResponse<FailureResponse>(pull);
        Assert.Equal("50N42", failure.GqlStatus);
        Assert.Equal(
            "A result row does not match the announced result fields.",
            BoltSharp.Tests.Messages.BoltMessageFieldsTestAccess.GetString(failure.Metadata, "description"));
        Assert.Equal(BoltConnectionState.Failed, pull.State);
        Assert.Equal(1, Assert.Single(provider.Operations).DisposeAttempts);
    }

    [Fact]
    public void DisposalFailureAtCompletion_IsFailureDefunct_NoSuccess()
    {
        var provider = new FaultyProvider { Wrap = op => new FaultyOperation(op) { ThrowOnDispose = true } };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        var pull = session.Receive(new PullRequest(-1, null));

        Assert.Equal(2, pull.Responses.Count);
        Assert.IsType<RecordResponse>(pull.Responses[0]);
        var failure = Assert.IsType<FailureResponse>(pull.Responses[1]);
        Assert.Equal("50N00", failure.GqlStatus);
        Assert.Equal(BoltConnectionState.Defunct, pull.State);
        Assert.True(pull.ShouldClose);

        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.DisposeAttempts);
        Assert.Same(operation.CleanupError, session.LastCleanupFailure);

        var reset = session.Receive(new ResetRequest());
        Assert.Empty(reset.Responses);
        Assert.Equal(BoltConnectionState.Defunct, reset.State);
    }

    [Fact]
    public void ReadFailureThenDisposalFailure_PrimaryPreserved_AndDefunct()
    {
        var provider = new FaultyProvider
        {
            Wrap = op => new FaultyOperation(op) { ThrowOnReadNumber = 1, ThrowOnDispose = true },
        };
        var session = BackendTestSupport.Authenticated(provider);
        session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.PropertyQueryText));

        var pull = session.Receive(new PullRequest(-1, null));

        var failure = SessionHarness.SingleResponse<FailureResponse>(pull);
        Assert.Equal("50N00", failure.GqlStatus); // primary provider failure preserved
        Assert.Equal(BoltConnectionState.Defunct, pull.State);
        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.DisposeAttempts);
        Assert.Same(operation.CleanupError, session.LastCleanupFailure); // secondary fact recorded
    }

    [Fact]
    public void MetadataFailureThenDisposalFailure_IsDefunct_OneAttempt()
    {
        var provider = new FaultyProvider
        {
            Wrap = op => new FaultyOperation(op) { NullFields = true, ThrowOnDispose = true },
        };
        var session = BackendTestSupport.Authenticated(provider);

        var run = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));

        SessionHarness.SingleResponse<FailureResponse>(run);
        Assert.Equal(BoltConnectionState.Defunct, run.State);
        Assert.True(run.ShouldClose);
        var operation = Assert.Single(provider.Operations);
        Assert.Equal(1, operation.DisposeAttempts);
        Assert.Same(operation.CleanupError, session.LastCleanupFailure);

        var runAgain = session.Receive(BackendTestSupport.Run(ReferenceQueryEvaluator.ScalarQueryText));
        Assert.Empty(runAgain.Responses);
        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, operation.DisposeAttempts);
    }

    [Fact]
    public void UseAfterDisposal_IsDeterministic()
    {
        var operation = new ReferenceResultOperation(
            new[] { "x" },
            new[] { new PackStreamValue?[] { new BoltSharp.PackStream.PackStreamInteger(1) } });

        operation.Dispose();
        operation.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => operation.ReadRow());
    }
}
