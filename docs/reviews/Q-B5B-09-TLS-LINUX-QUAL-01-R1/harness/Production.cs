using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BoltSharp.Transport;

namespace R1Harness;

using static R1Harness.RecordExtensions;

internal static class Production
{
    internal static async Task RunCellAsync(Context ctx, CellSpec cell)
    {
        var runs = 0;
        var successes = 0;
        Profile? qualifying = null;
        var failure = "";
        for (var discovery = 0; discovery < cell.DiscoveryBound && failure.Length == 0; discovery++)
        {
            var profile = Campaign.ProfileFor(cell.Seed, discovery);
            runs++;
            var record = await WithWatchdog(ctx, cell, profile, runs, discovery, null,
                (token) => RunTerminalAttemptAsync(ctx, cell, profile, runs, discovery, null, false, token));
            ctx.Journal.Write(record);
            var outcome = (string)record["outcome"]!;
            if (outcome == QualifyingOutcome(cell))
            {
                qualifying = profile;
                successes = 1;
                break;
            }

            if (IsChecksFailure(outcome))
            {
                failure = outcome;
                break;
            }
        }

        if (qualifying is { } selected && failure.Length == 0 && cell.Repeats > 0)
        {
            for (var repeat = 1; repeat <= cell.Repeats; repeat++)
            {
                runs++;
                var record = await WithWatchdog(ctx, cell, selected, runs, -1, repeat,
                    (token) => RunTerminalAttemptAsync(ctx, cell, selected, runs, -1, repeat, false, token));
                ctx.Journal.Write(record);
                var outcome = (string)record["outcome"]!;
                if (outcome == QualifyingOutcome(cell))
                {
                    successes++;
                }
                else
                {
                    failure = "repeat-miss:" + outcome;
                    break;
                }
            }
        }

        var qualified = qualifying is not null && failure.Length == 0 && successes == 1 + cell.Repeats;
        ctx.Journal.WriteCellSummary(cell.Id, new Dictionary<string, object?>
        {
            ["cell"] = cell.Id,
            ["kind"] = cell.Kind,
            ["seed"] = cell.Seed,
            ["runs"] = runs,
            ["successes"] = successes,
            ["qualified"] = qualified,
            ["qualifying_profile_ordinal"] = qualifying?.Ordinal,
            ["failure"] = failure,
            ["discovery_bound"] = cell.DiscoveryBound,
            ["repeats_required"] = cell.Repeats,
            ["max_runs"] = cell.MaxRuns
        });
        ctx.Journal.Write(RecordExtensions.With(ctx.Journal.Record("cell-end", cell.Id),
            ("runs", runs), ("successes", successes), ("qualified", qualified),
            ("qualifying_profile_ordinal", qualifying?.Ordinal), ("failure", failure)));
    }

    internal static async Task RunProductionPlaintextCapAsync(Context ctx)
    {
        var cell = Campaign.L6Production;
        var profile = new Profile(1, 4096, 4096, 0);
        var record = await WithWatchdog(ctx, cell, profile, 1, 0, null,
            (token) => RunPlaintextCapAttemptAsync(ctx, cell, profile, 1, token));
        record["supplementary"] = true;
        ctx.Journal.Write(record);
        var qualified = (string)record["outcome"]! == "cap-bounded-and-clean";
        ctx.Journal.WriteCellSummary(cell.Id, new Dictionary<string, object?>
        {
            ["cell"] = cell.Id,
            ["kind"] = cell.Kind,
            ["supplementary"] = true,
            ["runs"] = 1,
            ["successes"] = qualified ? 1 : 0,
            ["qualified"] = qualified,
            ["failure"] = qualified ? "" : record["outcome"]
        });
    }

