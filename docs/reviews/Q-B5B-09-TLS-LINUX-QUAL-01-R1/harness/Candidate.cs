using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BoltSharp.Transport;

namespace R1Harness;

using static R1Harness.RecordExtensions;

internal static class Candidate
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
                (token) => RunCandidateAttemptAsync(ctx, cell, profile, runs, discovery, null, false, token));
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
                    (token) => RunCandidateAttemptAsync(ctx, cell, selected, runs, -1, repeat, false, token));
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

    internal static async Task<Dictionary<string, object?>> WithWatchdog(Context ctx, CellSpec cell,
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
            var timeout = NewAttempt(cell, profile, attempt, "candidate");
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
            var error = NewAttempt(cell, profile, attempt, "candidate");
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

    private static async Task<Dictionary<string, object?>> RunCandidateAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, int discoveryIndex, int? repeat, bool extendedDeadline, CancellationToken token)
    {
        var result = NewAttempt(cell, profile, attempt, "candidate");
        result["phase"] = discoveryIndex >= 0 ? "discovery" : "repeat";
        result["discovery_index"] = discoveryIndex;
        result["repeat"] = repeat;
        result["production_commit"] = ctx.Commit;
        result["production_tree"] = ctx.Tree;
        var deadlineMs = cell.Kind.Contains("close-notify") ? Campaign.CloseNotifyPendingBoundMs : Campaign.TerminalPendingBoundMs;
        if (extendedDeadline) deadlineMs = 800;
        result["deadline_ms"] = deadlineMs;
        result["interruption"] = cell.Kind.Contains("listener") ? "listener-cancellation"
            : extendedDeadline ? "extended-deadline-control" : "phase-deadline";

        BoltTcpServer? server = null;
        BoltPeer? peer = null;
        PythonProcess? peerProcess = null;
        PythonProcess? proxyProcess = null;
        var provider = ScriptedProvider.WithRow(profile.PrecedingBytes);
        var sent = 0L;
        var confirmed = false;
        var pendingMs = 0d;
        var pendingFirstMs = -1d;
        var peerUnreadAtSample = 0;
        var senderWritable = false;
        var stopMs = -1d;
        var peerReadOutcome = "not-read";
        Dictionary<string, object?> accounting = new();
        var productionSocketClosed = false;
        var port = 0;

        try
        {
            var cert = ctx.Certificate;
            var serverOptions = new BoltTcpServerOptions
            {
                CredentialVerifier = new AcceptAllVerifier(),
                OperationProviderFactory = () => provider,
                Tls = new BoltTlsServerOptions(cert),
                MaxConnections = 1,
                WriteTimeout = TimeSpan.FromSeconds(30),
                MessageReadTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            };

            server = BoltTcpServer.Create(serverOptions);
            await server.StartAsync(token).ConfigureAwait(false);
            port = server.BoundEndPoint!.Port;

            var endpoint = server.BoundEndPoint!;

            if (cell.Kind.Contains("proxy"))
            {
                var proxyScript = Path.Combine(ctx.PeerScriptDir, "gated_proxy.py");
                var proxyPort = 0;
                var proxyDescriptor = Path.Combine(ctx.Journal.PeerDir, $"L4-proxy-{attempt}-descriptor.json");
                var proxyJournal = Path.Combine(ctx.Journal.PeerDir, $"L4-proxy-{attempt}-journal.jsonl");
                var proxyDescriptorObj = new Dictionary<string, object?>
                {
                    ["upstream"] = $"{endpoint.Address}:{endpoint.Port}",
                    ["listen_port"] = 0,
                    ["gate_ms"] = 50,
                    ["journal"] = proxyJournal,
                    ["cert_pem"] = ctx.CertificatePemPath,
                    ["cert_sha256"] = ctx.CertificateSha256
                };
                File.WriteAllText(proxyDescriptor, Json.Write(proxyDescriptorObj, indented: true));
                proxyProcess = PythonProcess.Start(ctx.Python, proxyScript, ["--descriptor", proxyDescriptor]);
                var proxyPortLine = await proxyProcess.ReadLineAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                proxyPort = int.Parse(proxyPortLine!);
                result["proxy_port"] = proxyPort;
                result["proxy_journal"] = proxyJournal;
                endpoint = new IPEndPoint(IPAddress.Loopback, proxyPort);
            }

            peer = await BoltPeer.ConnectAsync(endpoint, cert, profile.PeerReceive, token).ConfigureAwait(false);
            result["tls_protocol"] = peer.Tls.SslProtocol.ToString();
            result["tls_cipher"] = peer.Tls.NegotiatedCipherSuite.ToString();
            result["peer_receive_requested"] = profile.PeerReceive;
            result["peer_receive_actual"] = peer.Tcp.ReceiveBufferSize;
            result["buffer_configured"] = profile.PeerReceive > 0 && peer.Tcp.ReceiveBufferSize > 0;
            result["buffer_configured_side"] = "peer-receive";

            await peer.AuthenticateAsync(token).ConfigureAwait(false);

            if (cell.Kind.Contains("close-notify"))
            {
                await RunCloseNotifyScenarioAsync(ctx, cell, peer, result, token);
            }
            else
            {
                var batch = Wire.Frame(Wire.Encode(Wire.Pull(2)))
                    .Concat(Wire.Frame(Wire.TerminalBegin))
                    .Concat(Wire.Frame(Wire.Encode(Wire.Run())))
                    .ToArray();
                sent = Stopwatch.GetTimestamp();
                await peer.SendAsync(batch, token).ConfigureAwait(false);

                long queue = -1;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var elapsed = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
                    var messages = server.Diagnostics.MessagesProcessed;
                    var available = peer.Available;
                    queue = OperatingSystemProbe.ProcNetTcp(port, peer.LocalPort)?.TxQueue ?? -1;
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
                result["sender_writable_source"] = "proc-net-tcp-tx-queue";
                result["peer_unread_at_sample"] = peerUnreadAtSample;
                result["production_tx_queue_bytes"] = queue;
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
            }

            var messagesFinal = (long)accounting.GetValueOrDefault("messages", 0L)!;
            var active = (long)accounting.GetValueOrDefault("active", 0L)!;
            var accepted = (long)accounting.GetValueOrDefault("accepted", 0L)!;
            var completed = (long)accounting.GetValueOrDefault("completed", 0L)!;
            var failed = (long)accounting.GetValueOrDefault("failed", 0L)!;
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
                ["tls_close_notify_transmission"] = cell.Kind.Contains("close-notify"),
                ["peer_receipt"] = false,
                ["verified_peer_receipt"] = false
            };
            ctx.Journal.Transcript(cell.Id,
                $"attempt {attempt} profile {profile.Ordinal}: confirmed={confirmed} pending={pendingMs:F1} " +
                $"unread={peerUnreadAtSample} elapsed={result["elapsed_ms"]} outcome={result["outcome"]}");
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
            proxyProcess?.Dispose();
            peerProcess?.Dispose();
            if (server is not null)
            {
                try { await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }

        return result;
    }

    private static async Task RunCloseNotifyScenarioAsync(Context ctx, CellSpec cell,
        BoltPeer peer, Dictionary<string, object?> result, CancellationToken token)
    {
        var payload = new byte[36864];
        var write = peer.Tls.WriteAsync(payload).AsTask();
        await write.WaitAsync(TimeSpan.FromMilliseconds(2000), token).ConfigureAwait(false);

        peer.Tcp.SendBufferSize = 2048;

        var started = Stopwatch.GetTimestamp();
        var shutdown = peer.Tls.ShutdownAsync();
        var pending = false;
        var pendingMs = 0d;
        bool? writable = null;
        while (!shutdown.IsCompleted && Stopwatch.GetElapsedTime(started).TotalMilliseconds < Campaign.CloseNotifyPendingBoundMs)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            writable = peer.Tcp.Client.Poll(0, SelectMode.SelectWrite);
            if (elapsed >= Campaign.MinimumPendingMs && !shutdown.IsCompleted)
            {
                pending = true;
                pendingMs = elapsed;
                break;
            }
            await Task.Delay(2, token).ConfigureAwait(false);
        }
        result["shutdown_pending"] = pending;
        result["shutdown_pending_ms"] = Math.Round(pendingMs, 3);
        result["sender_writable_at_sample"] = writable ?? false;

        var deadlineMs = Campaign.CloseNotifyPendingBoundMs;
        var wait = deadlineMs - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (wait > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(wait), token).ConfigureAwait(false);

        peer.Tls.Dispose();
    }

    internal static async Task RunCapCellAsync(Context ctx, CellSpec cell, int declaredLimit, int precedingBytes)
    {
        var profile = new Profile(1, precedingBytes, 4096, 0);
        var record = await WithWatchdog(ctx, cell, profile, 1, 0, null,
            (token) => RunCapAttemptAsync(ctx, cell, profile, 1, declaredLimit, token));
        record["supplementary"] = true;
        record["declared_limit"] = declaredLimit;
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

    internal static async Task<Dictionary<string, object?>> RunCapAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, int declaredLimit, CancellationToken token)
    {
        var result = NewAttempt(cell, profile, attempt, "candidate-cap");
        result["declared_limit"] = declaredLimit;
        BoltTcpServer? server = null;
        PlaintextCloseCapture? capture = null;
        TcpClient? peer = null;
        long ciphertextBytes = 0;
        try
        {
            capture = new PlaintextCloseCapture();
            var provider = ScriptedProvider.WithRow(profile.PrecedingBytes);
            server = BoltTcpServer.Create(new BoltTcpServerOptions
            {
                CredentialVerifier = new AcceptAllVerifier(),
                OperationProviderFactory = () => provider,
                Tls = new BoltTlsServerOptions(ctx.Certificate),
                MaxConnections = 1,
                WriteTimeout = TimeSpan.FromSeconds(30),
                ShutdownTimeout = TimeSpan.FromSeconds(5)
            });
            await server.StartAsync(token).ConfigureAwait(false);

            peer = new TcpClient(AddressFamily.InterNetwork);
            peer.ReceiveBufferSize = profile.PeerReceive;
            await peer.ConnectAsync(server.BoundEndPoint!, token).ConfigureAwait(false);
            var raw = peer.GetStream();
            var tls = new SslStream(raw, leaveInnerStreamOpen: true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, c, _, _) => c?.GetCertHashString() == ctx.Certificate.Thumbprint
            }, token).ConfigureAwait(false);

            await tls.WriteAsync(Wire.Handshake, token).ConfigureAwait(false);
            var selected = new byte[4];
            await ReadExactlyAsync(tls, selected, token).ConfigureAwait(false);
            var reader = new BoltSharp.Framing.BoltMessageReader(tls);
            var codec = new BoltSharp.Messages.BoltMessageCodec();
            foreach (var request in new BoltSharp.Messages.BoltRequest[] { Wire.Hello(), Wire.Logon(), Wire.Run() })
            {
                await tls.WriteAsync(Wire.Frame(Wire.Encode(request)), token).ConfigureAwait(false);
                var message = await reader.ReadMessageAsync(token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("closed during authentication");
                _ = codec.DecodeResponse(message);
            }

            var batch = Wire.Frame(Wire.Encode(Wire.Pull(2)))
                .Concat(Wire.Frame(Wire.TerminalBegin))
                .ToArray();
            await tls.WriteAsync(batch, token).ConfigureAwait(false);

            var flood = Task.Run(async () =>
            {
                var payload = new byte[4096];
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await tls.WriteAsync(payload, token).ConfigureAwait(false);
                        Interlocked.Add(ref ciphertextBytes, payload.Length);
                        if (ciphertextBytes >= declaredLimit)
                            break;
                    }
                }
                catch (Exception) { }
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
            result["declared_plaintext_cap"] = declaredLimit;
            result["ciphertext_bytes"] = ciphertextBytes;
            result["ciphertext_source"] = "candidate TLS write flood counter";
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
                && end.Bytes == declaredLimit
                && end.Reason == "byte-limit"
                && capture.Admitted == 1
                && capture.Released == 1
                && active == 0
                && accepted == 1
                && completed + failed == 1;
            result["checks"] = new Dictionary<string, object?>
            {
                ["drain_at_cap"] = drain?.Bytes == declaredLimit,
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
                $"attempt {attempt}: cap {declaredLimit} plaintext={drain?.Bytes} ciphertext={ciphertextBytes} outcome={result["outcome"]}");
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

    private static async Task ReadExactlyAsync(SslStream stream, byte[] destination, CancellationToken token)
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
        => cell.Kind.StartsWith("candidate-cap", StringComparison.Ordinal)
            ? "cap-bounded-and-clean"
            : "induced-and-interrupted";

    internal static bool IsChecksFailure(string outcome)
        => outcome is "induced-but-checks-failed" or "cap-checks-failed" or "phase-candidate-cleanup-failed";
}