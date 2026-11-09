using System.Text;

using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>§19: negative controls — each control must actually detect the targeted invalid behavior.</summary>
internal static class ProbeNegative
{
    public static void Run()
    {
        Program.Section("M. Negative controls");
        N1_HandshakeVersionOracle();
        N2_TlsDowngradeRefusal();
        N3_ExternalPlaintextRefusal();
        N4_OversizedMessageRefusal();
        N5_PartialMessageNeverExecutes();
        N6_PipeliningOracle();
        N7_FailedDisposalPermantlyClosed();
        N8_CredentialSentinelScan();
    }

    private static void N1_HandshakeVersionOracle()
    {
        // Control: if the server selected an unsupported version, the reply would differ from
        // 00 00 08 05. The oracle detects any deviation.
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        var reply = client.HandshakeLegacy(0x00000805u);
        Program.Check(
            "N1 negotiation oracle detects wrong version selection",
            reply == "00000805",
            $"reply={reply} (any other value would be a detected defect)");
    }

    private static void N2_TlsDowngradeRefusal()
    {
        // Control: a TLS listener that accepted plaintext would answer the Bolt handshake here.
        var certificate = ProbeTls.MakeCertificate();
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            Tls = new BoltTlsServerOptions(certificate),
        });
        try
        {
            using var client = ReviewClient.Connect(server.EndPoint);
            client.SendRaw(new byte[] { 0x60, 0x60, 0xB0, 0x17, 0, 0, 8, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            var downgrade = client.WaitForClose(10_000) != "closed";
            Program.Check(
                "N2 downgrade control: plaintext must NOT receive a Bolt handshake reply",
                !downgrade,
                downgrade ? "DEFECT: TLS listener answered plaintext" : "no plaintext answer (refused)");
        }
        finally
        {
            server.Server.StopAsync().GetAwaiter().GetResult();
            server.Server.DisposeAsync().GetAwaiter().GetResult();
            certificate.Dispose();
        }
    }

    private static void N3_ExternalPlaintextRefusal()
    {
        // Control: if external plaintext binding were allowed, BoltTcpServer.Create would accept
        // IPAddress.Any. The oracle detects acceptance.
        var accepted = false;
        try
        {
            var server = BoltTcpServer.Create(new BoltTcpServerOptions
            {
                CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
                BindAddress = System.Net.IPAddress.Any,
            });
            accepted = true;
        }
        catch (InvalidOperationException)
        {
            // expected refusal
        }

        Program.Check(
            "N3 external-binding control: plaintext IPAddress.Any must be refused",
            !accepted,
            accepted ? "DEFECT: external plaintext bind accepted" : "refused at construction");
    }

    private static void N4_OversizedMessageRefusal()
    {
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            FramingLimits = new BoltSharp.Framing.BoltFramingLimits(maxMessageSize: 100),
        });
        
        using var client = Authorize(server);

        // If the oversized message EXECUTED, the server would answer RUN SUCCESS — the oracle
        // detects that as the targeted defect.
        client.SendFramed(Framing.Split(Review.Run("RETURN 42 AS answer" + new string(' ', 90)), 20));
        var executed = false;
        try
        {
            var message = client.ReadMessage();
            executed = message is not null;
        }
        catch (Exception)
        {
            executed = false;
        }

        Program.Check(
            "N4 oversize control: oversized message must not execute",
            !executed,
            executed ? "DEFECT: oversized request executed" : "refused (connection closed)");
    }

    private static void N5_PartialMessageNeverExecutes()
    {
        var opened = 0;
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            OperationProviderFactory = () => new CountingNoopProvider(() => Interlocked.Increment(ref opened)),
        });
        
        using var client = Authorize(server);
        var framed = Framing.Frame(Review.Run("RETURN 42 AS answer"));
        client.SendRaw(framed[..^1]); // truncate the final terminator byte, then EOF
        client.Dispose();

        Program.Check(
            "N5 truncation control: partial message must not execute",
            Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)) && opened == 0,
            $"prepareCount={opened}");
    }

    private sealed class CountingNoopProvider : IBoltOperationProvider
    {
        private readonly Action _onPrepare;
        public CountingNoopProvider(Action onPrepare) => _onPrepare = onPrepare;

        public IBoltOperation? Prepare(RunRequest run)
        {
            _onPrepare();
            return run.Query == "RETURN 42 AS answer" ? new NoopOperation() : null;
        }
    }

    private sealed class NoopOperation : IBoltOperation
    {
        private int _noopRows;
        public IReadOnlyList<string> Fields { get; } = new[] { "answer" };
        public IReadOnlyList<PackStreamValue?>? ReadRow() =>
            _noopRows++ == 0 ? new PackStreamValue?[] { new PackStreamInteger(42) } : null;
        public void Dispose() { }
    }

    private static void N6_PipeliningOracle()
    {
        // Control: if pipelined messages executed out of order, the RUN would be answered before
        // the HELLO — the oracle detects any sequence deviation.
        using var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        using var client = ReviewClient.Connect(server.EndPoint);
        client.HandshakeLegacy(0x00000805u);
        var oneWrite = Framing.Frame(Review.Hello())
            .Concat(Framing.Frame(Review.Logon()))
            .Concat(Framing.Frame(Review.Run("RETURN 42 AS answer")))
            .ToArray();
        client.SendRaw(oneWrite);

        var m1 = client.ReadMessageOrThrow();
        var m2 = client.ReadMessageOrThrow();
        var m3 = client.ReadMessageOrThrow();
        Program.Check(
            "N6 ordering oracle: HELLO SUCCESS, LOGON SUCCESS, RUN SUCCESS in exact order",
            (m1[1], m2[1], m3[1]) == (0x70, 0x70, 0x70),
            $"{Program.Hex(m1.AsSpan(0, 2))} {Program.Hex(m2.AsSpan(0, 2))} {Program.Hex(m3.AsSpan(0, 2))}");
        client.Send(Review.Goodbye);
    }

    private static void N7_FailedDisposalPermantlyClosed()
    {
        // Control: if a failed disposal permitted another RUN, the provider-open counter would
        // exceed 1 — the oracle detects it.
        var prepares = 0;
        var disposes = 0;
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            OperationProviderFactory = () => new FailOnceProvider(() => Interlocked.Increment(ref prepares), () => Interlocked.Increment(ref disposes)),
        });
        
        using var client = Authorize(server, "RETURN 42 AS answer");
        client.Send(Review.Pull(-1));
        client.ReadMessageOrThrow();
        client.ReadMessageOrThrow(); // FAILURE for the failed retirement
        client.WaitForClose(10_000);

        try
        {
            client.Send(Review.Run("RETURN 42 AS answer"));
        }
        catch (Exception)
        {
            // closed socket is fine
        }

        Program.Check(
            "N7 reuse control: failed disposal must not permit another operation",
            prepares == 1 && disposes == 1,
            $"prepares={prepares} disposes={disposes}");
    }

    private sealed class FailOnceProvider : IBoltOperationProvider
    {
        private readonly Action _onPrepare;
        private readonly Action _onDispose;
        public FailOnceProvider(Action onPrepare, Action onDispose) { _onPrepare = onPrepare; _onDispose = onDispose; }
        public IBoltOperation? Prepare(RunRequest run)
        {
            _onPrepare();
            return run.Query == "RETURN 42 AS answer" ? new FaultyOperation(_onDispose) : null;
        }
    }

    private sealed class FaultyOperation : IBoltOperation
    {
        private readonly Action _onDispose;
        public FaultyOperation(Action onDispose) => _onDispose = onDispose;
        private int _rows;
        public IReadOnlyList<string> Fields { get; } = new[] { "answer" };
        public IReadOnlyList<PackStreamValue?>? ReadRow() =>
            _rows++ == 0 ? new PackStreamValue?[] { new PackStreamInteger(42) } : null;
        public void Dispose()
        {
            _onDispose();
            throw new InvalidOperationException("reviewer disposal fault");
        }
    }

    private static void N8_CredentialSentinelScan()
    {
        // Control: if credentials leaked into diagnostics, the sentinel would appear in the
        // observed event stream — the oracle detects it.
        var observed = new StringBuilder();
        var server = Program.StartServerWith(() => new BoltTcpServerOptions
        {
            CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
        });
        server.Server.Diagnostics.Event += e => observed.Append(e.Detail).Append(';');
        
        using var client = ReviewClient.Connect(server.EndPoint);
        client.HandshakeLegacy(0x00000805u);
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Logon("sentinel-user", "sentinel-pass"));
        client.ReadMessageOrThrow();
        client.Send(Review.Run("RETURN 42 AS answer"));
        client.ReadMessageOrThrow();

        var text = observed.ToString();
        Program.Check(
            "N8 sentinel scan: credentials must not appear in diagnostics",
            !text.Contains("sentinel-user") && !text.Contains("sentinel-pass"),
            $"diagnostic events: {text.Length} chars scanned");
        client.Send(Review.Goodbye);
    }

    private static ReviewClient Authorize(Program.ProbeServer server, string? runQuery = null)
    {
        var client = ReviewClient.Connect(server.EndPoint);
        var reply = client.HandshakeLegacy(0x00000805u);
        if (reply != "00000805") { throw new InvalidOperationException("handshake failed"); }
        client.Send(Review.Hello());
        client.ReadMessageOrThrow();
        client.Send(Review.Logon());
        client.ReadMessageOrThrow();
        if (runQuery is not null)
        {
            client.Send(Review.Run(runQuery));
            client.ReadMessageOrThrow();
        }

        return client;
    }
}