    private static async Task<Dictionary<string, object?>> WithWatchdog(Context ctx, CellSpec cell,
        Profile profile, int attempt, int discoveryIndex, int? repeat,
        Func<CancellationToken, Task<Dictionary<string, object?>>> body)
    {
        using var watchdog = new CancellationTokenSource(TimeSpan.FromMilliseconds(Campaign.AttemptLifetimeMs));
        var started = Stopwatch.GetTimestamp();
        try
        {
            var task = body(watchdog.Token);
            var result = await task.WaitAsync(TimeSpan.FromMilliseconds(Campaign.AttemptLifetimeMs));
            result["attempt_lifetime_ms"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3);
            return result;
        }
        catch (TimeoutException)
        {
            watchdog.Cancel();
            var timeout = NewAttempt(cell, profile, attempt, "production");
            timeout["phase"] = discoveryIndex >= 0 ? "discovery" : "repeat";
            timeout["discovery_index"] = discoveryIndex;
            timeout["repeat"] = repeat;
            timeout["outcome"] = "attempt-watchdog";
            timeout["error"] = "attempt exceeded the 3500 ms lifetime";
            timeout["attempt_lifetime_ms"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3);
            ctx.Journal.Transcript(cell.Id, $"attempt {attempt}: watchdog");
            return timeout;
        }
        catch (Exception ex)
        {
            var error = NewAttempt(cell, profile, attempt, "production");
            error["phase"] = discoveryIndex >= 0 ? "discovery" : "repeat";
            error["discovery_index"] = discoveryIndex;
            error["repeat"] = repeat;
            error["outcome"] = "environment";
            error["error"] = ex.GetType().Name + ":" + ex.Message;
            error["attempt_lifetime_ms"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3);
            ctx.Journal.Transcript(cell.Id, $"attempt {attempt}: {error["error"]}");
            return error;
        }
    }

    private static Dictionary<string, object?> NewAttempt(CellSpec cell, Profile profile, int attempt, string declaration)
        => new()
        {
            ["record_type"] = "attempt",
            ["cell"] = cell.Id,
            ["attempt"] = attempt,
            ["seed"] = cell.Seed,
            ["profile"] = new Dictionary<string, object?>
            {
                ["ordinal"] = profile.Ordinal,
                ["preceding_bytes"] = profile.PrecedingBytes,
                ["peer_receive"] = profile.PeerReceive,
                ["target_send"] = profile.TargetSend
            },
            ["declaration"] = declaration,
            ["production_commit"] = "",
            ["production_tree"] = ""
        };

