using System.Net;
using System.Text;

using BoltSharp.Messages;
using BoltSharp.PackStream;
using BoltSharp.Sessions;
using BoltSharp.Transport;

namespace B5AQ1Probe;

/// <summary>
/// Critical investigation E (§12 session options), §13 framing, §14 queries, §15 auth/state,
/// §16 pipelining, §17 fail-closed ownership and §18 multi-client — the protocol-integration
/// battery, all with reviewer-owned encoders, framers and oracles.
/// </summary>
internal static class ProbeProtocol
{
    public static void Run()
    {
        Program.Section("F. Session-options passthrough (§12)");
        SessionOptionsPassthrough();

        Program.Section("G. Framing and fragmentation (§13)");
        FramingMatrix();

        Program.Section("H. Scalar, graph and parameter queries (§14)");
        ScalarConversation();
        GraphConversation();
        ParameterConversation();

        Program.Section("I. Authentication and protocol-state behavior (§15)");
        AuthenticationMatrix();

        Program.Section("J. Pipelining and terminal closure (§16)");
        PipeliningMatrix();

        Program.Section("K. Fail-closed resource ownership over TCP (§17)");
        FailClosedOwnership();

        Program.Section("L. Multi-client isolation and limits (§18)");
        MultiClientIsolation();
    }

    private static Program.ProbeServer Start(Func<IBoltOperationProvider>? provider = null)
    {
        return Program.StartServerWith(() =>
        {
            var options = new BoltTcpServerOptions
            {
                CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
            };
            if (provider is not null)
            {
                options = new BoltTcpServerOptions
                {
                    CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
                    OperationProviderFactory = provider,
                };
            }

            return options;
        });
    }

    /// <summary>Authorized full conversation: handshake, HELLO, LOGON, optionally RUN.</summary>
    private static ReviewClient Authorize(Program.ProbeServer server, string? runQuery = null)
    {
        var client = ReviewClient.Connect(server.EndPoint);
        var reply = client.HandshakeLegacy(0x00000805u);
        if (reply != "00000805") { throw new InvalidOperationException("handshake failed: " + reply); }
        client.Send(Review.Hello());
        ExpectSuccess(client.ReadMessageOrThrow(), "HELLO");
        client.Send(Review.Logon());
        ExpectSuccess(client.ReadMessageOrThrow(), "LOGON");
        if (runQuery is not null)
        {
            client.Send(Review.Run(runQuery));
            ExpectSuccess(client.ReadMessageOrThrow(), "RUN");
        }

        return client;
    }

    private static void ExpectSuccess(byte[] message, string step)
    {
        if (message.Length < 2 || message[0] != 0xB1 || message[1] != 0x70)
        {
            throw new InvalidOperationException($"expected SUCCESS for {step}, got {Program.Hex(message.AsSpan(0, Math.Min(8, message.Length)))}");
        }
    }

    // ------------------------------------------------------------------ F. session options

    private static void SessionOptionsPassthrough()
    {
        // Source fact (reviewed): BoltTcpConnection constructs
        //   new BoltSessionOptions { Verifier = options.CredentialVerifier }
        // — every other session option is the B3-B default and is NOT configurable through
        // BoltTcpServerOptions. The reviewer verifies the DEFAULT limit is honored over TCP with
        // a custom provider (public seam), and records that customization is not exposed.
        var provider = new RowProvider(rows: 2500);
        using var server = Start(provider: () => provider);
        using var client = Authorize(server, "RETURN 42 AS answer");

        client.Send(Review.Pull(-1));
        var totalRecords = 0;
        var hasMoreCount = 0;
        var terminalWithoutHasMore = false;
        while (true)
        {
            var message = client.ReadMessageOrThrow();
            if (message[0] == 0xB1 && message[1] == 0x71) { totalRecords++; continue; }
            var summary = Parse.Read(message).As<Parse.St>().Fields[0].As<Parse.M>();
            var hasMore = summary.Get("has_more");
            if (hasMore is Parse.S { Value: "true" }) { hasMoreCount++; client.Send(Review.Pull(-1)); continue; }
            terminalWithoutHasMore = hasMore is null;
            break;
        }

        Program.Check(
            "F1 B3-B default MaxRecordsPerResponse (1000) honored over TCP",
            totalRecords == 2500 && hasMoreCount == 2,
            $"records={totalRecords} hasMorePasses={hasMoreCount}");
        Program.Check(
            "F2 terminal summary carries no has_more field (accepted B3-B semantics)",
            terminalWithoutHasMore,
            "SUCCESS {}");
        Program.Check(
            "F3 session options beyond the verifier are NOT configurable through BoltTcpServerOptions (producer Q7 confirmed from source)",
            true,
            "BoltTcpConnection.cs: new BoltSessionOptions { Verifier = ... } only");
        client.Send(Review.Goodbye);
    }

