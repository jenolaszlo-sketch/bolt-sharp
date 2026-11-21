// Q-B5B-05-PROBE-01 — negative controls for the same-connection RESET-recovery oracle.
//
// Each control injects or simulates a specific false-positive recovery scenario and verifies
// that the intended oracle rejects it. Controls NC-01 and NC-02 mutate only the probe's own
// client behavior (never the production listener). NC-03 uses a producer-owned provider whose
// operation disposal fails exactly once, injected in an isolated temporary test process. NC-04
// substitutes a terminal failure classification for a recoverable one. No canonical production
// behavior is modified: the listener always runs the accepted production engine with a
// probe-supplied provider.
using System.Net;

using BoltSharp.Backend;
using BoltSharp.Handshake;
using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Q5B05.Probe.Infra;
using BoltSharp.Sessions;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Q5B05.Probe;

public sealed class NegativeControls
{
    private const string User = "probe-owner-user";
    private const string Secret = "probe-owner-secret";
    private const string UnsupportedQuery = "MATCH (n) DETACH DELETE n";
    private const string SupportedQuery = "RETURN 42 AS answer";
    private const string ResetResponseHex = "B170A0";
    private const string IgnoredResponseHex = "B07E";
    private static readonly TimeSpan ReadBound = TimeSpan.FromSeconds(20);

    private static byte[] Payload(byte[]? message) =>
        message ?? throw new InvalidOperationException("the server closed the connection");

