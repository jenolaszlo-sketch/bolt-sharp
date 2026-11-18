#!/usr/bin/env python3
"""Q-B5B-09-PLAIN-Q2 integrated negative-control runner (R1 target).

Applies each fault mutation to the isolated R1 worktree, runs the detecting
canonical test filter, records the detection, and restores the original bytes.
"""

import subprocess
import sys
from pathlib import Path

ROOT = Path(r"C:\Windows\Temp\opencode\q2w\r1")

CONTROLS = [
    ("NC-01", "Trailing-command execution (R04)", "PlaintextTerminalCloseTests.ExactFailureAndNoTrailingExecution", [
    ("src/BoltSharp/Sessions/BoltConnectionSession.cs",
     "            var refusal = FailTerminal(UnsupportedMessageFailure());\n"
     "            return authenticated ? refusal.MarkPlaintextCloseEligible() : refusal;",
     "            var refusal = FailTerminal(UnsupportedMessageFailure());\n"
     "            if (authenticated)\n"
     "                _state = BoltConnectionState.Ready;\n"
     "            return authenticated ? refusal.MarkPlaintextCloseEligible() : refusal;"),
    ("src/BoltSharp/Transport/BoltTcpConnection.cs",
     "                return await plaintextClose.RunAsync(client, plaintext, connectionId, options.WriteTimeout,\n"
     "                    (cancellation, pending) => WritePlaintextTerminalResponseAsync(\n"
     "                        plaintext, codec, result.Responses, cancellation, pending),",
     "                return await plaintextClose.RunAsync(client, plaintext, connectionId, options.WriteTimeout,\n"
     "                    async (cancellation, pending) => {\n"
     "                        await WritePlaintextTerminalResponseAsync(\n"
     "                            plaintext, codec, result.Responses, cancellation, pending).ConfigureAwait(false);\n"
     "                        if (await reader.ReadMessageAsync(cancellation).ConfigureAwait(false) is { } queued)\n"
     "                            _ = session.ReceiveBytes(queued);\n"
     "                    },"),
    ]),
    ("NC-02", "Socket retention after shutdown (R09/R10)", "PlaintextTerminalCloseTests.ListenerStopJoinsActualHandleAndReturnsPermit", [
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "            if (Interlocked.Exchange(ref _closed, 1) == 0)\n                Client.Dispose();",
     "            if (Interlocked.Exchange(ref _closed, 1) == 0)\n                GC.KeepAlive(Client);"),
    ("src/BoltSharp/Transport/BoltTcpConnection.cs",
     "        using var clientOwnership = client;",
     "        var clientOwnership = client;"),
    ]),
    ("NC-03", "Unlinked cancellation (R06)", "PlaintextTerminalCloseTests.ListenerStopJoinsActualHandleAndReturnsPermit", [
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "            using var onListener = listener.UnsafeRegister(_ => owner.Interrupt(\"listener-shutdown\"), null);",
     "            // Fault injection: leave the listener unregistered."),
    ]),
    ("NC-04", "Deadline renewal (R05)", "PlaintextTerminalCloseTests.OneDeadlineCoversWriteAndDrain", [
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "            phase = \"shutdown\";\n            ArmPhaseTimer(drain, DrainBudget, started, owner);",
     "            phase = \"shutdown\";\n            total.CancelAfter(TotalBudget);\n            ArmPhaseTimer(drain, DrainBudget, Stopwatch.GetTimestamp(), owner);"),
    ]),
    ("NC-05", "Drain byte-cap violation (R08)", "PlaintextTerminalCloseTests.ExactFailureAndNoTrailingExecution", [
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "    private const int DrainByteLimit = 8 * 1024;",
     "    private const int DrainByteLimit = 12 * 1024;"),
    ]),
    ("NC-06", "Drain concurrency violation (R09)", "PlaintextTerminalCloseTests.DrainAdmissionAndRecovery", [
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "        _permits = new SemaphoreSlim(Math.Min(1, maxConnections - 1), 1);",
     "        _permits = new SemaphoreSlim(Math.Min(2, maxConnections - 1), 2);"),
    ]),
    ("NC-07", "TLS entering plaintext closure (R12)", "PlaintextTerminalCloseTests.TlsRefusalNeverEntersPlaintextHelper", [
    ("src/BoltSharp/Transport/BoltTcpServer.cs",
     "        _plaintextClose = options.Tls is null ? new BoltPlaintextTerminalClose(options.MaxConnections) : null;",
     "        _plaintextClose = new BoltPlaintextTerminalClose(options.Tls is null ? options.MaxConnections : 1);"),
    ("src/BoltSharp/Transport/BoltTcpConnection.cs",
     "                    options.Tls is null ? plaintextClose : null,",
     "                    plaintextClose,"),
    ("src/BoltSharp/Transport/BoltTcpConnection.cs",
     "                return await plaintextClose.RunAsync(client, plaintext, connectionId, options.WriteTimeout,",
     "                return await plaintextClose.RunAsync(client, client.GetStream(), connectionId, options.WriteTimeout,"),
    ]),
    ("NC-09", "Added: drain-permit leak (resource-lifetime oracle)", "PlaintextTerminalCloseTests", [
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "            if (admitted)\n                _permits.Release();",
     "            if (admitted && false)\n                _permits.Release();"),
    ]),
]