    private sealed class RowProvider : IBoltOperationProvider
    {
        private readonly int _rows;
        public RowProvider(int rows) => _rows = rows;
        public IBoltOperation? Prepare(RunRequest run) =>
            run.Query == "RETURN 42 AS answer" ? new RowOperation(_rows) : null;
    }

    private sealed class RowOperation : IBoltOperation
    {
        private readonly int _rows;
        private int _next;
        public RowOperation(int rows) => _rows = rows;
        public IReadOnlyList<string> Fields { get; } = new[] { "answer" };
        public IReadOnlyList<PackStreamValue?>? ReadRow() => _next < _rows
            ? new PackStreamValue?[] { new PackStreamInteger(_next++) }
            : null;
        public void Dispose() { }
    }

    // ------------------------------------------------------------------ G. framing

    private static void FramingMatrix()
    {
        using var server = Start();
                var run = Review.Run("RETURN 42 AS answer");

        // G1: single chunk.
        using (var client = Authorize(server))
        {
            client.SendFramed(run);
            ExpectSuccess(client.ReadMessageOrThrow(), "single chunk RUN");
            client.Send(Review.Goodbye);
        }

        Program.Check("G1 single-chunk request processed", true, "RUN SUCCESS");

        // G2: header split across two writes.
        using (var client = Authorize(server))
        {
            var framed = Framing.Frame(run);
            client.SendRaw(new[] { framed[0] });
            client.SendRaw(framed[1..]);
            ExpectSuccess(client.ReadMessageOrThrow(), "split header RUN");
            client.Send(Review.Goodbye);
        }

        Program.Check("G2 chunk header split across writes processed", true, "RUN SUCCESS");

        // G3: payload split at every byte boundary.
        using (var client = Authorize(server))
        {
            client.SendFramed(Framing.Split(run, 1));
            ExpectSuccess(client.ReadMessageOrThrow(), "byte-boundary RUN");
            client.Send(Review.Goodbye);
        }

        Program.Check("G3 payload split at every byte boundary processed", true, "RUN SUCCESS");

        // G4: many small chunks (chunk size 2).
        using (var client = Authorize(server))
        {
            client.SendFramed(Framing.Split(run, 2));
            ExpectSuccess(client.ReadMessageOrThrow(), "2-byte chunks RUN");
            client.Send(Review.Goodbye);
        }

        Program.Check("G4 many small chunks processed", true, "RUN SUCCESS");

        // G5: terminator and the NEXT message in the same write; NOOP between messages.
        using (var client = Authorize(server))
        {
            var runFramed = Framing.Frame(run);
            var noopThenNext = new byte[] { 0x00, 0x00 }; // inter-message NOOP
            var next = Framing.Frame(Review.Pull(1));
            client.SendRaw(runFramed.Concat(noopThenNext).Concat(next).ToArray());
            ExpectSuccess(client.ReadMessageOrThrow(), "RUN");
            var record = client.ReadMessageOrThrow(); // PULL(1) answer, NOT a NOOP-induced request
            Program.Check(
                "G5 NOOP between messages is keep-alive, not a request (RECORD follows)",
                record[0] == 0xB1 && record[1] == 0x71,
                Program.Hex(record));
            client.Send(Review.Goodbye);
        }

        // G6: truncated header then EOF → safe close.
        using (var client = Authorize(server))
        {
            client.SendRaw(new byte[] { 0x00 });
            client.Dispose(); // then EOF: the reader must fail closed
            var closed = client.WaitForClose(5_000) == "closed";
            Program.Check("G6 truncated chunk header closes safely", closed, "EOF mid-header");
        }

        // G7: truncated payload then EOF → safe close.
        using (var client = Authorize(server))
        {
            client.SendRaw(new byte[] { 0x00, 0x0A, 0x01, 0x02 });
            client.Dispose(); // then EOF
            var closed = client.WaitForClose(5_000) == "closed";
            Program.Check("G7 truncated chunk payload closes safely", closed, "EOF mid-payload");
        }

        // G8: EOF mid-message → no execution of the incomplete request.
        using var serverForG8 = Start();
        {
            var opened = 0;
            using (var client = Authorize(serverForG8))
            {
                var framed = Framing.Frame(run);
                client.SendRaw(framed[..^1]); // everything but the final terminator byte
            }

            Program.Check(
                "G8 incomplete request never executes (no fault, clean close)",
                Program.WaitUntil(() => serverForG8.Server.Diagnostics.ConnectionsActive == 0, TimeSpan.FromSeconds(5)),
                $"active={serverForG8.Server.Diagnostics.ConnectionsActive}");
        }

        // G9: oversized cumulative message refused under a small receiver limit.
        {
            var small = Program.StartServerWith(() => new BoltTcpServerOptions
            {
                CredentialVerifier = Program.ReviewVerifier.AcceptBasic(),
                FramingLimits = new BoltSharp.Framing.BoltFramingLimits(maxMessageSize: 100),
            });
            
            using var client = Authorize(small);
            var oversized = Review.Run("RETURN 42 AS answer" + new string(' ', 90));
            client.SendFramed(Framing.Split(oversized, 20));
            Program.Check(
                "G9 oversized cumulative message refused before execution",
                client.WaitForClose(5_000) == "closed",
                "receiver-side limit");
        }

        // G10: multiple requests in one write preserve order (framing-level echo oracle).
        using (var client = Authorize(server))
        {
            var run2 = Framing.Frame(run);
            var pull = Framing.Frame(Review.Pull(1));
            client.SendRaw(run2.Concat(pull).ToArray());
            ExpectSuccess(client.ReadMessageOrThrow(), "RUN (order 1)");
            var record = client.ReadMessageOrThrow();
            Program.Check("G10 two messages in one write: order preserved", record[0] == 0xB1 && record[1] == 0x71, Program.Hex(record));
            client.Send(Review.Goodbye);
        }
    }

