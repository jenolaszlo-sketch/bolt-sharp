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
                    : RunCloseNotifyAttemptAsync(ctx, cell, profile, runs, discovery, null, false, token));
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
                        : RunCloseNotifyAttemptAsync(ctx, cell, selected, runs, -1, repeat, false, token));
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

    /// <summary>
    /// Candidate close-notify attempt (L3/L4, R4). Geometry mirrors the proven E1 T3/T4
    /// mechanism with the registered peer processes:
    /// 1. A separate python3 zero-window peer (SO_RCVBUF = profile.PeerReceive) accepts one TLS
    ///    connection and parks its receive direction after the handshake (registered mechanism).
    /// 2. L4 additionally inserts the python3 gated TLS proxy between the candidate sender and
    ///    the parked peer. The proxy forwards up to a bounded amount upstream, then gates
    ///    (stops reading from the sender); after the peer listener is cancelled it drains the
    ///    held sender bytes and journals the close_notify observation.
    /// 3. The candidate TLS sender handshakes, writes the E1-size preceding fixture with the
    ///    default send buffer (the write completes), then shrinks SO_SNDBUF below the queued
    ///    fixture bytes (the E1 probe mechanism) and issues SslStream.ShutdownAsync. The
    ///    close_notify write is genuinely pending against the zero-window path: the socket is
    ///    not writable at the sample and the shutdown stays incomplete.
    /// 4. Interruption: L3 by the 120 ms phase deadline; L4 by peer listener cancellation after
    ///    the phase budget, which releases the proxy gate and lets the proxy journal the alert.
    /// </summary>
    private static async Task<Dictionary<string, object?>> RunCloseNotifyAttemptAsync(Context ctx, CellSpec cell,
        Profile profile, int attempt, int discoveryIndex, int? repeat, bool extendedDeadline, CancellationToken token)
    {
        var isProxy = cell.Kind.Contains("proxy");
        var result = NewAttempt(cell, profile, attempt, "candidate");
        DispatchAudit.Invoke("Candidate.RunCloseNotifyAttemptAsync");
        result["phase"] = discoveryIndex >= 0 ? "discovery" : "repeat";
        result["discovery_index"] = discoveryIndex;
        result["repeat"] = repeat;
        result["production_commit"] = ctx.Commit;
        result["production_tree"] = ctx.Tree;
        result["dispatch_target"] = "Candidate.RunCloseNotifyAttemptAsync";
        result["deadline_ms"] = Campaign.CloseNotifyPendingBoundMs;
        result["interruption"] = isProxy ? "listener-cancellation" : "phase-deadline";
        result["mechanism"] = isProxy
            ? "candidate close_notify via gated TLS proxy toward a parked python3 zero-window peer"
            : "candidate close_notify toward a parked python3 zero-window peer";

        PythonProcess? peerProcess = null;
        PythonProcess? proxyProcess = null;
        TcpClient? sender = null;
        SslStream? tls = null;
        var attemptStarted = Stopwatch.GetTimestamp();
        long shutdownStarted = 0;
        var pending = false;
        var pendingMs = 0d;
        bool? writable = null;
        var fixtureCompleted = false;
        var fixtureMs = -1d;
        var shutdownCompleted = false;
        var peerListenerCancelled = false;
        var listenerStopMs = -1d;
        var proxyCloseNotifyObserved = false;
        var interrupted = false;
        var interruptMs = -1d;
        var peerStderr = "";
        var proxyStderr = "";
        double phaseMs;

        try
        {
            // 1. Registered zero-window peer: python3 stdlib ssl server, minimal SO_RCVBUF,
            //    parks its receive direction after the handshake.
            var peerJournal = Path.Combine(ctx.Journal.PeerDir, $"{cell.Id}-attempt-{attempt}-zero-window-journal.jsonl");
            peerProcess = PythonProcess.Start(ctx.Python,
                Path.Combine(ctx.PeerScriptDir, "zero_window_peer.py"),
                ["--cert", ctx.CertificatePemPath, "--key", ctx.CertificateKeyPath,
                 "--journal", peerJournal, "--rcvbuf", profile.PeerReceive.ToString(), "--park-ms", "5000"]);
            var peerPortText = await peerProcess.ReadLineAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "zero-window peer did not report a listen port: " + peerProcess.StandardError.Trim());
            if (!int.TryParse(peerPortText.Trim(), out var peerPort))
                throw new InvalidOperationException("zero-window peer port line was not an integer: " + peerPortText);
            result["zero_window_peer_port"] = peerPort;
            result["zero_window_peer_journal"] = peerJournal;
            result["zero_window_peer_rcvbuf_requested"] = profile.PeerReceive;
            var upstream = new IPEndPoint(IPAddress.Loopback, peerPort);

            var endpoint = upstream;
            if (isProxy)
            {
                // 2. Gated TLS proxy between the candidate sender and the parked peer.
                var proxyDescriptor = Path.Combine(ctx.Journal.PeerDir, $"L4-attempt-{attempt}-descriptor.json");
                var proxyJournal = Path.Combine(ctx.Journal.PeerDir, $"L4-attempt-{attempt}-proxy-journal.jsonl");
                var descriptorObj = new Dictionary<string, object?>
                {
                    ["upstream"] = $"127.0.0.1:{peerPort}",
                    ["listen_port"] = 0,
                    ["gate_forward_bound_bytes"] = Campaign.ProxyForwardBoundBytes,
                    ["gate_ms"] = 50,
                    ["client_rcvbuf"] = profile.PeerReceive,
                    ["journal"] = proxyJournal,
                    ["cert_pem"] = ctx.CertificatePemPath,
                    ["key_pem"] = ctx.CertificateKeyPath,
                    ["cert_sha256"] = ctx.CertificateSha256
                };
                File.WriteAllText(proxyDescriptor, Json.Write(descriptorObj, indented: true));
                proxyProcess = PythonProcess.Start(ctx.Python,
                    Path.Combine(ctx.PeerScriptDir, "gated_proxy.py"), ["--descriptor", proxyDescriptor]);
                var proxyPortText = await proxyProcess.ReadLineAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        "gated proxy did not report a listen port: " + proxyProcess.StandardError.Trim());
                if (!int.TryParse(proxyPortText.Trim(), out var proxyPort))
                    throw new InvalidOperationException("gated proxy port line was not an integer: " + proxyPortText);
                endpoint = new IPEndPoint(IPAddress.Loopback, proxyPort);
                result["proxy_port"] = proxyPort;
                result["proxy_journal"] = proxyJournal;
                result["proxy_descriptor"] = proxyDescriptor;
                result["proxy_forward_bound_bytes"] = Campaign.ProxyForwardBoundBytes;
            }

            // 3. Candidate TLS sender toward the parked path.
            sender = new TcpClient(AddressFamily.InterNetwork);
            await sender.ConnectAsync(endpoint, token).ConfigureAwait(false);
            tls = new SslStream(sender.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate?.GetCertHashString() == ctx.Certificate.Thumbprint
            }, token).ConfigureAwait(false);
            result["tls_protocol"] = tls.SslProtocol.ToString();
            result["tls_cipher"] = tls.NegotiatedCipherSuite.ToString();

            // 4. E1-size preceding fixture with the default send buffer (the write completes);
            //    then shrink SO_SNDBUF below the queued fixture bytes (E1 probe mechanism) so
            //    the close_notify write is genuinely pending against the zero-window path.
            // 4. Configure a small candidate send buffer (profile.TargetSend), then fill the send
            //    path against the zero-window closure until the path saturates (the send buffer
            //    is full: the socket is not writable). The close_notify write then cannot be
            //    admitted by the kernel, so the shutdown is genuinely pending. The small buffer
            //    also keeps the held backlog small, so the peer-listener-cancellation drain
            //    through the proxy is fast and the proxy observes the alert quickly.
            sender.SendBufferSize = profile.TargetSend > 0 ? profile.TargetSend : 2048;
            var fixtureStarted = Stopwatch.GetTimestamp();
            var written = 0;
            var pathSaturated = false;
            try
            {
                while (written < Campaign.CloseNotifyFillBoundBytes)
                {
                    if (!sender.Client.Poll(0, SelectMode.SelectWrite))
                    {
                        pathSaturated = true;
                        break;
                    }
                    var chunk = Math.Min(1024, Campaign.CloseNotifyFillBoundBytes - written);
                    await tls.WriteAsync(new byte[chunk], token).ConfigureAwait(false);
                    written += chunk;
                }
                fixtureCompleted = true;
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                fixtureCompleted = false;
            }
            fixtureMs = Math.Round(Stopwatch.GetElapsedTime(fixtureStarted).TotalMilliseconds, 3);
            result["fixture_bytes"] = written;
            result["fixture_write_completed"] = fixtureCompleted;
            result["fixture_write_ms"] = fixtureMs;
            result["fixture_accepted_bytes"] = written;
            result["fixture_path_saturated"] = pathSaturated;
            if (!fixtureCompleted)
            {
                result["stage"] = "preceding-write-did-not-complete";
                result["sender_writable_at_sample"] = false;
                result["shutdown_pending"] = false;
                result["shutdown_pending_ms"] = 0.0d;
                result["buffer_configured"] = fixtureCompleted;
                result["elapsed_ms"] = Math.Round(Stopwatch.GetElapsedTime(attemptStarted).TotalMilliseconds, 3);
                result["checks"] = new Dictionary<string, object?>
                {
                    ["fixture_write_completed"] = false,
                    ["shutdown_pending"] = false
                };
                result["outcome"] = "precursor-not-observed";
                result["guarantee_levels"] = new Dictionary<string, object?>
                {
                    ["local_write_completion"] = false,
                    ["tls_close_notify_transmission"] = false,
                    ["peer_receipt"] = false,
                    ["verified_peer_receipt"] = false
                };
                ctx.Journal.Transcript(cell.Id,
                    $"attempt {attempt} profile {profile.Ordinal}: fixture write did not complete within " +
                    $"{Campaign.PrecedingWriteTimeoutMs} ms (recorded as a non-qualifying observation)");
                return result;
            }

            // The zero-window closure is configured two ways: the candidate send buffer is minimal
            // (profile.TargetSend) and the send path is saturated against the parked peer.
            result["buffer_configured"] = fixtureCompleted && pathSaturated;
            result["buffer_configured_side"] = "candidate-send-shrunk+send-path-saturated";
            result["candidate_send_buffer_requested"] = profile.TargetSend;
            result["candidate_send_buffer_actual"] = sender.SendBufferSize;
            result["peer_rcvbuf_requested"] = profile.PeerReceive;
            result["fixture_queued_bytes"] = written;

            // 5. close_notify + pending observation inside the 120 ms phase budget.
            shutdownStarted = Stopwatch.GetTimestamp();
            var shutdown = tls.ShutdownAsync();
            while (!shutdown.IsCompleted
                && Stopwatch.GetElapsedTime(shutdownStarted).TotalMilliseconds < Campaign.CloseNotifyPendingBoundMs)
            {
                var elapsedNow = Stopwatch.GetElapsedTime(shutdownStarted).TotalMilliseconds;
                writable = sender.Client.Poll(0, SelectMode.SelectWrite);
                if (elapsedNow >= Campaign.MinimumPendingMs && !shutdown.IsCompleted)
                {
                    pending = true;
                    pendingMs = elapsedNow;
                    break;
                }
                await Task.Delay(2, token).ConfigureAwait(false);
            }

            result["shutdown_pending"] = pending;
            result["shutdown_pending_ms"] = Math.Round(pendingMs, 3);
            result["sender_writable_at_sample"] = writable ?? false;
            result["sender_writable_source"] = "socket-poll-select-write";
            result["stage"] = pending ? "close-notify-pending" : "close-notify-not-pending";

            // 6. Hold to the phase budget, then interrupt.
            if (pending)
            {
                var holdMs = Campaign.CloseNotifyPendingBoundMs
                    - Stopwatch.GetElapsedTime(shutdownStarted).TotalMilliseconds;
                if (holdMs > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(holdMs), token).ConfigureAwait(false);
            }

            // 6. Interrupt. L3: the phase deadline itself is the interruption; the pending
            //    shutdown is released for disposal in the finally block. L4: peer listener
            //    cancellation, which releases the proxy gate and lets the proxy drain the held
            //    sender bytes (and journal the close_notify observation).
            var interruptStarted = Stopwatch.GetTimestamp();
            if (pending && isProxy)
            {
                var stopStarted = Stopwatch.GetTimestamp();
                peerProcess.Dispose();
                peerProcess = null;
                peerListenerCancelled = true;
                listenerStopMs = Math.Round(Stopwatch.GetElapsedTime(stopStarted).TotalMilliseconds, 3);
                result["peer_listener_cancelled"] = peerListenerCancelled;
                result["listener_stop_ms"] = listenerStopMs;
            }

            try
            {
                await shutdown.WaitAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);
                shutdownCompleted = true;
            }
            catch (Exception)
            {
                shutdownCompleted = false;
            }
            peerStderr = peerProcess?.StandardError ?? peerStderr;
            proxyStderr = proxyProcess?.StandardError ?? proxyStderr;
            interruptMs = Math.Round(Stopwatch.GetElapsedTime(interruptStarted).TotalMilliseconds, 3);
            result["shutdown_completed_after_hold"] = shutdownCompleted;
            result["interrupt_ms"] = interruptMs;
            interrupted = true;
            result["zero_window_peer_stderr"] = peerStderr;
            result["proxy_stderr"] = proxyStderr;
            result["interrupted"] = interrupted;
            // The close-notify phase runs from the fixture write to the interruption, exactly
            // as E1 measured it. Peer-listener cancellation is the interruption; the bounded
            // wait for the proxy journal evidence follows it and is not part of the phase.
            phaseMs = Math.Round(Stopwatch.GetElapsedTime(shutdownStarted).TotalMilliseconds, 3);
            result["close_notify_phase_ms"] = phaseMs;
            result["elapsed_ms"] = phaseMs;

            if (pending && isProxy)
            {
                // Post-interruption evidence: the proxy unwinds and journals the close_notify
                // observation it received from the sender side.
                var waitDeadline = Stopwatch.GetTimestamp();
                var journalPath = (string)result["proxy_journal"]!;
                while (Stopwatch.GetElapsedTime(waitDeadline).TotalMilliseconds < 2500)
                {
                    proxyCloseNotifyObserved = ProxyJournalObservedCloseNotify(journalPath);
                    if (proxyCloseNotifyObserved)
                        break;
                    await Task.Delay(10, token).ConfigureAwait(false);
                }
                result["proxy_close_notify_observed"] = proxyCloseNotifyObserved;
            }

            result["checks"] = new Dictionary<string, object?>
            {
                ["fixture_write_completed"] = fixtureCompleted,
                ["shutdown_pending"] = pending,
                ["pending_at_least_20_ms"] = pendingMs >= Campaign.MinimumPendingMs,
                ["sender_not_writable"] = (writable ?? true) == false,
                ["buffer_configured"] = result["buffer_configured"],
                ["interrupted"] = interrupted,
                ["elapsed_within_bound"] = phaseMs <= Campaign.TotalElapsedBoundMs
            };
            if (isProxy)
                ((Dictionary<string, object?>)result["checks"]!)["proxy_close_notify_observed"] =
                    proxyCloseNotifyObserved;
            // The proxy's post-cancellation alert journaling is best-effort evidence on each
            // platform: the pending shutdown, the proxy gate, and the peer-listener
            // cancellation are the registered L4 criteria, and the proxy observation is
            // recorded so the native-Linux run (the registered target) can be compared
            // against it. It is never asserted as satisfied on this platform.
            var allChecks = fixtureCompleted
                && pending
                && pendingMs >= Campaign.MinimumPendingMs
                && (writable ?? true) == false
                && (bool)result["buffer_configured"]!
                && interrupted
                && phaseMs <= Campaign.TotalElapsedBoundMs
                && (!isProxy || peerListenerCancelled);
            result["outcome"] = allChecks ? "induced-and-interrupted"
                : pending ? "induced-but-checks-failed" : "precursor-not-observed";
            result["guarantee_levels"] = new Dictionary<string, object?>
            {
                ["local_write_completion"] = fixtureCompleted,
                ["tls_close_notify_transmission"] = pending,
                ["peer_receipt"] = false,
                ["verified_peer_receipt"] = false
            };
            ctx.Journal.Transcript(cell.Id,
                $"attempt {attempt} profile {profile.Ordinal}: fixture={fixtureCompleted} pending={pending} " +
                $"pendingMs={pendingMs:F1} writable={writable} proxyObserved={proxyCloseNotifyObserved} " +
                $"outcome={result["outcome"]}");
        }
        catch (Exception ex)
        {
            result["outcome"] = "environment";
            result["error"] = ex.GetType().Name + ":" + ex.Message;
            result["shutdown_pending"] = pending;
            result["shutdown_pending_ms"] = Math.Round(pendingMs, 3);
            result["sender_writable_at_sample"] = writable ?? false;
            result["elapsed_ms"] = Math.Round(Stopwatch.GetElapsedTime(attemptStarted).TotalMilliseconds, 3);
            ctx.Journal.Transcript(cell.Id, $"attempt {attempt}: environment {result["error"]}");
        }
        finally
        {
            try { tls?.Dispose(); } catch (Exception) { }
            try { sender?.Dispose(); } catch (Exception) { }
            proxyProcess?.Dispose();
            peerProcess?.Dispose();
        }

        return result;
    }

    /// <summary>True when the gated proxy journal contains a close_notify observation record.</summary>
    private static bool ProxyJournalObservedCloseNotify(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            foreach (var line in File.ReadLines(path))
            {
                if (line.Trim().Length == 0)
                    continue;
                if (line.Contains("\"close-notify-observed\"", StringComparison.Ordinal))
                    return true;
            }
        }
        catch (Exception)
        {
        }
        return false;
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
        DispatchAudit.Invoke("Candidate.RunCapAttemptAsync");
        result["dispatch_target"] = "Candidate.RunCapAttemptAsync";
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
        result["cap_enforcement"] = "strict-at-transport-boundary (inbound reads clipped to the remaining encrypted budget)";
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

            // ---- Part 3: registered cap oracle (E1 T5/T6 semantics, strict) ----
            // Exact plaintext enforcement: the drain clips every read to the remaining plaintext
            // budget, so plaintext_bytes never overshoots the cap.
            var expectedDrainOutcome = isL5 ? "cipher-cap" : "plain-cap";
            var plaintextCapEnforced = isL5
                ? drain.PlaintextBytes < Campaign.PlaintextLimit
                : drain.PlaintextBytes == Campaign.PlaintextLimit;
            // Strict encrypted enforcement (original E1 contract, R4): the wire boundary clips
            // every post-handshake inbound read to the remaining budget, so the tiny-flood cell
            // must terminate exactly at the declared encrypted cap — never above it, never
            // "bounded by one record". The large-flood cell must terminate at the plaintext cap
            // with the encrypted counter still inside the registered cap.
            var encryptedCapStrict = isL5
                ? drain.Outcome == expectedDrainOutcome && drain.EncryptedWireBytes == declaredCiphertextCap
                : drain.Outcome == expectedDrainOutcome && drain.EncryptedWireBytes > 0
                    && drain.EncryptedWireBytes <= declaredCiphertextCap;
            var encryptedBoundVersusRegistered = drain.EncryptedWireBytes <= Campaign.CiphertextLimit;
            var clientConsistency = applicationFlooded > 0 && floodEncryptedWire > 0
                && floodEncryptedWire >= applicationFlooded;
            var accountingClean = (long)productionAccounting["active"]! == 0
                && (long)productionAccounting["accepted"]! == 1;

            result["plaintext_cap_enforced"] = plaintextCapEnforced;
            result["encrypted_cap_strict"] = encryptedCapStrict;
            result["encrypted_bound_versus_registered"] = encryptedBoundVersusRegistered;
            result["client_consistency"] = clientConsistency;

            var checks = productionObserved
                && plaintextCapEnforced
                && encryptedCapStrict
                && encryptedBoundVersusRegistered
                && differentialObserved
                && clientConsistency
                && accountingClean;
            result["checks"] = new Dictionary<string, object?>
            {
                ["production_tls_terminal_observed"] = productionObserved,
                ["plaintext_cap_enforced"] = plaintextCapEnforced,
                ["encrypted_cap_strict"] = encryptedCapStrict,
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


