// Q-B5B-05-PROBE-01 — plaintext raw-protocol same-connection RESET-recovery demonstrations.
//
// These probes use the producer-owned raw client (ProbeClient) and the real BoltSharp listener
// with the accepted reference backend. They establish the protocol and connection-identity
// requirements from commission §5 and §6.
//
// Each demonstration records the same-connection verdict twice: once while the sequence is in
// progress (the connection must still be admitted and active) and once after the orderly
// GOODBYE (the connection must complete exactly once without ever failing). Both verdicts must
// place the recoverable FAILURE, the RESET and the subsequent successful operation on one
// physical connection.
using System.Net;

using BoltSharp.Q5B05.Probe.Infra;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Q5B05.Probe;

public sealed class ResetRecovery
{
    private const string User = "probe-owner-user";
    private const string Secret = "probe-owner-secret";

    // The accepted reference-backend refusal: every RUN the allowlist does not recognize is
    // refused by the provider, and the session deterministically maps that to a RECOVERABLE
    // FAILURE (50N42 Neo.ClientError.Request.Invalid) leaving the connection open.
    private const string UnsupportedQuery = "MATCH (n) DETACH DELETE n";

    private const string SupportedQuery = "RETURN 42 AS answer";

    private const string ResetResponseHex = "B170A0";

    private const string IgnoredResponseHex = "B07E";

    private static readonly TimeSpan ReadBound = TimeSpan.FromSeconds(20);

    private static SameConnectionVerdict SameConnection(
        BoltTcpServer server,
        ProbeTrace trace,
        ProbeClient client,
        IPEndPoint? portBefore,
        ProbeProviderLog providers,
        RecordingProxy? proxy,
        string phase) =>
        new()
        {
            ClientLocalEndPoint = portBefore ?? client.LocalEndPoint,
            AcceptedConnectionId = trace.AcceptedConnectionIds.Count == 1
                ? trace.AcceptedConnectionIds[0]
                : "<none-or-many>",
            MessageProcessedConnectionIds = trace.MessageProcessedConnectionIds,
            ConnectionsAccepted = server.Diagnostics.ConnectionsAccepted,
            ConnectionsActive = server.Diagnostics.ConnectionsActive,
            ConnectionsCompleted = server.Diagnostics.ConnectionsCompleted,
            ConnectionsFailed = server.Diagnostics.ConnectionsFailed,
            MessagesProcessed = server.Diagnostics.MessagesProcessed,
            ProviderInstanceCount = providers.Providers.Count,
            ClientSocketStillOpen = client.IsOpen,
            ClientLocalPortUnchanged = portBefore is null || portBefore.Port == client.LocalEndPoint.Port,
            CapturedBeforeOrderlyCompletion = phase == "during-sequence",
            VerdictPhase = phase,
            ProxyConnectionId = proxy is not null && proxy.ConnectionCount == 1
                ? proxy.Connections()[0].ConnectionId
                : null,
            ProxyMessageCount = proxy is not null && proxy.ConnectionCount == 1
                ? proxy.Connections()[0].Messages.Count
                : 0,
        };

    private static void AssertIndependent(SameConnectionVerdict verdict, string label, string log)
    {
        Assert.True(verdict.ExactlyOneAcceptedConnection,
            $"{label}: exactly one accepted physical connection. {verdict.Summary}");
        Assert.True(verdict.EveryMessageOnThatConnection,
            $"{label}: every processed message carried the accepted connection id. {verdict.Summary}");
        Assert.True(verdict.ConnectionNeverFailed,
            $"{label}: the connection never failed (a recoverable FAILURE is not a transport " +
            $"fault). {verdict.Summary}");
        Assert.True(verdict.ConnectionAtMostOneOrderlyCompletion,
            $"{label}: at most the one orderly completion. {verdict.Summary}");
        if (verdict.CapturedBeforeOrderlyCompletion)
        {
            Assert.True(verdict.ConnectionStillActive,
                $"{label}: the connection was still admitted and active during the sequence. " +
                $"{verdict.Summary}");
        }

        Assert.True(verdict.ExactlyOneProviderInstance,
            $"{label}: exactly one per-connection provider instance (one physical connection). " +
            $"{verdict.Summary}");
        Assert.True(verdict.ClientSocketIdentityHeld,
            $"{label}: the client socket identity held (local port unchanged). {verdict.Summary}");
        Assert.True(verdict.SamePhysicalConnection,
            $"{label}: every line of evidence places the failure, the RESET and the subsequent " +
            $"successful operation on one physical connection. {verdict.Summary}");
        ProbeHost.AppendEvidence(log, $"{label} [{verdict.VerdictPhase}]: {verdict.Summary}\n");
    }