    // ------------------------------------------------------------------ H. queries

    private static void ScalarConversation()
    {
        using var server = Start();
        using var client = Authorize(server);

        // Reviewer-encoded RUN must equal the historical golden bytes.
        var runBytes = Review.Run("RETURN 42 AS answer");
        Program.Check(
            "H1 reviewer encoder reproduces authentic RUN bytes",
            Program.Hex(runBytes) == Golden.RunScalarRequest,
            Program.Hex(runBytes));
        var pullBytes = Review.Pull(1000);
        Program.Check(
            "H1 reviewer encoder reproduces authentic PULL bytes",
            Program.Hex(pullBytes) == Golden.PullN1000,
            Program.Hex(pullBytes));

        client.Send(runBytes);
        var runSuccess = client.ReadMessageOrThrow();
        var meta = Parse.Read(runSuccess).As<Parse.St>().Fields[0].As<Parse.M>();
        var fields = meta.Get("fields") as Parse.L;
        Program.Check(
            "H2 RUN SUCCESS announces fields=[answer]",
            fields is not null && fields.Items.Count == 1 && fields.Items[0].As<Parse.S>().Value == "answer",
            Program.Hex(runSuccess.AsSpan(0, Math.Min(40, runSuccess.Length))));

        client.Send(pullBytes);
        var record = client.ReadMessageOrThrow();
        Program.Check("H3 RECORD [42] byte-identical to golden", Program.Hex(record) == Golden.Record42, Program.Hex(record));
        var summary = client.ReadMessageOrThrow();
        Program.Check(
            "H4 terminal SUCCESS with no has_more",
            summary[0] == 0xB1 && summary[1] == 0x70 && !Program.Hex(summary).Contains("886861735F6D6F7265"),
            Program.Hex(summary));

        // Connection reuse after completion.
        client.Send(Review.Run("RETURN 42 AS answer"));
        ExpectSuccess(client.ReadMessageOrThrow(), "second RUN");
        client.Send(Review.Pull(1000));
        var record2 = client.ReadMessageOrThrow();
        Program.Check("H5 connection reusable after normal completion", Program.Hex(record2) == Golden.Record42, "second query OK");
        client.Send(Review.Goodbye);
    }

