using BoltSharp.Transport;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>
/// Â§16: accepted B3-B fault-injection patterns exercised through the real transport. The
/// mandatory case is the fail-closed disposal sequence: a provider disposal failure must lead to
/// DEFUNCT, connection closure, no RESET recovery and no second operation.
/// </summary>
public sealed class TransportFailureTests
{
    private sealed class PrepareThrowingProvider : IBoltOperationProvider
    {
        public int PrepareCount { get; private set; }

        public IBoltOperation? Prepare(RunRequest run)
        {
            PrepareCount++;
            throw new InvalidOperationException("prepare exploded");
        }
    }

    /// <summary>An operation whose field metadata getter throws when first read.</summary>
    private sealed class MetadataThrowingOperation : IBoltOperation
    {
        public int DisposeCount { get; private set; }

        public bool DisposeThrows { get; init; }

        public IReadOnlyList<string> Fields => throw new InvalidOperationException("metadata exploded");

        public IReadOnlyList<PackStreamValue?>? ReadRow() => null;

        public void Dispose()
        {
            DisposeCount++;
            if (DisposeThrows)
            {
                throw new InvalidOperationException("dispose exploded");
            }
        }
    }

    /// <summary>A provider returning one fixed operation instance (or a per-RUN factory result).</summary>
    private sealed class FixedOperationProvider : IBoltOperationProvider
    {
        private readonly Func<RunRequest, IBoltOperation?> _factory;
        public int PrepareCount { get; private set; }

        public FixedOperationProvider(Func<IBoltOperation> operation)
            : this(_ => operation())
        {
        }

        public FixedOperationProvider(Func<RunRequest, IBoltOperation?> factory) => _factory = factory;

        public IBoltOperation? Prepare(RunRequest run)
        {
            PrepareCount++;
            return _factory(run);
        }
    }

    private static ScriptedOperation ScalarOperation(int disposeThrowsOn = 0)
    {
        var operation = new ScriptedOperation(new[] { "answer" }, new PackStreamValue?[] { new PackStreamInteger(42) });
        if (disposeThrowsOn > 0)
        {
            operation.DisposeOverride = () => throw new InvalidOperationException("dispose exploded");
        }

        return operation;
    }

    [Fact]
    public async Task ProviderPrepareThrows_RecoverableFailure_ConnectionStaysUsable()
    {
        // Accepted B3-B semantics: an unexpected provider Prepare failure is a recoverable
        // FAILURE (FAILED state). The connection stays open; RESET recovers.
        var provider = new PrepareThrowingProvider();
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal(1, provider.PrepareCount);

        // FAILED state: pipelined work is IGNORED, then RESET recovers and the provider is
        // consulted again (no permanent damage from the thrown Prepare).
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<IgnoredResponse>(client.ReadResponse());
        client.SendRequest(new ResetRequest());
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal(2, provider.PrepareCount);
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task MetadataGetterThrows_FailureIsSurfaced()
    {
        var operation = new MetadataThrowingOperation();
        var provider = new FixedOperationProvider(_ => operation);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        // Metadata capture failure: the RUN is a recoverable failure (B3-B), so FAILURE arrives.
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, operation.DisposeCount); // the failed operation was retired exactly once
    }

    [Fact]
    public async Task MetadataFailurePlusDisposalFailure_FailsClosed()
    {
        var operation = new MetadataThrowingOperation { DisposeThrows = true };
        var provider = new FixedOperationProvider(_ => operation);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<FailureResponse>(client.ReadResponse()); // FAILURE still reported exactly once
        Assert.Equal("closed", client.WaitForClose(5_000)); // failed retirement: fail closed
        Assert.Equal(1, operation.DisposeCount); // no retry
    }

    [Fact]
    public async Task RecordReadThrows_RecordsThenFailure_RetiresExactlyOnce()
    {
        var operation = new ScriptedOperation(
            new[] { "answer" },
            new PackStreamValue?[] { new PackStreamInteger(1) });
        operation.ThrowOnReadNumber = 2; // second row read explodes
        var provider = new ScriptedOperationProvider(operation);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(1000, null));