    private static void AssertOrderlyClose(
        BoltTcpServer server,
        ProbeClient client,
        ProbeTrace trace,
        ProbeProviderLog providers,
        IPEndPoint? portBefore,
        RecordingProxy? proxy,
        string label,
        string log)
    {
        // Step 10 of the protocol sequence: an orderly lifecycle completion.
        client.SendFramed(ProbeMessages.Goodbye());
        Assert.True(client.HasNoFurtherMessage(ReadBound), "GOODBYE produced no further message");

        var afterGoodbye = SameConnection(server, trace, client, portBefore, providers, proxy,
            "after-goodbye");
        AssertIndependent(afterGoodbye, label, log);
        Assert.Equal(1L, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0L, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0L, server.Diagnostics.ConnectionsActive);
    }

    private static byte[] Payload(byte[]? message) =>
        message ?? throw new InvalidOperationException("the server closed the connection");

    private async static Task AuthorizeAsync(ProbeClient client)
    {
        Assert.Equal("00000805", await client.HandshakeAsync());
        client.SendFramed(ProbeMessages.Hello("Q-B5B-05-PROBE-01/1.0", "BoltSharp-Probe/1.0"));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Logon("basic", User, Secret));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
    }

    private static void RunAndConsume(ProbeClient client)
    {
        client.SendFramed(ProbeMessages.Run(SupportedQuery));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Pull(-1));
        Assert.Equal(ProbeMessages.RecordTag, Payload(client.ReadMessage(ReadBound))[1]);
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
    }

    private static void AssertRecoverableFailure(ProbeClient client, bool pipelined = false)
    {
        if (pipelined)
        {
            client.SendRaw(ProbeMessages.Frame(ProbeMessages.Run(UnsupportedQuery))
                .Concat(ProbeMessages.Frame(ProbeMessages.Pull(1000))).ToArray());
        }
        else
        {
            client.SendFramed(ProbeMessages.Run(UnsupportedQuery));
            client.SendFramed(ProbeMessages.Pull(1000));
        }

        var failure = Payload(client.ReadMessage(ReadBound));
        Assert.Equal(ProbeMessages.FailureTag, failure[1]);
        Assert.Contains(ProbeMessages.AsciiHex("50N42"), Convert.ToHexString(failure),
            StringComparison.Ordinal);
        Assert.Contains(ProbeMessages.AsciiHex("Neo.ClientError.Request.Invalid"),
            Convert.ToHexString(failure), StringComparison.Ordinal);
        var ignored = Payload(client.ReadMessage(ReadBound));
        Assert.Equal(IgnoredResponseHex, Convert.ToHexString(ignored));
    }

    private static void AssertResetRecovers(ProbeClient client)
    {
        client.SendFramed(ProbeMessages.Reset());
        Assert.Equal(ResetResponseHex, Convert.ToHexString(Payload(client.ReadMessage(ReadBound))));
    }

    // ---- P-01: the principal acceptance gate ------------------------------

    [Fact]
    public async Task P01_Plaintext_RecoverableFailureResetAndSubsequentOperation_SameConnection()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        using var client = new ProbeClient();
        const string label = "P-01 plaintext recoverable-failure RESET recovery";
        const string log = "p01-plaintext-reset-recovery.txt";
        await client.ConnectAsync((IPEndPoint)proxy.BoundEndPoint);
        var portBefore = client.LocalEndPoint;
        await AuthorizeAsync(client);

        // Step 2: a valid initial operation.
        RunAndConsume(client);

        // Steps 3-4: the recoverable FAILURE and the failed session state.
        AssertRecoverableFailure(client);

        // Steps 5-7: RESET, its successful response, and the recovered session state.
        AssertResetRecovers(client);

        // Steps 8-9: a subsequent valid operation on the same connection.
        RunAndConsume(client);

        var duringSequence = SameConnection(server, trace, client, portBefore, providers, proxy,
            "during-sequence");
        AssertIndependent(duringSequence, label, log);
        AssertOrderlyClose(server, client, trace, providers, portBefore, proxy, label, log);

        var transcript = proxy.ConnectionCount == 1 ? proxy.Connections()[0] : null;
        var document = new System.Text.StringBuilder();
        document.AppendLine("P-01 plaintext: recoverable FAILURE -> IGNORED -> RESET -> successful " +
                            "operation -> orderly close, all on one physical connection");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("connection ids: accepted=[" +
                            string.Join(",", trace.AcceptedConnectionIds) + "] messageProcessed=[" +
                            string.Join(",", trace.MessageProcessedConnectionIds) + "]");
        document.AppendLine("RESET messages observed server-side: " +
                            trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("proxy transcript (" + proxy.ConnectionCount + " connection(s)): " +
                            (transcript is null ? "<not-single>" : transcript.Sequence()));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence(log, document.ToString());

        Assert.Equal(1L, trace.CountProcessedSignatures("0x0F"));
        Assert.Single(providers.Providers);
        Assert.Equal(3, providers.Single?.PrepareCount);
        Assert.Equal(2, providers.Single?.SuccessCount);
        Assert.Equal(1, providers.Single?.RefusalCount);
    }

    // ---- P-02: repeated recoverable failure and RESET ---------------------

    [Fact]
    public async Task P02_Plaintext_RepeatedFailureAndReset_CyclesOnOneConnection()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        using var client = new ProbeClient();
        const string label = "P-02 repeated recoverable-failure RESET cycles";
        const string log = "p02-plaintext-repeated-reset.txt";
        await client.ConnectAsync((IPEndPoint)proxy.BoundEndPoint);
        var portBefore = client.LocalEndPoint;
        await AuthorizeAsync(client);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            AssertRecoverableFailure(client);
            AssertResetRecovers(client);
        }

        RunAndConsume(client);

        var duringSequence = SameConnection(server, trace, client, portBefore, providers, proxy,
            "during-sequence");
        AssertIndependent(duringSequence, label, log);
        AssertOrderlyClose(server, client, trace, providers, portBefore, proxy, label, log);

        var transcript = proxy.ConnectionCount == 1 ? proxy.Connections()[0] : null;
        var document = new System.Text.StringBuilder();
        document.AppendLine("P-02 plaintext: three recoverable FAILURE -> IGNORED -> RESET cycles, " +
                            "then a successful operation and an orderly close, on one physical " +
                            "connection");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("RESET messages observed server-side: " +
                            trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("proxy transcript (" + proxy.ConnectionCount + " connection(s)): " +
                            (transcript is null ? "<not-single>" : transcript.Sequence()));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence(log, document.ToString());

        Assert.Equal(3L, trace.CountProcessedSignatures("0x0F"));
        Assert.Single(providers.Providers);
        Assert.Equal(1L, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(1L, server.Diagnostics.ConnectionsCompleted);
    }

    // ---- P-03: the exact Q-B5B-05 IGNORED scenario ------------------------

    [Fact]
    public async Task P03_Plaintext_PipelinedFailureThenIgnored_ThenResetRecovers()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var proxy = RecordingProxy.Start(server.BoundEndPoint!);

        using var client = new ProbeClient();
        const string label = "P-03 pipelined FAILURE+IGNORED then RESET recovery";
        const string log = "p03-plaintext-pipelined-ignored.txt";
        await client.ConnectAsync((IPEndPoint)proxy.BoundEndPoint);
        var portBefore = client.LocalEndPoint;
        await AuthorizeAsync(client);

        // The observed Q-B5B-05 scenario: RUN and PULL pipelined in one client write; the server
        // answers FAILURE for RUN and the byte-exact zero-field IGNORED for PULL.
        AssertRecoverableFailure(client, pipelined: true);
        AssertResetRecovers(client);
        RunAndConsume(client);

        var duringSequence = SameConnection(server, trace, client, portBefore, providers, proxy,
            "during-sequence");
        AssertIndependent(duringSequence, label, log);
        AssertOrderlyClose(server, client, trace, providers, portBefore, proxy, label, log);

        var transcript = proxy.ConnectionCount == 1 ? proxy.Connections()[0] : null;
        var document = new System.Text.StringBuilder();
        document.AppendLine("P-03 plaintext: pipelined RUN+PULL answers FAILURE(50N42) then the " +
                            "byte-exact zero-field IGNORED (B0 7E); RESET recovers the same " +
                            "physical connection and a subsequent operation succeeds");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("proxy transcript (" + proxy.ConnectionCount + " connection(s)): " +
                            (transcript is null ? "<not-single>" : transcript.Sequence()));
        document.AppendLine("RESET messages observed server-side: " +
                            trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence(log, document.ToString());

        Assert.Equal(1L, trace.CountProcessedSignatures("0x0F"));
        Assert.Single(providers.Providers);
    }

    // ---- P-05: an unaffected concurrent client ----------------------------

    [Fact]
    public async Task P05_Plaintext_ConcurrentClientUnaffected_AndRecoveryIsIsolated()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;

        using var healthy = new ProbeClient();
        await healthy.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
        var healthyPort = healthy.LocalEndPoint;
        await AuthorizeAsync(healthy);

        using var failed = new ProbeClient();
        await failed.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
        var failedPort = failed.LocalEndPoint;
        await AuthorizeAsync(failed);

        var healthyId = trace.ConnectionIdForClientPort(healthyPort.Port);
        var failedId = trace.ConnectionIdForClientPort(failedPort.Port);
        Assert.NotNull(healthyId);
        Assert.NotNull(failedId);
        Assert.NotEqual(healthyId, failedId);

        // The failing client enters the failed session state.
        AssertRecoverableFailure(failed);

        // The unaffected client still works while the other session is failed.
        RunAndConsume(healthy);

        // The failed client recovers independently on its own connection.
        AssertResetRecovers(failed);
        RunAndConsume(failed);

        healthy.SendFramed(ProbeMessages.Goodbye());
        Assert.True(healthy.HasNoFurtherMessage(ReadBound), "GOODBYE produced no further message");
        failed.SendFramed(ProbeMessages.Goodbye());
        Assert.True(failed.HasNoFurtherMessage(ReadBound), "GOODBYE produced no further message");

        // Both connections completed orderly and never failed.
        Assert.Equal(2L, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0L, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0L, server.Diagnostics.ConnectionsActive);
        Assert.Equal(2, providers.Providers.Count);
        Assert.NotEqual(healthyPort.Port, failedPort.Port);
        Assert.True(healthy.LocalEndPoint.Port == healthyPort.Port, "the healthy client's socket identity held");
        Assert.True(failed.LocalEndPoint.Port == failedPort.Port, "the failed client's socket identity held");

        // Per-connection identity: each client's messages were processed on its own connection.
        Assert.Equal(1L, trace.CountProcessedSignatures(failedId!, "0x0F"));
        Assert.Equal(0L, trace.CountProcessedSignatures(healthyId!, "0x0F"));
        Assert.Equal(2, trace.ConnectionIds().Count);

        // Per-connection provider counters describe what ran on each physical connection.
        var perConnection = providers.Providers
            .Select(p => (Prepare: p.PrepareCount, Success: p.SuccessCount, Refusal: p.RefusalCount))
            .OrderBy(t => t.Prepare)
            .ToArray();
        Assert.Equal((1, 1, 0), perConnection[0]); // the healthy session: one supported query
        Assert.Equal((2, 1, 1), perConnection[1]); // the failed session: one refused + one supported

        var document = new System.Text.StringBuilder();
        document.AppendLine("P-05 plaintext: healthy client unaffected while the other session is " +
                            "failed; each connection then recovers and completes independently");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("connection ids: accepted=[" +
                            string.Join(",", trace.AcceptedConnectionIds) + "]");
        document.AppendLine("healthy local port: " + healthyPort.Port + "; failed-session local " +
                            "port: " + failedPort.Port);
        document.AppendLine("per-connection provider counts: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount} " +
                                                 $"success={p.SuccessCount} " +
                                                 $"refused={p.RefusalCount}")));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("p05-plaintext-concurrent-client.txt", document.ToString());

        Assert.Equal(2L, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(2L, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0L, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(2, providers.Providers.Count);
        Assert.NotEqual(healthyPort.Port, failedPort.Port);
    }
}