NC08 = [
    ("src/BoltSharp/Transport/BoltTcpConnection.cs",
     "                return await plaintextClose.RunAsync(client, plaintext, connectionId, options.WriteTimeout,\n"
     "                    (cancellation, pending) => WritePlaintextTerminalResponseAsync(\n"
     "                        plaintext, codec, result.Responses, cancellation, pending),",
     "                return await plaintextClose.RunAsync(client, plaintext, connectionId, options.WriteTimeout,\n"
     "                    async (cancellation, pending) => {\n"
     "                        await WritePlaintextTerminalResponseAsync(\n"
     "                            plaintext, codec, result.Responses, cancellation, pending).ConfigureAwait(false);\n"
     "                        diagnostics.Report(BoltTransportEventKind.MessageProcessed, connectionId,\n"
     "                            $\"sig={DescribePayloadSignature(message)} responses={result.Responses.Count} state={result.State}\");\n"
     "                    },"),
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "                await writeResponse(write.Token, () => Report(\"write-pending\")).ConfigureAwait(false);",
     "                await writeResponse(owner.Token, () => Report(\"write-pending\")).ConfigureAwait(false);"),
    ("src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
     "                    if (Interlocked.CompareExchange(ref writeInterruption, cause, null) is null)\n                        write.Cancel();",
     "                    if (Interlocked.CompareExchange(ref writeInterruption, cause, null) is null)\n                        owner.Interrupt(cause);"),
]


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True)


def apply_mutation(path, old, new):
    p = ROOT / path
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"MUTATION TARGET NOT FOUND in {path}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


def restore():
    git("checkout", "--", "src/")


def main():
    logs = ROOT.parent / "nc-logs"
    logs.mkdir(parents=True, exist_ok=True)
    summary = []
    for name, title, filt, edits in CONTROLS:
        restore()
        for path, old, new in edits:
            apply_mutation(path, old, new)
        logfile = logs / f"{name.lower()}.log"
        with open(logfile, "w", encoding="utf-8") as fh:
            proc = subprocess.run(
                ["dotnet", "test", "tests/BoltSharp.Tests/BoltSharp.Tests.csproj",
                 "-c", "Debug", "--filter", f"FullyQualifiedName~{filt}",
                 "--logger", "trx;LogFileName=" + name.lower() + ".trx", "-v", "n"],
                cwd=ROOT, stdout=fh, stderr=subprocess.STDOUT, text=True)
        text = logfile.read_text(encoding="utf-8", errors="replace")
        failed = [l.strip() for l in text.splitlines() if l.strip().startswith("Failed ")]
        passed_line = [l.strip() for l in text.splitlines() if "Passed!" in l or "Failed!" in l]
        summary.append((name, title, proc.returncode, failed, passed_line[:1]))
        restore()
        status = git("status", "--porcelain")
        if status.stdout.strip():
            print(f"WARNING: {name} left a dirty tree")
    # NC-08 (F01 write-timeout ownership reintroduction) uses its multi-edit list.
    restore()
    for path, old, new in NC08:
        apply_mutation(path, old, new)
    logfile = logs / "nc-08.log"
    with open(logfile, "w", encoding="utf-8") as fh:
        proc = subprocess.run(
            ["dotnet", "test", "tests/BoltSharp.Tests/BoltSharp.Tests.csproj",
             "-c", "Debug", "--filter", "FullyQualifiedName~PlaintextTerminalCloseTests",
             "--logger", "trx;LogFileName=nc-08.trx", "-v", "n"],
            cwd=ROOT, stdout=fh, stderr=subprocess.STDOUT, text=True)
    text = logfile.read_text(encoding="utf-8", errors="replace")
    failed = [l.strip() for l in text.splitlines() if l.strip().startswith("Failed ")]
    passed_line = [l.strip() for l in text.splitlines() if "Passed!" in l or "Failed!" in l]
    summary.append(("NC-08", "Reintroduction of the original F01 write-timeout ownership defect",
                    proc.returncode, failed, passed_line[:1]))
    restore()
    print("=== Q2 NEGATIVE CONTROL CAMPAIGN (R1 target) ===")
    for name, title, code, failed, line in summary:
        verdict = "DETECTED" if code != 0 else "NOT DETECTED"
        print(f"{name}: {verdict} | {title}")
        for f in failed[:6]:
            print(f"    {f}")
        for l in line:
            print(f"    {l}")
    restore()
    print("final tree status:", git("status", "--porcelain").stdout.strip() or "clean")


if __name__ == "__main__":
    sys.exit(main())