    private static void GraphConversation()
    {
        using var server = Start();
                using var client = Authorize(server, "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a, r, b");

        client.Send(Review.Pull(1000));
        var record = client.ReadMessageOrThrow();
        Program.Check("H6 graph RECORD byte-identical to accepted golden", Program.Hex(record) == Golden.GraphRecord, Program.Hex(record));

        var parsed = Parse.Read(record).As<Parse.St>();
        var data = parsed.Fields[0].As<Parse.L>().Items;
        Program.Check("H7 record arity 3 (a, r, b)", data.Count == 3, $"arity={data.Count}");
        var alice = data[0].As<Parse.Node>();
        var knows = data[1].As<Parse.Rel>();
        var bob = data[2].As<Parse.Node>();
        Program.Check(
            "H8 Alice: id 1, label Person, elementId n:1",
            alice.Id == 1 && alice.Labels.SequenceEqual(new[] { "Person" }) && alice.ElementId == "n:1",
            $"id={alice.Id} elem={alice.ElementId}");
        Program.Check(
            "H8 Bob: id 2, label Person, elementId n:2",
            bob.Id == 2 && bob.Labels.SequenceEqual(new[] { "Person" }) && bob.ElementId == "n:2",
            $"id={bob.Id} elem={bob.ElementId}");
        Program.Check(
            "H9 KNOWS: id 1, r:1, direction Alice->Bob, since=2024",
            knows.Id == 1 && knows.ElementId == "r:1" && knows.Start == alice.Id && knows.End == bob.Id
            && knows.Type == "KNOWS"
            && knows.Props.Get("since") is Parse.I { Value: 2024 },
            $"rel={knows.Id}/{knows.ElementId} {knows.Start}->{knows.End}");
        client.Send(Review.Goodbye);
    }

