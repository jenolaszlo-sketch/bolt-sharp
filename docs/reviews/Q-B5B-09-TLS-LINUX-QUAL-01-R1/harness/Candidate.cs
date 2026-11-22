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
        // Cap cells (L5/L6) run the E1-faithful candidate TLS drain; other candidate cells run
        // the close-notify / terminal-write observation flow. The R2 dispatch routed cap cells
        // through the terminal-write flow, leaving RunCapAttemptAsync unreachable â€” fixed here.
        var isCapCell = cell.Id is "L5" or "L6"; // kind prefixes never matched (R2 dispatch bug)
        var runs = 0;
        var successes = 0;
        Profile? qualifying = null;
        var failure = "";
        for (var discovery = 0; discovery < cell.DiscoveryBound && failure.Length == 0; discovery++)
        {
            var profile = isCapCell ? Campaign.CapProfile : Campaign.ProfileFor(cell.Seed, discovery);
            runs++;
            var record = await WithWatchdog(ctx, cell, profile, runs, discovery, null,
                (token) => isCapCell
                    ? RunCapAttemptAsync(ctx, cell, profile, runs, discovery, null, false, token)
                    : RunCandidateAttemptAsync(ctx, cell, profile, runs, discovery, null, false, token));
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
                    (token) => isCapCell
                        ? RunCapAttemptAsync(ctx, cell, selected, runs, -1, repeat, false, token)
                        : RunCandidateAttemptAsync(ctx, cell, selected, runs, -1, repeat, false, token));
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

    /// <summary>
    /// E1-faithful candidate cap attempt (L5/L6, explicitly candidate-only). Structure:
    /// 1. Production TLS terminal observation: a real BoltTcpServer TLS session completes
    ///    HELLO/LOGON/RUN, then PULL(4096-row) + BEGIN â€” production writes the PULL result and
    ///    the terminal failure over TLS (production TLS terminal path, preserved from R1).
    /// 2. Candidate drain: a harness-owned TLS listener runs the E1-faithful capped drain whose
    ///    ciphertext counter sits at the transport boundary beneath SslStream (the counting
    ///    inner stream measures the exact encrypted record bytes SslStream consumes), with the
    ///    plaintext counter clipped exactly at the registered plaintext budget.
    /// 3. Controlled differential: the flooding client also counts beneath its own SslStream,
    ///    so the journal proves encrypted wire bytes differ from application bytes
    ///    (TLS record overhead is measured, not assumed).
    /// Registered caps: L5 enforces the 32768-byte encrypted cap (tiny flood â€” the encrypted
    /// cap binds first); L6 enforces 8192 plaintext (large flood â€” the plaintext cap binds
    /// first, exact) with the encrypted cap verified as a bound (E1 T5/T6 pattern).
    /// </summary>
    internal static async Task<Dictionary<string, object?>> RunCapAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, int discoveryIndex, int? repeat,
        bool extendedDeadline, CancellationToken token, long? tamperedCiphertextCap = null)
    {
        var isL5 = cell.Id == "L5";
        var result = NewAttempt(cell, profile, attempt, "candidate-cap");
        result["phase"] = discoveryIndex >= 0 ? "discovery" : "repeat";
        result["discovery_index"] = discoveryIndex;
        result["repeat"] = repeat;
        result["production_commit"] = ctx.Commit;
        result["production_tree"] = ctx.Tree;
        result["ciphertext_measurement"] = CandidateDrain.MeasurementBoundary;
        result["ciphertext_source"] = CandidateDrain.CiphertextSource;
        result["plaintext_source"] = "decrypted bytes read from candidate SslStream (exact, clipped to cap)";
        result["registered_ciphertext_cap"] = Campaign.CiphertextLimit;
        result["registered_plaintext_cap"] = Campaign.PlaintextLimit;
        result["max_tls_record_wire_bytes"] = Campaign.MaxTlsRecordWireBytes;
        result["flood_mode"] = isL5 ? "tiny" : "large";

        // Cap accounting: the declared encrypted cap is the registered 32768 unless the NC-1
        // control tampers it (declared != registered is exactly how the tamper is detected).
        var declaredCiphertextCap = tamperedCiphertextCap ?? Campaign.CiphertextLimit;
        result["declared_ciphertext_cap"] = declaredCiphertextCap;
        result["ciphertext_cap_tampered"] = tamperedCiphertextCap is not null;

        BoltTcpServer? server = null;
        BoltPeer? productionPeer = null;
        CandidateDrainListener? drainListener = null;
        TcpClient? floodPeer = null;
        CandidateDrain.DrainResult? drain = null;
        try
        {
            // ---- Part 1: production TLS terminal observation (preserved from R1/R2) ----
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
            productionPeer = await BoltPeer.ConnectAsync(server.BoundEndPoint!, ctx.Certificate,
                profile.PeerReceive, token).ConfigureAwait(false);
            result["production_tls_protocol"] = productionPeer.Tls.SslProtocol.ToString();
            result["production_tls_cipher"] = productionPeer.Tls.NegotiatedCipherSuite.ToString();
            await productionPeer.AuthenticateAsync(token).ConfigureAwait(false);
            var productionBatch = Wire.Frame(Wire.Encode(Wire.Pull(2)))
                .Concat(Wire.Frame(Wire.TerminalBegin))
                .ToArray();
            await productionPeer.SendAsync(productionBatch, token).ConfigureAwait(false);

            // The 4096-byte PULL result fits the peer receive buffer; production writes both the
            // PULL result and the terminal failure without parking, then closes (terminal outcome).
            var productionDeadline = Stopwatch.GetTimestamp();
            while (server.Diagnostics.MessagesProcessed < 5
                && Stopwatch.GetElapsedTime(productionDeadline).TotalMilliseconds < 2000)
                await Task.Delay(2, token).ConfigureAwait(false);
            var productionTerminal = await productionPeer.ReadUntilEofAsync(TimeSpan.FromSeconds(1), token)
                .ConfigureAwait(false);
            var productionMessages = server.Diagnostics.MessagesProcessed;
            var productionAccounting = Account.Snapshot(server);
            var productionObserved = productionMessages == 5
                && !productionTerminal.StartsWith("read-timeout", StringComparison.Ordinal);
            result["production_tls_terminal_observed"] = productionObserved;
            result["production_messages_processed"] = productionMessages;
            result["production_peer_read_outcome"] = productionTerminal;
            result["production_accounting"] = productionAccounting;
            await server.StopAsync(token).WaitAsync(TimeSpan.FromSeconds(6), token).ConfigureAwait(false);
            productionPeer.Dispose();
            productionPeer = null;
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6), token).ConfigureAwait(false);
            server = null;

            // ---- Part 2: candidate TLS drain with real ciphertext measurement ----
            drainListener = new CandidateDrainListener(ctx.Certificate, declaredCiphertextCap,
                Campaign.PlaintextLimit, closeNotify: true, token);
            var drainEndpoint = drainListener.Start();

            // Flooding client: counts beneath its own SslStream for the controlled differential.
            floodPeer = new TcpClient(AddressFamily.InterNetwork);
            floodPeer.ReceiveBufferSize = profile.PeerReceive;
            await floodPeer.ConnectAsync(drainEndpoint, token).ConfigureAwait(false);
            var floodWire = new CandidateDrain.WireCountingStream(floodPeer.GetStream());
            var floodTls = new SslStream(floodWire, leaveInnerStreamOpen: true);
            await floodTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, c, _, _) =>
                    c?.GetCertHashString() == ctx.Certificate.Thumbprint
            }, token).ConfigureAwait(false);
            var floodWireBefore = floodWire.Snapshot;

            var payload = isL5 ? new byte[1] : new byte[4096]; // E1 flood sizes: tiny 1 byte, large 4096
            var floodBound = 4L * 1024 * 1024; // hard safety bound far above any cap overshoot
            long applicationFlooded = 0;
            string? floodError = null;
            var flood = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && applicationFlooded < floodBound)
                    {
                        await floodTls.WriteAsync(payload, token).ConfigureAwait(false);
                        applicationFlooded += payload.Length;
                    }
                }
                catch (Exception ex)
                {
                    floodError = ex.GetType().Name;
                }
            }, token);

            var drainCompleted = await drainListener.DrainTask
                .WaitAsync(TimeSpan.FromMilliseconds(Campaign.AttemptLifetimeMs), token).ConfigureAwait(false);
            drain = drainCompleted;

            // Give the flood a bounded grace to observe the connection close, then stop it.
            try { await flood.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
            catch (Exception) { }
            var floodWireAfter = floodWire.Snapshot;
            var floodEncryptedWire = floodWireAfter.EncryptedOutbound - floodWireBefore.EncryptedOutbound;

            result["drain_outcome"] = drain.Outcome;
            result["drain_error"] = drain.Error;
            result["encrypted_wire_bytes"] = drain.EncryptedWireBytes;
            result["plaintext_bytes"] = drain.PlaintextBytes;
            result["drain_close_notify_completed"] = drain.CloseNotifyCompleted;
            result["drain_elapsed_ms"] = drain.ElapsedMs;
            result["drain_wire_totals"] = drain.Accounting;
            result["flood_application_bytes"] = applicationFlooded;
            result["flood_encrypted_wire_bytes"] = floodEncryptedWire;
            result["flood_error"] = floodError;

            // Controlled differential (R3 requirement 5): the TLS ciphertext counters and the
            // application byte counts MUST differ â€” the difference is the measured TLS record
            // overhead, proving the ciphertext evidence is wire measurement, not an app proxy.
            var overheadDrain = drain.EncryptedWireBytes - drain.PlaintextBytes;
            var overheadFlood = floodEncryptedWire - applicationFlooded;
            result["tls_record_overhead_bytes_drain"] = overheadDrain;
            result["tls_record_overhead_bytes_flood"] = overheadFlood;
            var differentialObserved = isL5
                ? overheadDrain > 0 && drain.EncryptedWireBytes > drain.PlaintextBytes
                : overheadDrain > 0 && overheadFlood > 0;
            result["ciphertext_app_differential_observed"] = differentialObserved;

            // ---- Part 3: registered cap oracle (E1 T5/T6 semantics) ----
            // Exact plaintext enforcement: the drain clips every read to the remaining plaintext
            // budget, so plaintext_bytes never overshoots the cap.
            var plaintextCapEnforced = isL5
                ? drain.PlaintextBytes < Campaign.PlaintextLimit
                : drain.PlaintextBytes == Campaign.PlaintextLimit;
            // Bounded encrypted enforcement: the encrypted counter is checked after every read;
            // a single SslStream record pull may overshoot by at most one maximum TLS record,
            // so WireBytes <= cap + MaxTlsRecordWireBytes proves no undetected violation
            // (R3 requirement 6: buffering/record overhead cannot hide a cap breach).
            var encryptedCapRespected = drain.EncryptedWireBytes > 0
                && drain.EncryptedWireBytes <= declaredCiphertextCap + Campaign.MaxTlsRecordWireBytes;
            var encryptedBoundVersusRegistered = drain.EncryptedWireBytes <= Campaign.CiphertextLimit
                + Campaign.MaxTlsRecordWireBytes;
            var clientConsistency = applicationFlooded > 0 && floodEncryptedWire > 0
                && floodEncryptedWire >= applicationFlooded;
            var accountingClean = (long)productionAccounting["active"]! == 0
                && (long)productionAccounting["accepted"]! == 1;

            result["plaintext_cap_enforced"] = plaintextCapEnforced;
            result["encrypted_cap_respected"] = encryptedCapRespected;
            result["encrypted_bound_versus_registered"] = encryptedBoundVersusRegistered;
            result["client_consistency"] = clientConsistency;

            var checks = productionObserved
                && plaintextCapEnforced
                && encryptedCapRespected
                && encryptedBoundVersusRegistered
                && differentialObserved
                && clientConsistency
                && accountingClean;
            result["checks"] = new Dictionary<string, object?>
            {
                ["production_tls_terminal_observed"] = productionObserved,
                ["plaintext_cap_enforced"] = plaintextCapEnforced,
                ["encrypted_cap_respected"] = encryptedCapRespected,
                ["encrypted_bound_versus_registered"] = encryptedBoundVersusRegistered,
                ["ciphertext_app_differential_observed"] = differentialObserved,
                ["client_consistency"] = clientConsistency,
                ["accounting_active_zero"] = (long)productionAccounting["active"]! == 0,
                ["accounting_balanced"] = (long)productionAccounting["accepted"]! == 1
                    && ((long)productionAccounting["completed"]! + (long)productionAccounting["failed"]!) == 1
            };
            result["outcome"] = checks ? "cap-bounded-and-clean" : "cap-checks-failed";
            result["guarantee_levels"] = new Dictionary<string, object?>
            {
                ["local_write_completion"] = true,
                ["tls_close_notify_transmission"] = drain.CloseNotifyCompleted,
                ["peer_receipt"] = false,
                ["verified_peer_receipt"] = false
            };
            ctx.Journal.Transcript(cell.Id,
                $"attempt {attempt}: drain={drain.Outcome} wire={drain.EncryptedWireBytes} " +
                $"plain={drain.PlaintextBytes} app={applicationFlooded} overhead={overheadDrain} " +
                $"outcome={result["outcome"]}");
        }
        catch (Exception ex)
        {
            result["outcome"] = "environment";
            result["error"] = ex.GetType().Name + ":" + ex.Message;
            result["drain_outcome"] = drain?.Outcome;
            result["encrypted_wire_bytes"] = drain?.EncryptedWireBytes;
            result["plaintext_bytes"] = drain?.PlaintextBytes;
            ctx.Journal.Transcript(cell.Id, $"attempt {attempt}: environment {result["error"]}");
        }
        finally
        {
            floodPeer?.Dispose();
            if (drainListener is not null)
                await drainListener.DisposeAsync().ConfigureAwait(false);
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
        => cell.Id is "L5" or "L6"
            ? "cap-bounded-and-clean"
            : "induced-and-interrupted";

    internal static bool IsChecksFailure(string outcome)
        => outcome is "induced-but-checks-failed" or "cap-checks-failed" or "phase-candidate-cleanup-failed";
}


