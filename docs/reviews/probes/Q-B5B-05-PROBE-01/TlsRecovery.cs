// Q-B5B-05-PROBE-01 — the same RESET-recovery demonstration over the accepted TLS loopback
// arrangement.
//
// The probe connects in plaintext, performs the Bolt 5.8 handshake, and then upgrades the same
// physical socket to TLS with the client-side SslStream before HELLO (there is no STARTTLS and
// no plaintext/TLS detection: the mode is listener configuration, and the server listener is
// configured for TLS). Every subsequent protocol byte, including the recoverable FAILURE, the
// RESET and the subsequent successful operation, is encrypted on that one socket.
//
// Because a passive byte tap cannot observe inside TLS, this probe relies on server-side
// connection identity and counters (the same evidence line the driver TLS scenarios rely on),
// which is sufficient for the physical-connection gate: the production listener mints exactly
// one connection id per accepted socket and records it on every diagnostics event.
using System.Net;

using BoltSharp.Q5B05.Probe.Infra;
using BoltSharp.Transport;

using Xunit;

namespace BoltSharp.Q5B05.Probe;

public sealed class TlsRecovery
{
    private const string User = "probe-owner-user";
    private const string Secret = "probe-owner-secret";
    private const string UnsupportedQuery = "MATCH (n) DETACH DELETE n";
    private const string SupportedQuery = "RETURN 42 AS answer";
    private const string ResetResponseHex = "B170A0";
    private const string IgnoredResponseHex = "B07E";
    private const string Log = "p04-tls-reset-recovery.txt";
    private const string Label = "P-04 TLS recoverable-failure RESET recovery";

    private static readonly TimeSpan ReadBound = TimeSpan.FromSeconds(20);

    private static byte[] Payload(byte[]? message) =>
        message ?? throw new InvalidOperationException("the server closed the connection");

    [Fact]
    public async Task P04_Tls_RecoverableFailureResetAndSubsequentOperation_SameConnection()
    {
        var providers = new ProbeProviderLog();
        var (server, trace) = await ProbeHost.StartServerAsync(providers, tls: true);
        await using var _server = server;

        using var client = new ProbeClient();
        const string Phase = "during-sequence";
        await client.ConnectAsync((IPEndPoint)server.BoundEndPoint!);
        var portBefore = client.LocalEndPoint;

        // The listener is configured for TLS, so the TLS authentication happens before any Bolt
        // byte: the same physical socket is upgraded first and then carries the Bolt handshake.
        await client.TlsUpgradeAsync("localhost");
        Assert.Equal("00000805", await client.HandshakeAsync());

        client.SendFramed(ProbeMessages.Hello("Q-B5B-05-PROBE-01/1.0", "BoltSharp-Probe/1.0"));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Logon("basic", User, Secret));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);

        // A valid initial operation.
        client.SendFramed(ProbeMessages.Run(SupportedQuery));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Pull(-1));
        Assert.Equal(ProbeMessages.RecordTag, Payload(client.ReadMessage(ReadBound))[1]);
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);

        // The recoverable FAILURE and the failed session state.
        client.SendFramed(ProbeMessages.Run(UnsupportedQuery));
        var failure = Payload(client.ReadMessage(ReadBound));
        Assert.Equal(ProbeMessages.FailureTag, failure[1]);
        Assert.Contains(ProbeMessages.AsciiHex("50N42"), Convert.ToHexString(failure),
            StringComparison.Ordinal);
        client.SendFramed(ProbeMessages.Pull(1000));
        Assert.Equal(IgnoredResponseHex, Convert.ToHexString(Payload(client.ReadMessage(ReadBound))));

        // RESET and its successful response.
        client.SendFramed(ProbeMessages.Reset());
        Assert.Equal(ResetResponseHex, Convert.ToHexString(Payload(client.ReadMessage(ReadBound))));

        // A subsequent valid operation on the same connection.
        client.SendFramed(ProbeMessages.Run(SupportedQuery));
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);
        client.SendFramed(ProbeMessages.Pull(-1));
        Assert.Equal(ProbeMessages.RecordTag, Payload(client.ReadMessage(ReadBound))[1]);
        Assert.Equal(ProbeMessages.SuccessTag, Payload(client.ReadMessage(ReadBound))[1]);

        var duringSequence = new SameConnectionVerdict
        {
            ClientLocalEndPoint = portBefore,
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
            ClientLocalPortUnchanged = portBefore.Port == client.LocalEndPoint.Port,
            CapturedBeforeOrderlyCompletion = true,
            VerdictPhase = Phase,
            ProxyMessageCount = 0,
        };

        Assert.True(duringSequence.SamePhysicalConnection,
            $"{Label}: every line of evidence places the failure, the RESET and the subsequent " +
            $"successful operation on one physical connection. {duringSequence.Summary}");
        ProbeHost.AppendEvidence(Log, $"{Label} [{duringSequence.VerdictPhase}]: " +
                                    $"{duringSequence.Summary}\n");

        // An orderly lifecycle completion.
        client.SendFramed(ProbeMessages.Goodbye());
        Assert.True(client.HasNoFurtherMessage(ReadBound), "GOODBYE produced no further message");

        var document = new System.Text.StringBuilder();
        document.AppendLine("P-04 TLS: recoverable FAILURE -> IGNORED -> RESET -> successful " +
                            "operation -> orderly close, all on one physical TLS connection");
        document.AppendLine("server: " + ProbeHost.Summary(server));
        document.AppendLine("TLS handshake failures: " + server.Diagnostics.TlsHandshakeFailures);
        document.AppendLine("connection ids: accepted=[" +
                            string.Join(",", trace.AcceptedConnectionIds) + "] messageProcessed=[" +
                            string.Join(",", trace.MessageProcessedConnectionIds) + "]");
        document.AppendLine("RESET messages observed server-side: " +
                            trace.CountProcessedSignatures("0x0F"));
        document.AppendLine("client local port: " + client.LocalEndPoint.Port + " (unchanged from " +
                            portBefore.Port + ")");
        document.AppendLine("trace:");
        document.AppendLine(trace.Render());
        ProbeHost.WriteEvidence(Log, document.ToString());

        Assert.Equal(1L, server.Diagnostics.ConnectionsAccepted);
        Assert.Equal(1L, server.Diagnostics.ConnectionsCompleted);
        Assert.Equal(0L, server.Diagnostics.ConnectionsFailed);
        Assert.Equal(0L, server.Diagnostics.TlsHandshakeFailures);
        Assert.Equal(1L, trace.CountProcessedSignatures("0x0F"));
        Assert.Single(providers.Providers);
        Assert.Equal(2, providers.Single?.SuccessCount);
        Assert.Equal(1, providers.Single?.RefusalCount);
    }
}