        // Records already read are emitted, then FAILURE.
        var first = client.ReadResponse();
        Assert.IsType<RecordResponse>(first);
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal(1, operation.DisposeCount); // retired exactly once, no retry
    }

    [Fact]
    public async Task ReadFailurePlusDisposalFailure_FailsClosed()
    {
        var operation = new ScriptedOperation(
            new[] { "answer" },
            new PackStreamValue?[] { new PackStreamInteger(1) });
        operation.ThrowOnReadNumber = 2;
        operation.DisposeOverride = () => throw new InvalidOperationException("dispose exploded");
        var provider = new ScriptedOperationProvider(operation);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(1000, null));

        Assert.IsType<RecordResponse>(client.ReadResponse());
        Assert.IsType<FailureResponse>(client.ReadResponse()); // primary failure preserved
        Assert.Equal("closed", client.WaitForClose(5_000)); // failed retirement: terminal
        Assert.Equal(1, operation.DisposeCount);
    }

    /// <summary>
    /// The mandatory Â§16 fail-closed bridge: RUN opens operation #1; PULL exhausts it and
    /// disposal throws; the session becomes DEFUNCT and the connection closes. A RESET sent
    /// afterwards is never processed: no second RUN executes, no operation #2 opens, and no
    /// disposal retry occurs.
    /// </summary>
    [Fact]
    public async Task DisposalFailureDuringPull_FailClosed_NoResetRecovery_NoSecondOperation()
    {
        var operation = ScalarOperation(disposeThrowsOn: 1);
        var provider = new ScriptedOperationProvider(operation);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        // PULL drains the result; disposal of the exhausted operation throws. The failed
        // retirement reports the completion as FAILURE (never SUCCESS), then the connection closes.
        client.SendRequest(new PullRequest(1000, null));
        Assert.IsType<RecordResponse>(client.ReadResponse());
        Assert.IsType<FailureResponse>(client.ReadResponse());
        Assert.Equal("closed", client.WaitForClose(10_000));

        // Everything pipelined after the terminal outcome is never processed: any RESET/RUN write
        // lands on a socket the server has already closed (a local buffered write may still
        // succeed, so the proof is the server-side processing count).
        try
        {
            client.SendRequest(new ResetRequest());
            client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        }
        catch (Exception)
        {
            // A write failure after closure is also acceptable evidence.
        }

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));

        // One operation opened; one disposal attempt; no second RUN; no disposal retry.
        Assert.Equal(1, provider.PrepareCount);
        Assert.Equal(1, operation.DisposeCount);
        Assert.Equal(4, server.Diagnostics.MessagesProcessed); // HELLO, LOGON, RUN, PULL only
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task ClientDisconnectDuringActiveResult_OperationIsRetired()
    {
        var operation = new ScriptedOperation(
            new[] { "answer" },
            new PackStreamValue?[] { new PackStreamInteger(1) },
            new PackStreamValue?[] { new PackStreamInteger(2) });
        var provider = new ScriptedOperationProvider(operation);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(1, null)); // one record, has_more stays open
        client.ReadResult();

        client.Dispose(); // disconnect with the cursor still open

        Assert.True(TransportTestSupport.WaitUntil(
            () => server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, operation.DisposeCount); // owned operation retired on disconnect
        Assert.Equal(0, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task ListenerShutdownDuringActiveResult_RetiresOperation()
    {
        var operation = new ScriptedOperation(
            new[] { "answer" },
            new PackStreamValue?[] { new PackStreamInteger(1) },
            new PackStreamValue?[] { new PackStreamInteger(2) });
        var provider = new ScriptedOperationProvider(operation);
        var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => provider,
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(1, null));
        client.ReadResult();

        await server.StopAsync();
        await server.DisposeAsync();

        Assert.Equal(1, operation.DisposeCount); // owned operation retired on shutdown
        Assert.Equal(0, server.Diagnostics.ConnectionsActive);
    }
}