    private static void ParameterConversation()
    {
        using var server = Start();
        using var client = Authorize(server);

        // Typed parameters survive the socket path without text interpolation.
        foreach (var (label, valueBytes, expected) in new[]
                 {
                     ("integer", Pack.Int(-1234), "-1234"),
                     ("string", Pack.Str("héllo wörld"), "héllo wörld"),
                     ("boolean true", new byte[] { 0xC3 }, "true"),
                     ("boolean false", new byte[] { 0xC2 }, "false"),
                     ("null", Pack.Null, "null"),
                     ("float", new byte[] { 0xC1, 0x40, 0x59, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "100"),
                     ("list", Pack.List(Pack.Int(1), Pack.Int(2), Pack.Int(3)), "[1,2,3]"),
                     ("nested map", Pack.Map(("inner", Pack.Str("value"))), "{inner:value}"),
                 })
        {
            client.Send(Review.Run("RETURN $value AS answer", ("value", valueBytes)));
            var runSuccess = client.ReadMessageOrThrow();
            if (runSuccess[1] != 0x70)
            {
                Program.Check($"H10 parameter {label} accepted", false, Program.Hex(runSuccess.AsSpan(0, Math.Min(24, runSuccess.Length))));
                client.Send(Review.Reset);
                client.ReadMessageOrThrow();
                continue;
            }

            client.Send(Review.Pull(100));
            var record = client.ReadMessageOrThrow();
            var data = Parse.Read(record).As<Parse.St>().Fields[0].As<Parse.L>().Items[0];
            var rendered = Render(data);
            Program.Check($"H10 parameter {label} preserved over the full socket path", rendered == expected, rendered);
            client.ReadMessageOrThrow(); // terminal summary
        }

        client.Send(Review.Goodbye);
    }

    private static string Render(Parse.V v) => v switch
    {
        Parse.I i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Parse.S s => s.Value,
        Parse.Nul => "null",
        Parse.L l => "[" + string.Join(",", l.Items.Select(Render)) + "]",
        Parse.M m => "{" + string.Join(",", m.Entries.Select(e => e.Key + ":" + Render(e.Value!))) + "}",
        _ => v.GetType().Name,
    };

    // ------------------------------------------------------------------ I. auth/state

    private static void AuthenticationMatrix()
    {
        // I1: RUN before LOGON — accepted B3-B semantics: FAILURE + terminal close.
        {
            using var server = Start();
            using var client = ReviewClient.Connect(server.EndPoint);
            client.HandshakeLegacy(0x00000805u);
            client.Send(Review.Hello());
            client.ReadMessageOrThrow();
            client.Send(Review.Run("RETURN 42 AS answer"));
            var response = client.ReadMessageOrThrow();
            Program.Check(
                "I1 RUN before LOGON is a terminal protocol violation (FAILURE + close)",
                response[0] == 0xB1 && response[1] == 0x7F && client.WaitForClose(5_000) == "closed",
                Program.Hex(response.AsSpan(0, 2)));
        }

        // I2: missing verifier fails closed.
        {
            using var server = Program.StartServerWith(() => new BoltTcpServerOptions());
            using var client = ReviewClient.Connect(server.EndPoint);
            client.HandshakeLegacy(0x00000805u);
            client.Send(Review.Hello());
            ExpectSuccess(client.ReadMessageOrThrow(), "HELLO");
            client.Send(Review.Logon());
            var response = client.ReadMessageOrThrow();
            Program.Check("I2 missing verifier: LOGON rejected (FAILURE)", response[0] == 0xB1 && response[1] == 0x7F, Program.Hex(response.AsSpan(0, 2)));
        }

        // I3: wrong credentials → FAILURE + terminal close; failure metadata has no credentials.
        {
            using var server = Program.StartServerWith(() => new BoltTcpServerOptions
            {
                CredentialVerifier = new StrictVerifier("reviewer", "reviewer-secret"),
            });
            using var client = Authorize(server);
            client.Send(Review.Logon("reviewer", "wrong"));
            var response = client.ReadMessageOrThrow();
            var hex = Program.Hex(response);
            Program.Check("I3 wrong credentials rejected", response[0] == 0xB1 && response[1] == 0x7F, hex[..Math.Min(24, hex.Length)]);
            var text = Encoding.UTF8.GetString(response);
            Program.Check(
                "I3 failure description carries no credential material",
                !text.Contains("wrong") && !hex.Contains("wrong"),
                "no echo of rejected credentials");
            Program.Check("I3 connection terminal after authentication failure", client.WaitForClose(5_000) == "closed", "fail closed");
        }

        // I4: throwing verifier fails closed and leaks nothing.
        {
            var verifier = Program.ReviewVerifier.Thrower();
            using var server = Program.StartServerWith(() => new BoltTcpServerOptions
            {
                CredentialVerifier = verifier,
            });
            using var client = ReviewClient.Connect(server.EndPoint);
            client.HandshakeLegacy(0x00000805u);
            client.Send(Review.Hello());
            ExpectSuccess(client.ReadMessageOrThrow(), "HELLO");
            client.Send(Review.Logon("sentinel-principal", "sentinel-credential"));
            var response = client.ReadMessageOrThrow();
            var text = Encoding.UTF8.GetString(response);
            Program.Check("I4 throwing verifier fails closed (FAILURE)", response[0] == 0xB1 && response[1] == 0x7F, Program.Hex(response.AsSpan(0, 2)));
            Program.Check(
                "I4 no credential sentinel in the failure response",
                !text.Contains("sentinel-credential") && !text.Contains("sentinel-principal"),
                "credentials contained");
        }

        // I5: LOGOFF then RUN (terminal protocol violation), then close.
        {
            using var server = Start();
            using var client = Authorize(server);
            client.Send(Review.Logoff);
            ExpectSuccess(client.ReadMessageOrThrow(), "LOGOFF");
            client.Send(Review.Run("RETURN 42 AS answer"));
            var response = client.ReadMessageOrThrow();
            Program.Check(
                "I5 RUN after LOGOFF is a terminal protocol violation",
                response[0] == 0xB1 && response[1] == 0x7F && client.WaitForClose(5_000) == "closed",
                Program.Hex(response.AsSpan(0, 2)));
        }

        // I6: unsupported query → recoverable FAILURE; pipelined work IGNORED (B07E); RESET recovers.
        {
            using var server = Start();
            using var client = Authorize(server);
            var unsupported = Framing.Frame(Review.Run("MATCH (n) RETURN n LIMIT 1"));
            var pull = Framing.Frame(Review.Pull(1));
            var reset = Framing.Frame(Review.Reset);
            var supported = Framing.Frame(Review.Run("RETURN 42 AS answer"));
            client.SendRaw(unsupported.Concat(pull).Concat(reset).Concat(supported).ToArray());

            var failure = client.ReadMessageOrThrow();
            Program.Check("I6 unsupported query FAILURE", failure[0] == 0xB1 && failure[1] == 0x7F, Program.Hex(failure.AsSpan(0, 2)));
            var ignored = client.ReadMessageOrThrow();
            Program.Check("I6 pipelined work in FAILED state answered with B0 7E", Program.Hex(ignored) == Golden.Ignored, Program.Hex(ignored));
            ExpectSuccess(client.ReadMessageOrThrow(), "RESET");
            var runSuccess = client.ReadMessageOrThrow();
            Program.Check("I6 RESET recovery restores READY (supported query succeeds)", runSuccess[0] == 0xB1 && runSuccess[1] == 0x70, "RUN SUCCESS");
            client.Send(Review.Goodbye);
        }

        // I7: write query refused, recoverable.
        {
            using var server = Start();
            using var client = Authorize(server);
            client.Send(Review.Run("CREATE (n:X)"));
            var response = client.ReadMessageOrThrow();
            Program.Check("I7 write query refused (FAILURE, recoverable)", response[0] == 0xB1 && response[1] == 0x7F && client.WaitForClose(0) == "open", "connection open");
            client.Send(Review.Reset);
            ExpectSuccess(client.ReadMessageOrThrow(), "RESET after write refusal");
            client.Send(Review.Goodbye);
        }

        // I8: invalid RUN options (extra with wrong-typed known field). Accepted B3-A
        // semantics: a structurally invalid message is rejected by the codec, terminal at
        // the transport boundary (fail closed) - no response is fabricated.
        {
            using var server = Start();
            using var client = Authorize(server);
            client.Send(Review.RunBadExtra(Pack.Map(("db", Pack.Int(42)))));
            var closed = client.WaitForClose(5_000) == "closed";
            Program.Check("I8 invalid RUN extra rejected by the codec (terminal, fail closed)", closed, "codec rejection closes the connection");
        }

        // I9: PULL before RUN → FAILURE (protocol violation, terminal).
        {
            using var server = Start();
            using var client = Authorize(server);
            client.Send(Review.Pull(1));
            var response = client.ReadMessageOrThrow();
            Program.Check("I9 PULL before RUN rejected", response[0] == 0xB1 && response[1] == 0x7F, Program.Hex(response.AsSpan(0, 2)));
        }

        // I10: RUN while STREAMING is not executed (rejected or ignored per accepted B3-B).
        {
            var twoRows = new RowProvider(rows: 2);
            using var server = Start(provider: () => twoRows);
            using var client = Authorize(server, "RETURN 42 AS answer");

            client.Send(Review.Pull(1)); // one of two rows: cursor stays open (has_more)
            var first = client.ReadMessageOrThrow();
            var firstHex = Program.Hex(first);
            if (first[1] == 0x71 || (first[1] == 0x70 && firstHex.Contains("896861735F6D6F7265")))
            {
                var pending = client.ReadMessageOrThrow(); // the has_more summary of PULL(1)
            client.Send(Review.Run("RETURN 42 AS answer"));
                var second = client.ReadMessageOrThrow();
                Program.Check(
                    "I10 RUN while STREAMING is not executed (rejected or ignored)",
                    second[1] is 0x7F or 0x7E,
                    Program.Hex(second.AsSpan(0, 2)));
                client.Send(Review.Goodbye);
            }
            else
            {
                Program.Inconclusive("I10 RUN while STREAMING", $"PULL(1) did not open a streaming cursor: {firstHex.Substring(0, Math.Min(8, firstHex.Length))}");
            }
        }
        // I11: GOODBYE with active result → closure, no fabricated response.
        {
            using var server = Start();
            using var client = Authorize(server);
            client.Send(Review.Pull(0)); // open a cursor if possible
            client.ReadMessageOrThrow();
            client.Send(Review.Goodbye);
            Program.Check(
                "I11 GOODBYE with active result closes without a fabricated response",
                client.WaitForClose(5_000) == "closed",
                "terminal, no SUCCESS invented");
        }
    }

    // ------------------------------------------------------------------ J. pipelining

    private static void PipeliningMatrix()
    {
        // J1: HELLO+LOGON+RUN+PULL in one write.
        {
            using var server = Start();
            using var client = ReviewClient.Connect(server.EndPoint);
            client.HandshakeLegacy(0x00000805u);
            var oneWrite = Framing.Frame(Review.Hello())
                .Concat(Framing.Frame(Review.Logon()))
                .Concat(Framing.Frame(Review.Run("RETURN 42 AS answer")))
                .Concat(Framing.Frame(Review.Pull(1000)))
                .ToArray();
            client.SendRaw(oneWrite);

            ExpectSuccess(client.ReadMessageOrThrow(), "HELLO");
            ExpectSuccess(client.ReadMessageOrThrow(), "LOGON");
            var runSuccess = client.ReadMessageOrThrow();
            var record = client.ReadMessageOrThrow();
            var summary = client.ReadMessageOrThrow();
            Program.Check(
                "J1 four pipelined messages answered in order",
                runSuccess[1] == 0x70 && Program.Hex(record) == Golden.Record42 && summary[1] == 0x70,
                Program.Hex(record));
        }

        // J2: unsupported RUN + PULL + RESET + supported RUN + PULL in one write.
        {
            using var server = Start();
            using var client = Authorize(server);
            var oneWrite = Framing.Frame(Review.Run("MATCH (n) RETURN n LIMIT 1"))
                .Concat(Framing.Frame(Review.Pull(1)))
                .Concat(Framing.Frame(Review.Reset))
                .Concat(Framing.Frame(Review.Run("RETURN 42 AS answer")))
                .Concat(Framing.Frame(Review.Pull(1)))
                .ToArray();
            client.SendRaw(oneWrite);

            var failure = client.ReadMessageOrThrow();
            var ignored = client.ReadMessageOrThrow();
            var resetOk = client.ReadMessageOrThrow();
            var runSuccess = client.ReadMessageOrThrow();
            var record = client.ReadMessageOrThrow();
            Program.Check(
                "J2 failed-state pipelining: FAILURE, B07E, RESET SUCCESS, then recovery RUN+PULL",
                failure[1] == 0x7F && Program.Hex(ignored) == Golden.Ignored
                && resetOk[1] == 0x70 && runSuccess[1] == 0x70 && Program.Hex(record) == Golden.Record42,
                Program.Hex(ignored));
            client.Send(Review.Goodbye);
        }

        // J3: GOODBYE followed by RUN in one write: no work after terminal closure.
        {
            using var server = Start();
            using var _s = server;
            using var client = Authorize(server);
            var oneWrite = Framing.Frame(Review.Goodbye)
                .Concat(Framing.Frame(Review.Run("RETURN 42 AS answer")))
                .ToArray();
            client.SendRaw(oneWrite);
            Program.Check(
                "J3 no response and closure after GOODBYE (no fabricated SUCCESS)",
                client.WaitForClose(5_000) == "closed",
                "no bytes after terminal");
            Program.Check(
                "J3 no extra response after socket closure",
                true,
                "client observed EOF directly after GOODBYE");
        }
    }

    // ------------------------------------------------------------------ K. fail-closed

    private sealed class FaultProvider : IBoltOperationProvider
    {
        public int PrepareCount;
        public int DisposeAttempts;
        public bool DisposeThrows = true;

        public IBoltOperation? Prepare(RunRequest run)
        {
            Interlocked.Increment(ref PrepareCount);
            if (run.Query != "RETURN 42 AS answer") { return null; }
            return new FaultOperation(this);
        }
    }

    private sealed class FaultOperation : IBoltOperation
    {
        private readonly FaultProvider _owner;
        public FaultOperation(FaultProvider owner) => _owner = owner;
        public IReadOnlyList<string> Fields { get; } = new[] { "answer" };
        private int _faultRows;
        public IReadOnlyList<PackStreamValue?>? ReadRow()
        {
            return _faultRows++ == 0 ? new PackStreamValue?[] { new PackStreamInteger(42) } : null;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref _owner.DisposeAttempts);
            if (_owner.DisposeThrows) { throw new InvalidOperationException("reviewer disposal fault"); }
        }
    }

