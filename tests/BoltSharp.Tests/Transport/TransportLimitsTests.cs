using BoltSharp.Sessions;
using BoltSharp.Transport;
using BoltSharp.Framing;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Tests.Sessions;

using Xunit;

namespace BoltSharp.Tests.Transport;

/// <summary>§6/§10: bounded admission, message limits and incremental result pagination over TCP.</summary>
public sealed class TransportLimitsTests
{
    [Fact]
    public async Task ExcessConnectionAtCapacity_IsRefusedWithoutAffectingExisting()
    {
        var firstDone = new TaskCompletionSource();
        var operation = new ScriptedOperation(
            new[] { "answer" },
            new PackStreamValue?[] { new PackStreamInteger(42) });
        var first = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            MaxConnections = 1,
            OperationProviderFactory = () =>
            {
                var provider = new ScriptedOperationProvider(operation);
                firstDone.TrySetResult();
                return provider;
            },
        });
        await using var _ = first;

        // Client 1 occupies the only slot (handshake completed, session created).
        using var client1 = TransportTestSupport.ConnectAndAuthorize(first);
        var providerCreated = await Task.WhenAny(firstDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(firstDone.Task, providerCreated);
        Assert.Equal(1, first.Diagnostics.ConnectionsActive);

        // Client 2 is refused safely: closed immediately upon acceptance.
        using var client2 = TestBoltClient.Connect(first.BoundEndPoint!);
        Assert.Equal("closed", client2.WaitForClose(5_000));
        Assert.Equal(1, first.Diagnostics.ConnectionsRejectedForCapacity);

        // Client 1 is unaffected and completes a query.
        client1.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client1.ReadResponse());
        client1.SendRequest(new PullRequest(1000, null));
        Assert.Equal(2, client1.ReadResult().Length);
    }

    [Fact]
    public async Task MessageSizeLimit_IsReceiverSideAndEnforced()
    {
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            FramingLimits = new BoltFramingLimits(maxMessageSize: 100),
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        // A multi-chunk message whose cumulative size exceeds the limit: refused before the
        // payload is accumulated (reader-side bound).
        var chunks = TransportTestSupport.SplitForWire(
            TransportRequestEncoder.Encode(SessionHarness.Run("RETURN 42 AS answer" + new string(' ', 90))), 20);
        client.SendFramed(chunks);
        Assert.Equal("closed", client.WaitForClose(5_000));
        Assert.Equal(1, server.Diagnostics.ConnectionsFailed);
    }

    [Fact]
    public async Task PullAll_PaginatesInBoundedPasses_TerminalSummaryHasNoHasMore()
    {
        // 2500 rows: PULL n=-1 must serve in bounded passes of MaxRecordsPerResponse (1000) with
        // has_more=true, and the final summary must be SUCCESS with no has_more field (accepted
        // B3-B terminal semantics).
        var rows = Enumerable.Range(1, 2500)
            .Select(i => new PackStreamValue?[] { new PackStreamInteger(i) })
            .ToArray();
        var operation = new ScriptedOperation(new[] { "n" }, rows);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => new ScriptedOperationProvider(operation),
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());

        client.SendRequest(new PullRequest(-1, null));
        var totalRecords = 0;
        while (true)
        {
            var response = client.ReadResponse();
            if (response is RecordResponse)
            {
                totalRecords++;
                continue;
            }

            var summary = Assert.IsType<SuccessResponse>(response);
            if (summary.HasMore == true)
            {
                client.SendRequest(new PullRequest(-1, null));
                continue;
            }

            break;
        }

        Assert.Equal(2500, totalRecords);
        Assert.Equal(1, operation.DisposeCount);
    }

    [Fact]
    public async Task PullFiniteN_RespectsRequestedCount()
    {
        var rows = Enumerable.Range(1, 50)
            .Select(i => new PackStreamValue?[] { new PackStreamInteger(i) })
            .ToArray();
        var operation = new ScriptedOperation(new[] { "n" }, rows);
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            OperationProviderFactory = () => new ScriptedOperationProvider(operation),
        });
        using var client = TransportTestSupport.ConnectAndAuthorize(server);

        client.SendRequest(SessionHarness.Run("RETURN 42 AS answer"));
        Assert.IsType<SuccessResponse>(client.ReadResponse());
        client.SendRequest(new PullRequest(10, null));

        var records = 0;
        while (true)
        {
            var response = client.ReadResponse();
            if (response is RecordResponse)
            {
                records++;
                continue;
            }

            var summary = Assert.IsType<SuccessResponse>(response);
            if (summary.HasMore == true)
            {
                Assert.Equal(10, records); // the requested finite count was honored exactly
                client.SendRequest(new PullRequest(10, null));
                records = 0;
                continue;
            }

            break;
        }

        Assert.Equal(10, records); // final pass: 50 rows = 5 passes of 10 each
    }

    [Fact]
    public async Task CapacityRefusal_NeverAffectsListenerHealth()
    {
        await using var server = await TransportTestSupport.StartServerAsync(() => new BoltTcpServerOptions
        {
            CredentialVerifier = TestCredentialVerifier.AcceptAll(),
            MaxConnections = 1,
        });
        using var occupant = TestBoltClient.Connect(server.BoundEndPoint!);
        occupant.Handshake(new BoltProtocolVersion(5, 8));
        occupant.SendRequest(SessionHarness.Hello());
        Assert.IsType<SuccessResponse>(occupant.ReadResponse());

        using var excess = TestBoltClient.Connect(server.BoundEndPoint!);
        Assert.Equal("closed", excess.WaitForClose(5_000));

        // The listener still accepts after the refused connection.
        Assert.True(server.IsRunning);
        using var retry = TestBoltClient.Connect(server.BoundEndPoint!);
        Assert.Equal("closed", retry.WaitForClose(2_000)); // still at capacity
        Assert.Equal(2, server.Diagnostics.ConnectionsRejectedForCapacity);
    }
}
