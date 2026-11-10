// B5-B-F01-R1-Q1 — fail-closed resource ownership (§17): reviewer-owned fault-injecting provider
// through the supported test-host configuration seam (BoltTcpServerOptions.OperationProviderFactory).
//
// Triggering an operation-retirement failure after a supported RUN/PULL must yield: exactly one
// disposal attempt, FAILURE (never a successful summary), DEFUNCT terminality, no RESET revival,
// no second RUN, no disposal retry, and other connections remain usable.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class FailClosedRetirementTests
{
    [Fact]
    public async Task DisposalFailure_DuringPullRetirement_IsTerminal_NoRetry_NoRevival()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        var events = new List<BoltTransportDiagnosticEvent>();
        server.Diagnostics.Event += events.Add;
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        provider.FailNextDisposal = true;

        client.SendFramed(ProbeMessages.ScalarQuery());
        var run = client.ReadMessage();
        Assert.NotNull(run);
        Assert.Equal(0x70, run![1]); // one operation opened

        client.SendFramed(ProbeMessages.Pull(-1));
        var record = client.ReadMessage();
        Assert.NotNull(record);
        Assert.Equal(0x71, record![1]); // the row is delivered before retirement

        // Retirement fails: the completion is reported as FAILURE, never as a successful summary.
        var terminal = client.ReadMessage();
        Assert.NotNull(terminal);
        Assert.Equal(0x7F, terminal![1]);
        var metadata = ProbeMessages.FailureMetadata(terminal);
        Assert.Equal("50N00", metadata["gql_status"]);
        Assert.Equal("Neo.DatabaseError.General.UnknownError", metadata["neo4j_code"]);

        // The connection closes (DEFUNCT), no further message is fabricated.
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));

        // Exactly one disposal attempt; the failure is not retried during cleanup.
        Assert.Equal(1, provider.DisposeAttempts);
        Assert.Equal(1, provider.DisposeFailures);
        Assert.Equal(1, provider.PrepareCount);

        Assert.True(await WaitUntilAsync(() => server.Diagnostics.ConnectionsActive == 0));

        ProbeHost.AppendEvidence(
            "12-fail-closed-retirement.txt",
            "injected disposal failure: one operation, one disposal attempt, FAILURE (50N00) instead of summary, connection closed\n");
    }

    [Fact]
    public async Task DefunctSession_CannotExecuteSecondRun_AndHealthyConnectionsRemainUsable()
    {
        var provider = new ProbeProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        provider.FailNextDisposal = true;
        client.SendFramed(ProbeMessages.ScalarQuery());
        _ = client.ReadMessage();
        client.SendFramed(ProbeMessages.Pull(-1));
        _ = client.ReadMessage(); // record
        var terminal = client.ReadMessage();
        Assert.Equal(0x7F, terminal![1]);
        Assert.True(await client.WaitForCloseAsync(TimeSpan.FromSeconds(5)));

        // No second RUN on the failed connection: the server closed it; anything written is never
        // processed (no response, no operation).
        await client.SendToPossiblyClosedSocketAsync(ProbeMessages.ScalarQuery());
        Assert.False(await client.HasAdditionalMessageAsync());
        Assert.Equal(1, provider.PrepareCount); // unchanged: no second operation
        Assert.Equal(1, provider.DisposeAttempts); // no disposal retry

        // RESET cannot restore the connection.
        await client.SendToPossiblyClosedSocketAsync(ProbeMessages.Reset());
        Assert.False(await client.HasAdditionalMessageAsync());
        Assert.Equal(1, provider.DisposeAttempts); // still no retry

        // A new physical connection remains fully usable (healthy connection, not resurrection).
        using var healthy = await ProbeHost.ConnectAndAuthorizeAsync(server);
        healthy.SendFramed(ProbeMessages.ScalarQuery());
        var healthyRun = healthy.ReadMessage();
        Assert.NotNull(healthyRun);
        Assert.Equal(0x70, healthyRun![1]);
        healthy.SendFramed(ProbeMessages.Pull(-1));
        var healthyRecord = healthy.ReadMessage();
        Assert.NotNull(healthyRecord);
        Assert.Equal("B171912A", Convert.ToHexString(healthyRecord!)); // 42 again, healthy path intact

        ProbeHost.AppendEvidence(
            "12-fail-closed-retirement.txt",
            "DEFUNCT session: no second RUN, no RESET revival, no disposal retry; new connection healthy (B171912A)\n");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }
}