    private static async Task AuthorizeAsync(ProbeClient client, IPEndPoint endpoint)
    {
        await client.ConnectAsync(endpoint);
        Assert.Equal("00000805", await client.HandshakeAsync());
        client.SendFramed(ProbeMessages.Hello("Q-B5B-05-PROBE-01/1.0", "BoltSharp-Probe/1.0"));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Logon("basic", User, Secret));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
    }

    // ---- NC-01: a replacement connection must be rejected ----------------

    [Fact]
    public async Task NC01_ReplacementConnection_IsRejectedByTheSameConnectionOracle()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var endpoint = (IPEndPoint)server.BoundEndPoint!;

        using var first = new ProbeClient();
        await AuthorizeAsync(first, endpoint);
        var firstPort = first.LocalEndPoint;

        // A recoverable failure on the first connection.
        first.SendFramed(ProbeMessages.Run(UnsupportedQuery));
        Assert.Equal(ProbeMessages.FailureTag, Payload(first.ReadMessage(ReadBound))[1]);
        first.SendFramed(ProbeMessages.Pull(1000));
        Assert.Equal(IgnoredResponseHex, Convert.ToHexString(Payload(first.ReadMessage(ReadBound))));

        // RESET on the first connection, then close it WITHOUT a subsequent operation.
        first.SendFramed(ProbeMessages.Reset());
        Assert.Equal(ResetResponseHex, Convert.ToHexString(Payload(first.ReadMessage(ReadBound))));
        first.Dispose();

        // The subsequent successful operation runs on a REPLACEMENT physical connection: a new
        // socket, a new handshake and a new authentication, as the proxy transcript shows.
        using var second = new ProbeClient();
        await AuthorizeAsync(second, endpoint);
        var secondPort = second.LocalEndPoint;
        Assert.NotEqual(firstPort.Port, secondPort.Port);

        second.SendFramed(ProbeMessages.Run(SupportedQuery));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(second.ReadMessage(ReadBound))[1]);
        second.SendFramed(ProbeMessages.Pull(-1));
        Assert.Equal(ProbeMessages.RecordTag, Payload(second.ReadMessage(ReadBound))[1]);
        Assert.Equal(ProbeMessages.SuccessTag, Payload(second.ReadMessage(ReadBound))[1]);
        second.SendFramed(ProbeMessages.Goodbye());

        var document = new System.Text.StringBuilder();
        document.AppendLine("NC-01 replacement connection: a subsequent successful operation on a " +
                            "different physical connection must be rejected by the " +
                            "same-connection oracle.");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("connection ids: accepted=[" +
                            string.Join(",", trace.AcceptedConnectionIds) + "]");
        document.AppendLine("first client local port: " + firstPort.Port + "; replacement client " +
                            "local port: " + secondPort.Port);
        document.AppendLine("per-connection providers: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount}")));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("nc01-replacement-connection.txt", document.ToString());

        Assert.Equal(2L, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(2, providers.Providers.Count);
        Assert.Equal(2, trace.AcceptedConnectionIds.Count);
    }

    // ---- NC-02: a missing RESET cannot report valid recovery ------------

    [Fact]
    public async Task NC02_MissingReset_CannotReportValidRecovery()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var endpoint = (IPEndPoint)server.BoundEndPoint!;

        using var client = new ProbeClient();
        await AuthorizeAsync(client, endpoint);
        var portBefore = client.LocalEndPoint;

        // A recoverable failure, and then NO RESET.
        client.SendFramed(ProbeMessages.Run(UnsupportedQuery));
        Assert.Equal(ProbeMessages.FailureTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Pull(1000));
        Assert.Equal(IgnoredResponseHex, Convert.ToHexString(Payload(client.ReadMessage(ReadBound))));

        // The recovery question: attempt the subsequent operation without RESET. The server
        // answers IGNORED and the session remains failed.
        var recoveryAttempted = ProbeMessages.Run(SupportedQuery);
        client.SendFramed(recoveryAttempted);
        var withoutReset = Payload(client.ReadMessage(ReadBound));
        Assert.Equal(IgnoredResponseHex, Convert.ToHexString(withoutReset));

        // Recovery only succeeds after the RESET the omission left out.
        client.SendFramed(ProbeMessages.Reset());
        Assert.Equal(ResetResponseHex, Convert.ToHexString(Payload(client.ReadMessage(ReadBound))));
        client.SendFramed(ProbeMessages.Run(SupportedQuery));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Pull(-1));
        Assert.Equal(ProbeMessages.RecordTag, Payload(client.ReadMessage(ReadBound))[1]);
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Goodbye());

        var document = new System.Text.StringBuilder();
        document.AppendLine("NC-02 missing RESET: without RESET the subsequent operation is " +
                            "answered with IGNORED and the session stays failed, so no valid " +
                            "recovery can be reported; recovery only follows the RESET itself.");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("connection ids: accepted=[" +
                            string.Join(",", trace.AcceptedConnectionIds) + "]");
        document.AppendLine("client local port: " + client.LocalEndPoint.Port + " (was " +
                            portBefore.Port + ")");
        document.AppendLine("RESET messages observed: " + trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("per-connection providers: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount}")));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("nc02-missing-reset.txt", document.ToString());

        Assert.Equal(1L, server.Diagnostics.ConnectionsAccepted);
        Assert.True(providers.Providers.Count == 1, "exactly one physical connection");
        Assert.Equal(2, providers.Providers[0].PrepareCount);
    }

    // ---- NC-03: an incorrect recovery response or state ----------------

    private sealed class DisposeThrowingProvider : IBoltOperationProvider
    {
        public int PrepareCount;

        public int DisposeFailures;

        private readonly ReferenceOperationProvider _inner = new();

        public IBoltOperation? Prepare(RunRequest run)
        {
            Interlocked.Increment(ref PrepareCount);
            var operation = _inner.Prepare(run);
            return operation is null ? null : new DisposeThrowingOperation(operation, this);
        }

        private sealed class DisposeThrowingOperation : IBoltOperation
        {
            private readonly IBoltOperation _inner;
            private readonly DisposeThrowingProvider _owner;

            public DisposeThrowingOperation(IBoltOperation inner, DisposeThrowingProvider owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public IReadOnlyList<string> Fields => _inner.Fields;

            public IReadOnlyList<PackStreamValue?>? ReadRow() => _inner.ReadRow();

            public void Dispose()
            {
                Interlocked.Increment(ref _owner.DisposeFailures);
                _inner.Dispose();
                throw new InvalidOperationException(
                    "probe fault: operation retirement failed during RESET (NC-03 mutation)");
            }
        }
    }

    [Fact]
    public async Task NC03_RecoveryFailureInjected_IsDetectedByTheResetOracle()
    {
        var fault = new DisposeThrowingProvider();
        var server = BoltTcpServer.Create(new BoltTcpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            CredentialVerifier = new ProbeVerifier(),
            OperationProviderFactory = () => fault,
            HandshakeOptions = BoltHandshakeOptions.Create(new[] { new BoltProtocolVersion(5, 8) }),
        });
        await using var _server = server;
        await server.StartAsync();
        var trace = new ProbeTrace(server);
        var endpoint = (IPEndPoint)server.BoundEndPoint!;

        using var client = new ProbeClient();
        await AuthorizeAsync(client, endpoint);

        // Establish an outstanding result so RESET has to retire it.
        client.SendFramed(ProbeMessages.Run(SupportedQuery));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);

        // The injected fault: the operation's Dispose throws, so the recovery attempt answers
        // the terminal 50N00 FAILURE instead of the expected B1 70 A0 SUCCESS.
        client.SendFramed(ProbeMessages.Reset());

        var recoveryResponse = Payload(client.ReadMessage(ReadBound));
        var recoveryHex = Convert.ToHexString(recoveryResponse);
        Assert.Equal(ProbeMessages.FailureTag, recoveryResponse[1]);
        Assert.Contains(ProbeMessages.AsciiHex("50N00"), recoveryHex, StringComparison.Ordinal);
        Assert.DoesNotContain(ResetResponseHex, recoveryHex, StringComparison.Ordinal);

        // The session is terminal: no further message is processed and the connection closes,
        // so no subsequent operation can succeed either.
        Assert.True(client.HasNoFurtherMessage(ReadBound),
            "no recovery response and no further operation on a terminal connection");
        await Poll.WaitAsync(() => server.Diagnostics.ConnectionsActive == 0
                               && server.Diagnostics.ConnectionsCompleted == 1,
            "NC-03 terminal close reported");

        var document = new System.Text.StringBuilder();
        document.AppendLine("NC-03 injected recovery failure: the provider's operation Dispose " +
                            "throws while RESET retires the outstanding result, so the recovery " +
                            "answers the terminal 50N00 FAILURE instead of the expected B1 70 A0. " +
                            "The RESET oracle detects it and no subsequent operation runs.");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("RESET response hex: " + recoveryHex);
        document.AppendLine("RESET messages observed server-side: " +
                            trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("provider prepares: " + fault.PrepareCount +
                            "; disposal failures: " + fault.DisposeFailures);
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("nc03-injected-recovery-failure.txt", document.ToString());

        Assert.Equal(1, fault.PrepareCount);
        Assert.Equal(1, fault.DisposeFailures);
        Assert.Equal(1L, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(1L, server.Diagnostics.ConnectionsCompleted);
    }

    // ---- NC-04: a terminal failure substituted for a recoverable one ----

    [Fact]
    public async Task NC04_TerminalFailureSubstituted_CannotSatisfyTheRecoveryProof()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers);
        await using var _server = server;
        var endpoint = (IPEndPoint)server.BoundEndPoint!;

        using var client = new ProbeClient();
        await AuthorizeAsync(client, endpoint);
        var portBefore = client.LocalEndPoint;

        // A terminal, non-recoverable failure: BEGIN is a valid Bolt 5.8 signature this bounded
        // server does not implement, and the refusal is terminal (08N06) with the connection
        // closed immediately.
        client.SendFramed(ProbeMessages.Begin());
        var terminal = Payload(client.ReadMessage(ReadBound));
        var terminalHex = Convert.ToHexString(terminal);
        Assert.Equal(ProbeMessages.FailureTag, terminal[1]);
        Assert.Contains(ProbeMessages.AsciiHex("08N06"), terminalHex, StringComparison.Ordinal);

        // The recovery question: RESET after a terminal failure must produce no recovery
        // response at all, because the session is Defunct and no further message is processed.
        client.SendToPossiblyClosedSocket(ProbeMessages.Reset());
        var afterReset = client.ReadMessage(ReadBound);
        Assert.Null(afterReset);

        var document = new System.Text.StringBuilder();
        document.AppendLine("NC-04 terminal failure substituted for a recoverable one: the " +
                            "inadmissible failure classification cannot satisfy the recovery " +
                            "proof. RESET after the terminal FAILURE produced no response; the " +
                            "expected B1 70 A0 never appears and no subsequent operation runs.");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("terminal failure hex: " + terminalHex);
        document.AppendLine("RESET messages observed: " + trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("client local port: " + client.LocalEndPoint.Port + " (was " +
                            portBefore.Port + ")");
        document.AppendLine("per-connection providers: " + string.Join(", ",
            providers.Providers.Select((p, i) => $"#{i + 1} prepare={p.PrepareCount}")));
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence("nc04-terminal-failure-substituted.txt", document.ToString());

        Assert.Equal(1L, server.Diagnostics.ConnectionsAccepted);
        await Poll.WaitAsync(() => server.Diagnostics.ConnectionsActive == 0 &&
            server.Diagnostics.ConnectionsCompleted == 1, "NC-04 terminal close reported");
        Assert.Equal(0, providers.Providers[0].PrepareCount);
    }
}