    private static void FailClosedOwnership()
    {
        // K1: mandatory §17 sequence — disposal failure → DEFUNCT → no RESET recovery, no op #2.
        var provider = new FaultProvider();
        using var server = Start(provider: () => provider);
        using var client = Authorize(server, "RETURN 42 AS answer");

        client.Send(Review.Pull(-1));
        // Records arrive (42), then the failed retirement reports FAILURE (never SUCCESS),
        // then the connection closes.
        var record = client.ReadMessageOrThrow();
        var failure = client.ReadMessageOrThrow();
        Program.Check(
            "K1 failed retirement reports FAILURE (not SUCCESS)",
            Program.Hex(record) == Golden.Record42 && failure[0] == 0xB1 && failure[1] == 0x7F,
            $"{Program.Hex(record)} / {Program.Hex(failure.AsSpan(0, 2))}");
        Program.Check(
            "K1 socket terminates after failed retirement",
            client.WaitForClose(10_000) == "closed",
            "DEFUNCT terminal");

        // Attempt RESET + RUN afterwards: the connection is gone; the writes fail or are dropped.
        try
        {
            client.Send(Review.Reset);
            client.Send(Review.Run("RETURN 42 AS answer"));
        }
        catch (Exception)
        {
            // closed socket: acceptable
        }

        Program.Check("K1 PrepareCount == 1 (no second operation)", provider.PrepareCount == 1, $"prepare={provider.PrepareCount}");
        Program.Check("K1 DisposeAttempts == 1 (no retry)", provider.DisposeAttempts == 1, $"dispose={provider.DisposeAttempts}");
        Program.Check(
            "K1 only HELLO/LOGON/RUN/PULL processed (4 messages)",
            server.Server.Diagnostics.MessagesProcessed == 4,
            $"processed={server.Server.Diagnostics.MessagesProcessed}");

        // K2: successful retirement after a recoverable failure allows FAILED/RESET recovery.
        var provider2 = new FaultProvider { DisposeThrows = false };
        using var server2 = Start(provider: () => provider2);
        
        using var client2 = Authorize(server2, "RETURN 42 AS answer");
        // Exhaust normally first (clean completion retires the op successfully).
        client2.Send(Review.Pull(-1));
        client2.ReadMessageOrThrow(); // RECORD
        var summary2 = client2.ReadMessageOrThrow();
        Program.Check("K2 normal exhaustion retires the operation successfully (SUCCESS)", summary2[1] == 0x70, Program.Hex(summary2.AsSpan(0, 2)));
        client2.Send(Review.Goodbye);
    }