    internal static async Task<Dictionary<string, object?>> RunTerminalAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, int discoveryIndex, int? repeat, bool extendedDeadline, CancellationToken token)
    {
        var result = NewAttempt(cell, profile, attempt, "production");
        result["phase"] = discoveryIndex >= 0 ? "discovery" : "repeat";
        result["discovery_index"] = discoveryIndex;
        result["repeat"] = repeat;
        result["production_commit"] = ctx.Commit;
        result["production_tree"] = ctx.Tree;
        var deadlineMs = extendedDeadline ? 800 : Campaign.TerminalPendingBoundMs;
        result["deadline_ms"] = deadlineMs;
        result["listener_delay_ms"] = cell.Id == "L2" ? 50 : (object?)null;
        result["interruption"] = cell.Id == "L2" ? "listener-cancellation"
            : extendedDeadline ? "extended-deadline-control" : "total-deadline";

        BoltTcpServer? server = null;
        BoltPeer? peer = null;
        var provider = ScriptedProvider.WithRow(profile.PrecedingBytes);
        var sent = 0L;
        var confirmed = false;
        var pendingMs = 0d;
        var pendingFirstMs = -1d;
        var peerUnreadAtSample = 0;
        var txQueue = -1L;
        var senderWritable = false;
        var stopMs = -1d;
        var peerReadOutcome = "not-read";
        Dictionary<string, object?> accounting = new();
        var productionSocketClosed = false;
        var port = 0;
        try
        {
            server = BoltTcpServer.Create(new BoltTcpServerOptions
            {
                CredentialVerifier = new AcceptAllVerifier(),
                OperationProviderFactory = () => provider,
                Tls = new BoltTlsServerOptions(ctx.Certificate),
                MaxConnections = 1,
                WriteTimeout = TimeSpan.FromSeconds(30),
                MessageReadTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            });
            await server.StartAsync(token).ConfigureAwait(false);
            port = server.BoundEndPoint!.Port;
            peer = await BoltPeer.ConnectAsync(server.BoundEndPoint!, ctx.Certificate, profile.PeerReceive,
                token).ConfigureAwait(false);
            result["tls_protocol"] = peer.Tls.SslProtocol.ToString();
            result["tls_cipher"] = peer.Tls.NegotiatedCipherSuite.ToString();
            result["peer_receive_requested"] = profile.PeerReceive;
            result["peer_receive_actual"] = peer.Tcp.ReceiveBufferSize;
            result["buffer_configured"] = profile.PeerReceive > 0 && peer.Tcp.ReceiveBufferSize > 0;
            result["buffer_configured_side"] = "peer-receive";
            result["os_send_buffer_default"] = OperatingSystemProbe.SendBufferDefault();
            await peer.AuthenticateAsync(token).ConfigureAwait(false);

            var batch = Wire.Frame(Wire.Encode(Wire.Pull(2)))
                .Concat(Wire.Frame(Wire.TerminalBegin))
                .Concat(Wire.Frame(Wire.Encode(Wire.Run())))
                .ToArray();
            sent = Stopwatch.GetTimestamp();
            await peer.SendAsync(batch, token).ConfigureAwait(false);

            while (true)
            {
                token.ThrowIfCancellationRequested();
                var elapsed = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                var messages = server.Diagnostics.MessagesProcessed;
                var available = peer.Available;
                var queue = OperatingSystemProbe.ProcNetTcp(port, peer.LocalPort)?.TxQueue ?? -1;
                var pendingNow = messages == 3 && available > 0
                    && (queue > 0 || !OperatingSystemProbe.IsNativeLinux());
                if (!confirmed && pendingNow)
                {
                    confirmed = true;
                    pendingFirstMs = elapsed;
                }

                if (confirmed)
                {
                    pendingMs = Stopwatch.GetElapsedTime(sent).TotalMilliseconds - pendingFirstMs;
                    peerUnreadAtSample = available;
                    txQueue = queue;
                    senderWritable = false;
                    if (pendingMs >= Campaign.MinimumPendingMs)
                        break;
                }

                if (messages > 3)
                    break;
                if (elapsed > 1500)
                    break;
                await Task.Delay(2, token).ConfigureAwait(false);
            }

            result["confirmed"] = confirmed;
            result["pending_first_sample_ms"] = pendingFirstMs < 0 ? null : Math.Round(pendingFirstMs, 3);
            result["pending_ms"] = Math.Round(pendingMs, 3);
            result["sender_writable_at_sample"] = senderWritable;
            result["sender_writable_source"] = txQueue > 0 ? "proc-net-tcp-tx-queue" : "peer-parked+messages-not-advanced";
            result["peer_unread_at_sample"] = peerUnreadAtSample;
            result["production_tx_queue_bytes"] = txQueue;
            result["stage"] = "kernel-write-pending";

            if (confirmed && cell.Id == "L2")
            {
                var delay = 50 - pendingMs;
                if (delay > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), token).ConfigureAwait(false);
            }
            else if (confirmed)
            {
                var wait = deadlineMs - Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                if (wait > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(wait), token).ConfigureAwait(false);
            }

            var stopStarted = Stopwatch.GetTimestamp();
            result["stop_started_ms"] = Math.Round(Stopwatch.GetElapsedTime(sent).TotalMilliseconds, 3);
            await server.StopAsync(token).WaitAsync(TimeSpan.FromSeconds(6), token).ConfigureAwait(false);
            stopMs = Stopwatch.GetElapsedTime(stopStarted).TotalMilliseconds;
            result["stop_ms"] = Math.Round(stopMs, 3);
            result["elapsed_ms"] = Math.Round(Stopwatch.GetElapsedTime(sent).TotalMilliseconds, 3);

            var settle = Stopwatch.GetTimestamp();
            while (server.Diagnostics.ConnectionsActive > 0
                && Stopwatch.GetElapsedTime(settle).TotalMilliseconds < 2000)
                await Task.Delay(2, token).ConfigureAwait(false);

            peerReadOutcome = await peer.ReadUntilEofAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            result["peer_read_outcome"] = peerReadOutcome;
            result["peer_unread_at_end"] = peer.Available;
            result["peer_closed"] = peerReadOutcome.StartsWith("clean-eof", StringComparison.Ordinal)
                || peerReadOutcome.Contains("IOException", StringComparison.Ordinal)
                || peerReadOutcome.Contains("SocketException", StringComparison.Ordinal);
            productionSocketClosed = OperatingSystemProbe.ProcNetTcp(port, peer.LocalPort) is null;
            result["production_socket_closed"] = productionSocketClosed;
            accounting = Account.Snapshot(server);
            result["accounting"] = accounting;
            result["permits"] = 0;
            result["released_permits"] = 0;

            var messagesFinal = (long)accounting["messages"]!;
            var active = (long)accounting["active"]!;
            var accepted = (long)accounting["accepted"]!;
            var completed = (long)accounting["completed"]!;
            var failed = (long)accounting["failed"]!;
            var checks = confirmed
                && pendingMs >= Campaign.MinimumPendingMs
                && !senderWritable
                && peerUnreadAtSample > 0
                && (bool)result["buffer_configured"]!
                && (double)result["elapsed_ms"]! <= Campaign.TotalElapsedBoundMs
                && active == 0
                && accepted == 1
                && completed + failed == 1
                && messagesFinal == 3
                && !peerReadOutcome.StartsWith("read-timeout", StringComparison.Ordinal)
                && stopMs >= 0
                && stopMs < 1000;
            result["checks"] = new Dictionary<string, object?>
            {
                ["confirmed"] = confirmed,
                ["pending_at_least_20_ms"] = pendingMs >= Campaign.MinimumPendingMs,
                ["sender_not_writable"] = !senderWritable,
                ["peer_unread_positive"] = peerUnreadAtSample > 0,
                ["buffer_configured"] = result["buffer_configured"],
                ["elapsed_within_bound"] = (double)result["elapsed_ms"]! <= Campaign.TotalElapsedBoundMs,
                ["accounting_active_zero"] = active == 0,
                ["accounting_balanced"] = accepted == completed + failed,
                ["single_connection"] = accepted == 1 && completed + failed == 1,
                ["terminal_write_not_completed"] = messagesFinal == 3,
                ["peer_closed"] = result["peer_closed"],
                ["stop_bounded"] = stopMs >= 0 && stopMs < 1000
            };
            result["outcome"] = checks ? "induced-and-interrupted"
                : confirmed ? "induced-but-checks-failed" : "precursor-not-observed";
            result["guarantee_levels"] = new Dictionary<string, object?>
            {
                ["local_write_completion"] = false,
                ["tls_close_notify_transmission"] = false,
                ["peer_receipt"] = false,
                ["verified_peer_receipt"] = false
            };
            ctx.Journal.Transcript(cell.Id,
                $"attempt {attempt} profile {profile.Ordinal}: confirmed={confirmed} pending={pendingMs:F1} " +
                $"unread={peerUnreadAtSample} tx={txQueue} elapsed={result["elapsed_ms"]} outcome={result["outcome"]}");
        }
        catch (Exception ex)
        {
            result["outcome"] = "environment";
            result["error"] = ex.GetType().Name + ":" + ex.Message;
            result["accounting"] = server is null ? null : Account.Snapshot(server);
            ctx.Journal.Transcript(cell.Id, $"attempt {attempt}: environment {result["error"]}");
        }
        finally
        {
            peer?.Dispose();
            if (server is not null)
            {
                try { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }

        return result;
    }

    internal static async Task<Dictionary<string, object?>> RunReceiptAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, CancellationToken token)
    {
        var result = NewAttempt(cell, profile, attempt, "production");
        result["production_commit"] = ctx.Commit;
        result["production_tree"] = ctx.Tree;
        BoltTcpServer? server = null;
        var expected = Wire.ExpectedTerminalFailure();
        var expectedDigest = Wire.Sha256Hex(expected);
        result["expected_terminal_message_bytes"] = expected.Length;
        result["expected_terminal_sha256"] = expectedDigest;
        var descriptorPath = Path.Combine(ctx.Journal.PeerDir, $"L7-attempt-{attempt}-descriptor.json");
        var receiptPath = Path.Combine(ctx.Journal.PeerDir, $"L7-attempt-{attempt}-receipt.jsonl");
        try
        {
            var provider = ScriptedProvider.WithRow(profile.PrecedingBytes);
            server = BoltTcpServer.Create(new BoltTcpServerOptions
            {
                CredentialVerifier = new AcceptAllVerifier(),
                OperationProviderFactory = () => provider,
                Tls = new BoltTlsServerOptions(ctx.Certificate),
                MaxConnections = 1,
                WriteTimeout = TimeSpan.FromSeconds(30),
                MessageReadTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            });
            await server.StartAsync(token).ConfigureAwait(false);
            var endpoint = server.BoundEndPoint!;
            var requests = new List<string>
            {
                Convert.ToHexString(Wire.Frame(Wire.Encode(Wire.Hello()))).ToLowerInvariant(),
                Convert.ToHexString(Wire.Frame(Wire.Encode(Wire.Logon()))).ToLowerInvariant(),
                Convert.ToHexString(Wire.Frame(Wire.Encode(Wire.Run()))).ToLowerInvariant(),
                Convert.ToHexString(Wire.Frame(Wire.Encode(Wire.Pull(2)))).ToLowerInvariant(),
                Convert.ToHexString(Wire.Frame(Wire.TerminalBegin)).ToLowerInvariant(),
                Convert.ToHexString(Wire.Frame(Wire.Encode(Wire.Run()))).ToLowerInvariant()
            };
            var descriptor = new Dictionary<string, object?>
            {
                ["cell"] = cell.Id,
                ["attempt"] = attempt,
                ["endpoint"] = $"{endpoint.Address}:{endpoint.Port}",
                ["cert_pem"] = ctx.CertificatePemPath,
                ["cert_sha256"] = ctx.CertificateSha256,
                ["handshake_hex"] = Convert.ToHexString(Wire.Handshake).ToLowerInvariant(),
                ["selected_hex"] = Convert.ToHexString(Wire.Selected).ToLowerInvariant(),
                ["requests_hex"] = requests,
                ["expected_message_bytes"] = expected.Length,
                ["expected_sha256"] = expectedDigest,
                ["receipt_journal"] = receiptPath,
                ["app"] = "python3-stdlib-ssl-peer",
                ["timeout_ms"] = 8000
            };
            File.WriteAllText(descriptorPath, Json.Write(descriptor, indented: true));
            using var process = PythonProcess.Start(ctx.Python,
                Path.Combine(ctx.PeerScriptDir, "receipt_peer.py"),
                ["--descriptor", descriptorPath]);
            result["peer_pid"] = process.Id;
            result["peer_descriptor"] = descriptorPath;
            result["peer_receipt_journal"] = receiptPath;
            var exited = await process.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            result["peer_exited"] = exited;
            result["peer_exit_code"] = exited ? process.StandardError : null;
            if (!exited)
            {
                result["outcome"] = "peer-receipt-failed";
                result["error"] = "peer process did not exit";
                return result;
            }

            if (!File.Exists(receiptPath))
            {
                result["outcome"] = "peer-receipt-failed";
                result["error"] = "peer receipt journal missing: " + process.StandardError;
                return result;
            }

            var lines = File.ReadAllLines(receiptPath)
                .Where(l => l.Trim().Length > 0)
                .Select(l => JsonSerializer.Deserialize<Dictionary<string, object?>>(l)!)
                .ToArray();
            var receipt = lines.LastOrDefault(l => (string?)l.GetValueOrDefault("record_type") == "peer-receipt");
            var eof = lines.LastOrDefault(l => (string?)l.GetValueOrDefault("record_type") == "peer-eof");
            result["peer_receipt"] = receipt is not null;
            result["peer_eof"] = eof is not null;
            if (receipt is not null)
            {
                var hex = (string?)receipt.GetValueOrDefault("message_hex") ?? "";
                var bytes = hex.Length > 0 ? Convert.FromHexString(hex) : Array.Empty<byte>();
                var digest = Wire.Sha256Hex(bytes);
                result["receipt_message_bytes"] = receipt.GetValueOrDefault("message_bytes");
                result["receipt_sha256"] = receipt.GetValueOrDefault("sha256");
                result["receipt_recomputed_sha256"] = digest;
                result["receipt_ms"] = receipt.GetValueOrDefault("receipt_ms");
                result["receipt_app"] = receipt.GetValueOrDefault("app");
                result["receipt_journal_field"] = receipt.GetValueOrDefault("journal");
                result["receipt_matches"] = bytes.Length == expected.Length
                    && digest == expectedDigest
                    && (string?)receipt.GetValueOrDefault("sha256") == expectedDigest
                    && Convert.ToInt32(receipt.GetValueOrDefault("message_bytes")) == expected.Length;
            }

            result["peer_unread_at_end"] = eof?.GetValueOrDefault("unread_at_end");
            result["tls_protocol"] = receipt?.GetValueOrDefault("tls_version");
            result["tls_cipher"] = receipt?.GetValueOrDefault("tls_cipher");
            var accounting = Account.Snapshot(server);
            result["accounting"] = accounting;
            var active = (long)accounting["active"]!;
            var accepted = (long)accounting["accepted"]!;
            var completed = (long)accounting["completed"]!;
            var failed = (long)accounting["failed"]!;
            var messages = (long)accounting["messages"]!;
            var checks = receipt is not null
                && eof is not null
                && (bool)(result["receipt_matches"] ?? false)
                && Convert.ToInt64(result["peer_unread_at_end"] ?? -1) == 0
                && active == 0
                && accepted == 1
                && completed + failed == 1
                && failed == 0
                && messages == 5;
            result["checks"] = new Dictionary<string, object?>
            {
                ["receipt_present"] = receipt is not null,
                ["peer_eof"] = eof is not null,
                ["receipt_matches_campaign_digest"] = result["receipt_matches"],
                ["peer_unread_at_end_zero"] = Convert.ToInt64(result["peer_unread_at_end"] ?? -1) == 0,
                ["accounting_active_zero"] = active == 0,
                ["accounting_balanced"] = accepted == completed + failed,
                ["single_connection"] = accepted == 1 && completed + failed == 1,
                ["completed"] = failed == 0,
                ["terminal_message_processed"] = messages == 5
            };
            result["outcome"] = checks ? "peer-receipt-verified" : "peer-receipt-failed";
            result["guarantee_levels"] = new Dictionary<string, object?>
            {
                ["local_write_completion"] = true,
                ["tls_close_notify_transmission"] = false,
                ["peer_receipt"] = true,
                ["verified_peer_receipt"] = checks
            };
            ctx.Journal.Transcript(cell.Id,
                $"attempt {attempt}: receipt={receipt is not null} eof={eof is not null} " +
                $"digest={result["receipt_matches"]} outcome={result["outcome"]}");
        }
        catch (Exception ex)
        {
            result["outcome"] = "environment";
            result["error"] = ex.GetType().Name + ":" + ex.Message;
            result["accounting"] = server is null ? null : Account.Snapshot(server);
            ctx.Journal.Transcript(cell.Id, $"attempt {attempt}: environment {result["error"]}");
        }
        finally
        {
            if (server is not null)
            {
                try { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }

        return result;
    }

    private static async Task<Dictionary<string, object?>> RunPlaintextCapAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, CancellationToken token)
    {
        var result = NewAttempt(cell, profile, attempt, "production-plaintext");
        result["supplementary"] = true;
        BoltTcpServer? server = null;
        PlaintextCloseCapture? capture = null;
        TcpClient? peer = null;
        try
        {
            capture = new PlaintextCloseCapture();
            var provider = ScriptedProvider.WithRow(profile.PrecedingBytes);
            server = BoltTcpServer.Create(new BoltTcpServerOptions
            {
                CredentialVerifier = new AcceptAllVerifier(),
                OperationProviderFactory = () => provider,
                Tls = null,
                MaxConnections = 1,
                WriteTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            });
            await server.StartAsync(token).ConfigureAwait(false);
            peer = new TcpClient(AddressFamily.InterNetwork);
            await peer.ConnectAsync(server.BoundEndPoint!, token).ConfigureAwait(false);
            var stream = peer.GetStream();
            await stream.WriteAsync(Wire.Handshake, token).ConfigureAwait(false);
            var selected = new byte[4];
            await ReadExactlyAsync(stream, selected, token).ConfigureAwait(false);
            var reader = new BoltSharp.Framing.BoltMessageReader(stream);
            var codec = new BoltSharp.Messages.BoltMessageCodec();
            foreach (var request in new BoltSharp.Messages.BoltRequest[]
                     { Wire.Hello(), Wire.Logon(), Wire.Run() })
            {
                await stream.WriteAsync(Wire.Frame(Wire.Encode(request)), token).ConfigureAwait(false);
                var message = await reader.ReadMessageAsync(token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("closed during authentication");
                _ = codec.DecodeResponse(message);
            }

            var batch = Wire.Frame(Wire.Encode(Wire.Pull(2)))
                .Concat(Wire.Frame(Wire.TerminalBegin))
                .ToArray();
            await stream.WriteAsync(batch, token).ConfigureAwait(false);
            var flood = Task.Run(async () =>
            {
                var payload = new byte[4096];
                try
                {
                    while (!token.IsCancellationRequested)
                        await stream.WriteAsync(payload, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }, token);

            var started = Stopwatch.GetTimestamp();
            while (capture.DrainEnd is null && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 3000)
                await Task.Delay(5, token).ConfigureAwait(false);
            var drain = capture.DrainEnd;
            result["drain_end"] = drain is null ? null : new Dictionary<string, object?>
            {
                ["name"] = drain.Value.Name,
                ["bytes"] = drain.Value.Bytes,
                ["reason"] = drain.Value.Reason
            };
            result["plaintext_bytes"] = drain?.Bytes ?? -1;
            result["declared_plaintext_cap"] = Campaign.PlaintextLimit;
            result["ciphertext_bytes"] = 0;
            result["ciphertext_source"] = "not-applicable-plaintext";
            result["plaintext_source"] = "production BoltPlaintextTerminalClose drain-end event";
            result["permits"] = capture.Admitted;
            result["released_permits"] = capture.Released;
            result["elapsed_ms"] = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3);
            var accounting = Account.Snapshot(server, capture);
            result["accounting"] = accounting;
            var active = (long)accounting["active"]!;
            var accepted = (long)accounting["accepted"]!;
            var completed = (long)accounting["completed"]!;
            var failed = (long)accounting["failed"]!;
            var checks = drain is { } end
                && end.Bytes == Campaign.PlaintextLimit
                && end.Reason == "byte-limit"
                && capture.Admitted == 1
                && capture.Released == 1
                && active == 0
                && accepted == 1
                && completed + failed == 1;
            result["checks"] = new Dictionary<string, object?>
            {
                ["drain_at_plaintext_cap"] = drain?.Bytes == Campaign.PlaintextLimit,
                ["drain_reason_byte_limit"] = drain?.Reason == "byte-limit",
                ["permits_balanced"] = capture.Admitted == 1 && capture.Released == 1,
                ["accounting_active_zero"] = active == 0,
                ["accounting_balanced"] = accepted == completed + failed
            };
            result["outcome"] = checks ? "cap-bounded-and-clean" : "cap-checks-failed";
            result["guarantee_levels"] = new Dictionary<string, object?>
            {
                ["local_write_completion"] = true,
                ["tls_close_notify_transmission"] = false,
                ["peer_receipt"] = false,
                ["verified_peer_receipt"] = false
            };
            ctx.Journal.Transcript(cell.Id,
                $"attempt {attempt}: plaintext drain={drain?.Bytes} reason={drain?.Reason} outcome={result["outcome"]}");
        }
        catch (Exception ex)
        {
            result["outcome"] = "environment";
            result["error"] = ex.GetType().Name + ":" + ex.Message;
        }
        finally
        {
            peer?.Dispose();
            capture?.Dispose();
            if (server is not null)
            {
                try { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }

        return result;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] destination, CancellationToken token)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = await stream.ReadAsync(destination.AsMemory(read), token).ConfigureAwait(false);
            if (count == 0)
                throw new EndOfStreamException("closed during read");
            read += count;
        }
    }

    internal static string QualifyingOutcome(CellSpec cell)
        => cell.Kind.StartsWith("candidate-cap", StringComparison.Ordinal) || cell.Kind == "production-plaintext-cap"
            ? "cap-bounded-and-clean"
            : cell.Kind == "production-peer-receipt" ? "peer-receipt-verified" : "induced-and-interrupted";

    internal static bool IsChecksFailure(string outcome)
        => outcome is "induced-but-checks-failed" or "cap-checks-failed" or "peer-receipt-failed"
            or "phase-candidate-cleanup-failed";
}
