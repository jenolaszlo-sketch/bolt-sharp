// B5-B-F01-R1-Q1 — recoverable FAILED/IGNORED/RESET sequence and F02 non-regression (§12, §18).
//
// A refused (unsupported) QUERY is recoverable: RUN → FAILURE 50N42, pipelined PULL → zero-field
// IGNORED (B0 7E), RESET recovery. F02 must remain unchanged by the F01 remediation.
using BoltSharp.B5BF01R1Q1.Probes.Infra;

using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.B5BF01R1Q1.Probes;

public sealed class IgnoreAndF02Tests
{
    [Fact]
    public async Task RefusedQuery_FailureThenPipelinedPull_ProducesZeroFieldIgnored_B07E()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        client.SendFramed(ProbeMessages.UnsupportedQuery());
        var failure = client.ReadMessage();
        Assert.NotNull(failure);
        Assert.Equal(0x7F, failure![1]);
        var metadata = ProbeMessages.FailureMetadata(failure);
        Assert.Equal("50N42", metadata["gql_status"]); // F02 generic classification unchanged
        Assert.Equal("Neo.ClientError.Request.Invalid", metadata["neo4j_code"]);

        // Pipelined PULL after the FAILURE: zero-field IGNORED, byte oracle B0 7E.
        client.SendFramed(ProbeMessages.Pull(-1));
        var ignored = client.ReadMessage();
        Assert.NotNull(ignored);
        Assert.Equal("B07E", Convert.ToHexString(ignored!)); // exact IGNORED arity oracle

        // RESET returns the connection to READY (accepted recoverability rules).
        client.SendFramed(ProbeMessages.Reset());
        var reset = client.ReadMessage();
        Assert.NotNull(reset);
        Assert.Equal(0x70, reset![1]);

        // The connection is usable again on the SAME connection (recoverable, unlike F01).
        client.SendFramed(ProbeMessages.ScalarQuery());
        var run = client.ReadMessage();
        Assert.NotNull(run);
        Assert.Equal(0x70, run![1]);

        Assert.Equal(2, provider.PrepareCount); // the refused attempt + the supported query
        Assert.Equal(1, provider.SuccessCount); // only the supported query opened an operation

        ProbeHost.AppendEvidence(
            "11-ignored-f02.txt",
            "unsupported query: FAILURE 50N42; pipelined PULL → B0 7E; RESET → SUCCESS; same-connection recovery OK\n");
    }

    [Fact]
    public async Task F02_NonRegression_ReferenceGraphUnchanged_AfterRefusals()
    {
        var provider = new CountingReferenceProvider();
        await using var server = await ProbeHost.StartServerAsync(new ProbeVerifier(), () => provider);
        using var client = await ProbeHost.ConnectAndAuthorizeAsync(server);

        // Refusals first: unsupported query (recoverable F02) and unsupported BEGIN (terminal F01).
        client.SendFramed(ProbeMessages.UnsupportedQuery());
        var f02 = client.ReadMessage();
        Assert.NotNull(f02);
        Assert.Equal("50N42", ProbeMessages.FailureMetadata(f02!)["gql_status"]);
        client.SendFramed(ProbeMessages.Pull(-1));
        var ignored = client.ReadMessage(); // IGNORED
        Assert.Equal("B07E", Convert.ToHexString(ignored!));

        // RESET returns the recoverable FAILED connection to READY before further queries.
        client.SendFramed(ProbeMessages.Reset());
        _ = client.ReadMessage();

        // The reference graph is unchanged and the connection still serves supported queries.
        client.SendFramed(ProbeMessages.GraphQuery());
        var run = client.ReadMessage();
        Assert.NotNull(run);
        Assert.Equal(0x70, run![1]);
        client.SendFramed(ProbeMessages.Pull(-1));
        var record = client.ReadMessage();
        Assert.NotNull(record);
        Assert.Equal(0x71, record![1]); // one (a, r, b) record — graph unchanged

        ProbeHost.AppendEvidence(
            "11-ignored-f02.txt",
            "F02 non-regression: 50N42 classification unchanged; reference graph unchanged after refusals\n");
    }
}