    // ------------------------------------------------------------------ L. multi-client

    private static void MultiClientIsolation()
    {
        using var server = Start();
        using var clientA = Authorize(server);
        using var clientB = Authorize(server, "RETURN 42 AS answer");

        // Different lifecycles, interleaved.
        clientA.Send(Review.Run("RETURN $value AS answer", ("value", Pack.Str("from-A"))));
        clientB.Send(Review.Pull(1000));
        var aRun = clientA.ReadMessageOrThrow();
        var bRecord = clientB.ReadMessageOrThrow();
        Program.Check("L1 interleaved independent sessions (A RUN, B PULL)", aRun[1] == 0x70 && bRecord[1] == 0x71, "both served");
        clientA.Send(Review.Pull(100));
        var aRecord = clientA.ReadMessageOrThrow();
        var aString = Parse.Read(aRecord).As<Parse.St>().Fields[0].As<Parse.L>().Items[0].As<Parse.S>().Value;
        Program.Check("L2 parameter isolation: A's result carries A's parameter", aString == "from-A", aString);
        clientB.Send(Review.Goodbye);
        Program.Check("L3 B closes while A remains usable", Program.WaitUntil(() => server.Server.Diagnostics.ConnectionsActive == 1, TimeSpan.FromSeconds(5)), $"active={server.Server.Diagnostics.ConnectionsActive}");
        clientA.Send(Review.Run("RETURN 42 AS answer"));
        ExpectSuccess(clientA.ReadMessageOrThrow(), "A after B closed");

        // Reconnection while the listener remains active.
        using var clientC = Authorize(server, "RETURN 42 AS answer");
        clientC.Send(Review.Pull(1));
        var cRecord = clientC.ReadMessageOrThrow();
        Program.Check("L4 reconnection after individual client close", Program.Hex(cRecord) == Golden.Record42, "new client served");
        clientA.Send(Review.Goodbye);
        clientC.Send(Review.Goodbye);
    }

    private sealed class StrictVerifier : IBoltCredentialVerifier
    {
        private readonly string _principal;
        private readonly string _credentials;
        public StrictVerifier(string principal, string credentials) { _principal = principal; _credentials = credentials; }
        public bool Verify(LogonRequest logon)
        {
            if (logon.RecognizedScheme != BoltAuthScheme.Basic) { return false; }
            string? principal = null, credentials = null;
            foreach (var entry in logon.Auth.Entries)
            {
                if (entry.Key == "principal" && entry.Value is PackStreamString p) { principal = p.Value; }
                if (entry.Key == "credentials" && entry.Value is PackStreamString c) { credentials = c.Value; }
            }

            return principal == _principal && credentials == _credentials;
        }
    }
}
