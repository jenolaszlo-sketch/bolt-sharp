"""Verify committed Q-B5B-09-TLS-E1 evidence, journals, and bounded outcomes."""

from collections import Counter
import hashlib
import json
import subprocess
import sys

PACKAGE = "docs/reviews/evidence/Q-B5B-09-TLS-E1"
REPORT = "docs/reviews/Q-B5B-09-TLS-E1-report.md"
MANIFEST = f"{PACKAGE}/manifest.json"
PARENT = "250fb99273f975ae6552eb4e0ebefcc149796fa7"
PARENT_TREE = "bb69f2e3986dbae6a5db705c20690cb9f1cb9568"
HASH_CONVENTION = "SHA-256 over exact Git blob payload bytes"
CELLS = ("T1", "T2", "T3", "T4", "T5", "T6")
QUALIFYING_OUTCOMES = {"induced-and-interrupted", "cap-bounded-and-clean"}
TERMINAL_PENDING_BOUND_MS = 310
CLOSE_NOTIFY_PENDING_BOUND_MS = 120

PACKAGE_NAMES = {
    ".gitattributes",
    "README.md",
    "TlsE1MechanismTests.cs.txt",
    "TlsE1Tests.cs.txt",
    "ProductionTlsCloseTests.cs.txt",
    "commissioning-budget-reconciliation.json",
    "commissioning-matrix.json",
    "configuration.json",
    "manifest.json",
    "matrix.json",
    "nc1-cipher-cap-tamper.jsonl",
    "nc2-deadline-renewal.jsonl",
    "run-wsl2-candidate-repeat.sh",
    "run-wsl2-candidate.sh",
    "run-wsl2-production-repeat.sh",
    "run-wsl2-production.sh",
    "source-identity.json",
    "verify-evidence.py",
    "windows-campaign-first-run.jsonl",
    "windows-campaign.jsonl",
    "windows-mechanism.jsonl",
    "windows-production.jsonl",
    "wsl2-campaign-repeat.jsonl",
    "wsl2-campaign.jsonl",
    "wsl2-mechanism.jsonl",
    "wsl2-production-repeat.jsonl",
    "wsl2-production.jsonl",
}
TRANSCRIPT_NAMES = {
    "nc1-cipher-cap-tamper.log",
    "nc2-deadline-renewal.log",
    "wsl2-campaign-repeat.log",
    "wsl2-campaign.log",
    "wsl2-production-repeat.log",
    "wsl2-production.log",
    "windows-campaign.log",
    "windows-mechanism.log",
    "windows-production.log",
}
NAMES = PACKAGE_NAMES | {f"transcripts/{name}" for name in TRANSCRIPT_NAMES}


def git(*args):
    return subprocess.check_output(["git", *args])


def check(condition, message):
    if not condition:
        raise ValueError(message)


def parse_jsonl(data):
    text = data.decode("utf-8")
    check(text.endswith("\n"), "JSONL journal does not end with a newline")
    return [json.loads(line) for line in text.splitlines() if line]


def check_platform(row, source, platform):
    identity = source["platforms"][platform]
    check(row.get("platform") == identity["os"], f"Unexpected {platform} platform identity")
    check(row.get("runtime") == identity["runtime"], f"Unexpected {platform} runtime identity")


def expected_profile(config, cell, ordinal):
    if cell in ("T5", "T6"):
        profile = config["cap_profile"]
    else:
        matches = [item for item in config["profiles"] if item["ordinal"] == ordinal]
        check(len(matches) == 1, "Unknown or duplicate discovery profile")
        profile = matches[0]
    return {
        "Ordinal": profile["ordinal"],
        "PrecedingBytes": profile["preceding_fixture_bytes"],
        "PeerReceive": profile["requested_peer_receive_bytes"],
        "TargetSend": profile["requested_target_send_bytes"],
    }


def check_profile(start, config, cell):
    profile = start.get("profile")
    check(isinstance(profile, dict), "Missing campaign profile")
    expected = expected_profile(config, cell, profile.get("Ordinal"))
    check(profile == expected, f"Unexpected profile for {cell}: {profile}")


def check_cleanup(result):
    check(result.get("Closed") is True, "Connection socket was not closed")
    accounting = result.get("Accounting")
    check(isinstance(accounting, dict), "Missing connection accounting")
    check(accounting.get("Active") == 0, "Connection remained active")
    check(accounting.get("Accepted") == accounting.get("Completed", 0) + accounting.get("Failed", 0),
          "Connection accounting is not balanced")
    check(result.get("Permits") == result.get("ReleasedPermits"),
          "Drain permits were not balanced")


def parse_campaign(data, cells, config, source, platform):
    rows = parse_jsonl(data)
    check(rows and rows[-1].get("kind") == "campaign-end", "Campaign journal has no terminal record")
    check_platform(rows[-1], source, platform)
    check(rows[-1].get("defect") is False, "Final campaign records a defect")
    check(rows[-1].get("unobservedTaskExceptions") == 0, "Unobserved task exception recorded")

    summaries = []
    current_cell = None
    pending = None
    seen = []

    for row in rows[:-1]:
        kind = row.get("kind")
        if kind == "started":
            cell = row.get("cell")
            check(cell in cells, f"Unknown campaign cell {cell}")
            check(current_cell in (None, cell), "Campaign cell ended without cell-end")
            check(pending is None, f"Unmatched campaign start: {row}")
            if current_cell != cell:
                current_cell = cell
                seen.append(cell)
                summaries.append({"cell": cell, "runs": []})
            check_profile(row, config, cell)
            pending = row
        elif kind == "completed":
            check(pending is not None, "Completed record has no start")
            check(row.get("cell") == pending.get("cell"), "Completed cell differs from start")
            check(row.get("run") == pending.get("run"), "Completed run differs from start")
            check(row.get("repeat") == pending.get("repeat"), "Completed repeat differs from start")
            result = row.get("result")
            check(isinstance(result, dict), "Missing campaign result")
            check(result.get("Error") is None, "Campaign result contains an error")
            check(result.get("ElapsedMs", 1e30) <= config["maximum_attempt_lifetime_ms"],
                  "Campaign attempt exceeded the lifetime watchdog")
            check_cleanup(result)
            pending["result"] = result
            pending["completed"] = row
            summary = next(item for item in summaries if item["cell"] == current_cell)
            summary["runs"].append(pending)
            pending = None
        elif kind == "cell-end":
            check(current_cell == row.get("cell"), "cell-end names the wrong cell")
            check(pending is None, f"cell-end follows an unmatched start: {row}")
            cell = row.get("cell")
            summary = next(item for item in summaries if item["cell"] == cell)
            check(len(summary["runs"]) > 0, f"Empty cell summary for {cell}")
            check(row.get("runs") == len(summary["runs"]), "cell-end run count differs")
            check(row.get("successes") == (3 if row.get("qualified") else 0),
                  "cell-end success count differs")
            summary["end"] = row
            current_cell = None
        else:
            raise ValueError(f"Unknown campaign event {kind}")

    check(current_cell is None and pending is None, "Campaign ended with an open cell or run")
    check(tuple(seen) == tuple(cells), "Campaign cell order differs")
    check(len(summaries) == len(cells), "Campaign cell count differs")

    for summary in summaries:
        cell = summary["cell"]
        runs = summary["runs"]
        discovery = [run for run in runs if "repeat" not in run]
        repeats = [run for run in runs if "repeat" in run]
        check(len(discovery) <= 4, f"Discovery bound exceeded in {cell}")
        check(len(runs) <= (3 if cell in ("T5", "T6") else 6), f"Cell run bound exceeded in {cell}")
        discovery_ordinals = [run["profile"]["Ordinal"] for run in discovery]
        check(discovery_ordinals == list(range(1, len(discovery) + 1)),
              f"Discovery profile order differs in {cell}")
        check([run["repeat"] for run in repeats] == list(range(1, len(repeats) + 1)),
              f"Repeat order differs in {cell}")
        for run in repeats:
            check(run["profile"]["Ordinal"] in discovery_ordinals,
                  f"Repeat used an undiscovered profile in {cell}")
        if summary["end"].get("qualified"):
            check(len(repeats) == 2, f"Qualified cell lacks two repeats in {cell}")
            qualifying_discovery = [run for run in discovery
                                     if run["result"]["Outcome"] in QUALIFYING_OUTCOMES]
            check(qualifying_discovery, f"Qualified cell lacks a qualifying discovery in {cell}")
            selected = qualifying_discovery[-1]
            check(all(run["profile"]["Ordinal"] == selected["profile"]["Ordinal"] for run in repeats),
                  f"Repeats did not use the selected profile in {cell}")
            check(all(run["result"]["Outcome"] in QUALIFYING_OUTCOMES for run in repeats),
                  f"Repeat failed in {cell}")
        else:
            check(not repeats, f"Unqualified cell contains repeats in {cell}")
        summary["discovery"] = discovery
        summary["repeats"] = repeats
    return summaries


def check_candidate_result(cell, result, config):
    outcome = result.get("Outcome")
    check(outcome in QUALIFYING_OUTCOMES | {
        "preceding-write-did-not-complete",
        "target-reached-no-persistent-pending-write",
    }, f"Unexpected candidate outcome {outcome}")
    stages = result.get("Stages", [])
    if outcome == "induced-and-interrupted":
        check(cell in ("T1", "T2", "T3", "T4"), "Pending outcome used by the wrong cell")
        check(result.get("Confirmed") is True, "Pending write was not confirmed")
        check(result.get("PendingMs", 0) >= config["minimum_pending_observation_ms"],
              "Pending observation was too short")
        check(result.get("SenderWritableAtSample") is False, "Sender was writable at the pending sample")
        check(result.get("PeerUnreadAtSample", 0) > 0, "Peer was not parked at the pending sample")
        check(result.get("BufferConfigured") is True, "Send buffer was not configured")
        check("kernel-write-pending" in stages, "Pending stage was not observed")
        check(result.get("ParserEntries") == 5, "Unexpected parser entry count")
        check(result.get("TerminalResults") == 1, "Unexpected terminal result count")
        check(result.get("TerminalResult") == "Defunct:1", "Unexpected terminal state")
        check(result.get("ProviderPreparations") == 1, "Unexpected provider preparation count")
        expected = {
            "T1": ("terminal-write", "total-deadline", False, "total-deadline"),
            "T2": ("terminal-write", "server-cancelled", True, "server-cancelled"),
            "T3": ("close-notify", "phase-deadline", False, "phase-deadline"),
            "T4": ("close-notify", "server-cancelled", True, "server-cancelled"),
        }[cell]
        stage, finish_outcome, listener_pending, inner_outcome = expected
        finish = result.get("Finish")
        check(isinstance(finish, dict) and finish.get("Phase") in ("write", "shutdown"),
              "Missing pending-write finish")
        check(finish.get("Outcome") == finish_outcome, f"Unexpected finish outcome in {cell}")
        inner = result.get("InnerWrites")
        check(isinstance(inner, list) and len(inner) == 1, f"Unexpected inner-write count in {cell}")
        check(inner[0].get("Stage") == stage and inner[0].get("Status") == "Faulted",
              f"Unexpected inner write in {cell}")
        if inner_outcome == "total-deadline":
            check(inner[0].get("ElapsedMs", 1e30) <= TERMINAL_PENDING_BOUND_MS,
                  "Terminal write exceeded its pending lifetime bound")
            check(finish.get("ElapsedMs", 1e30) <= TERMINAL_PENDING_BOUND_MS,
                  "Terminal write finish exceeded its bound")
        else:
            check(inner[0].get("ElapsedMs", 1e30) <= CLOSE_NOTIFY_PENDING_BOUND_MS,
                  "Close notification exceeded its pending lifetime bound")
            check(finish.get("ElapsedMs", 1e30) <= CLOSE_NOTIFY_PENDING_BOUND_MS,
                  "Close notification finish exceeded its bound")
        check(result.get("PendingAtListener") is listener_pending, f"Wrong listener cause in {cell}")
        if listener_pending:
            check(result.get("ListenerMs") is not None, f"Missing listener time in {cell}")
        else:
            check(result.get("ListenerMs") is None, f"Unexpected listener time in {cell}")
        check(result.get("ElapsedMs", 1e30) <= config["total_elapsed_bound_ms"],
              f"Qualified attempt exceeded total elapsed bound in {cell}")
    elif outcome == "cap-bounded-and-clean":
        check(cell in ("T5", "T6"), "Cap outcome used by the wrong cell")
        finish = result.get("Finish")
        check(isinstance(finish, dict) and finish.get("Phase") == "drain", "Missing cap finish")
        if cell == "T5":
            check(finish.get("Outcome") == "cipher-cap", "T5 did not hit the encrypted-byte cap")
            check(finish.get("WireBytes") == config["candidate_limits"]["ciphertext"],
                  "T5 encrypted-byte cap differs")
            check(finish.get("Bytes") == 1441, "T5 decrypted-byte observation differs")
        else:
            check(finish.get("Outcome") == "byte-cap", "T6 did not hit the plaintext-byte cap")
            check(finish.get("Bytes") == config["candidate_limits"]["plaintext"],
                  "T6 plaintext-byte cap differs")
            check(finish.get("WireBytes") <= config["candidate_limits"]["ciphertext"],
                  "T6 exceeded the encrypted-byte cap")
        check(result.get("ElapsedMs", 1e30) <= config["total_elapsed_bound_ms"],
              "Cap attempt exceeded total elapsed bound")
    elif outcome == "preceding-write-did-not-complete":
        check(cell in ("T1", "T2", "T3", "T4"), "Preceding-write outcome used by wrong cell")
        check(result.get("Confirmed") is False and result.get("Finish") is None,
              "Preceding-write result was not a non-qualifying observation")
        check("kernel-write-pending" not in stages, "Preceding-write result reached the target")
    else:
        check(outcome == "target-reached-no-persistent-pending-write",
              f"Unexpected candidate outcome {outcome}")
        check(cell in ("T1", "T2", "T3", "T4"), "Non-pending outcome used by wrong cell")
        check(result.get("Confirmed") is False and result.get("Finish") is not None,
              "Non-pending target result is inconsistent")
        check("kernel-write-pending" not in stages, "Non-pending result contains a pending stage")


def check_final_campaign(summaries, matrix, platform):
    rows = {row["cell"]: row for row in matrix["cells"]}
    check(tuple(rows) == CELLS, "Matrix cell order differs")
    expected = {
        "windows": {
            "T1": (6, 5, True), "T2": (6, 5, True), "T3": (6, 5, True),
            "T4": (6, 5, True), "T5": (3, 3, True), "T6": (3, 3, True),
        },
        "wsl2": {
            "T1": (6, 3, True), "T2": (6, 3, True), "T3": (4, 0, False),
            "T4": (4, 0, False), "T5": (3, 3, True), "T6": (3, 3, True),
        },
    }[platform]
    for summary in summaries:
        cell = summary["cell"]
        outcomes = [run["result"]["Outcome"] for run in summary["runs"]]
        passed = sum(outcome in QUALIFYING_OUTCOMES for outcome in outcomes)
        qualified = summary["end"].get("qualified") is True
        row = rows[cell]
        check(len(summary["runs"]) == row[f"{platform}_runs"], f"Matrix run count differs in {cell}")
        check(passed == row[f"{platform}_passed"], f"Matrix pass count differs in {cell}")
        check(qualified == row[f"{platform}_qualified"], f"Matrix qualification differs in {cell}")
        check((len(summary["runs"]), passed, qualified) == expected[cell],
              f"Unexpected final matrix result in {cell}")
        check(summary["end"].get("defect") == "", f"Cell defect recorded in {cell}")
        for run in summary["runs"]:
            check_candidate_result(cell, run["result"], CONFIG)


def check_first_run(data, config, source):
    rows = parse_jsonl(data)
    check(rows[-1].get("kind") == "campaign-end", "First-run journal has no terminal record")
    check(rows[-1].get("defect") is True, "First run did not preserve the oracle mismatch")
    check(rows[-1].get("unobservedTaskExceptions") == 0, "First run observed a task exception")
    check_platform(rows[-1], source, "windows")
    cell_ends = [row for row in rows if row.get("kind") == "cell-end"]
    check([row["cell"] for row in cell_ends] == ["T1", "T2", "T3"], "First-run cells differ")
    check(cell_ends[0].get("qualified") and cell_ends[1].get("qualified"), "First-run qualification differs")
    check(not cell_ends[2].get("qualified") and cell_ends[2].get("defect") == "induced-but-checks-failed",
          "First-run T3 correction record differs")
    completed = [row for row in rows if row.get("kind") == "completed"]
    for row in completed:
        check_cleanup(row["result"])
    t3 = next(row for row in completed if row["cell"] == "T3")
    result = t3["result"]
    check(result["Outcome"] == "induced-but-checks-failed", "First-run T3 outcome differs")
    check(result["Finish"]["Phase"] == "shutdown" and result["Finish"]["Outcome"] == "phase-deadline",
          "First-run T3 did not preserve the corrected phase-deadline observation")
    check(result["InnerWrites"][0]["Stage"] == "close-notify", "First-run T3 stage differs")


def check_negative_controls(data, config, source, name):
    rows = parse_jsonl(data)
    check(rows[-1].get("kind") == "campaign-end", f"{name} has no terminal record")
    check(rows[-1].get("defect") is True, f"{name} did not trip its control oracle")
    check(rows[-1].get("unobservedTaskExceptions") == 0, f"{name} observed a task exception")
    check_platform(rows[-1], source, "windows")
    ends = [row for row in rows if row.get("kind") == "cell-end"]
    completed = [row for row in rows if row.get("kind") == "completed"]
    for row in completed:
        check_cleanup(row["result"])
    if name == "nc1-cipher-cap-tamper":
        check([row["cell"] for row in ends] == ["T1", "T2", "T3", "T4", "T5"], "NC-1 cells differ")
        check(all(row.get("qualified") for row in ends[:4]), "NC-1 pre-cap cells differ")
        t5_end = ends[4]
        check(not t5_end.get("qualified") and t5_end.get("defect") == "cap-checks-failed",
              "NC-1 did not detect the cap tamper")
        t5 = next(row for row in completed if row["cell"] == "T5")["result"]
        check(t5["Outcome"] == "cap-checks-failed", "NC-1 T5 outcome differs")
        check(t5["Finish"]["Outcome"] == "cipher-cap", "NC-1 T5 did not reach the cap stage")
        check(t5["Finish"]["WireBytes"] == 131072, "NC-1 tampered wire count differs")
        check(t5["Finish"]["WireBytes"] > config["candidate_limits"]["ciphertext"],
              "NC-1 did not exceed the declared encrypted-byte cap")
    else:
        check(name == "nc2-deadline-renewal", "Unknown negative control")
        check(len(ends) == 1 and ends[0]["cell"] == "T1", "NC-2 cell differs")
        check(not ends[0].get("qualified") and ends[0].get("defect") == "induced-but-checks-failed",
              "NC-2 did not detect the deadline tamper")
        result = completed[0]["result"]
        check(result["Outcome"] == "induced-but-checks-failed", "NC-2 outcome differs")
        check(result["Finish"]["Outcome"] == "total-deadline", "NC-2 finish differs")
        check(result["Finish"]["ElapsedMs"] > config["total_elapsed_bound_ms"],
              "NC-2 did not exceed the total elapsed bound")
        check(result["InnerWrites"][0]["ElapsedMs"] > TERMINAL_PENDING_BOUND_MS,
              "NC-2 did not exceed the terminal pending lifetime bound")


def check_mechanism(data, source, platform):
    rows = parse_jsonl(data)
    check(rows[0].get("kind") == "mechanism-start", "Mechanism journal has no start")
    check(rows[-1].get("kind") == "mechanism-end", "Mechanism journal has no end")
    check_platform(rows[0], source, platform)
    cases = [row for row in rows if row.get("kind") == "mechanism-case"]
    check(len(cases) == 3, "Unexpected mechanism case count")
    for ordinal, row in enumerate(cases, 1):
        result = row.get("result", {})
        check(result.get("Ordinal") == ordinal, "Mechanism profile order differs")
        check(result.get("ShrinkError") == "", "Socket shrink failed")
        if platform == "windows":
            check(result.get("SendBefore") == 65536, "Unexpected Windows send-buffer identity")
            check(result.get("SendAfter") == result.get("ShrinkRequested"),
                  "Windows send-buffer shrink was not exact")
            check(result.get("TerminalState") == "pending-at-400ms", "Windows write did not remain pending")
            check(result.get("TerminalMs", 0) >= 400, "Windows pending observation was too short")
        else:
            check(result.get("SendBefore") == 2626560, "Unexpected WSL2 send-buffer identity")
            check(result.get("SendAfter") == 4608, "Unexpected WSL2 shrunken send-buffer identity")
            check(result.get("TerminalState") == "completed", "WSL2 mechanism unexpectedly blocked")
            check(result.get("TerminalMs", 1e30) < 20, "WSL2 mechanism completion was unexpectedly slow")


def parse_production(data, source, platform):
    rows = parse_jsonl(data)
    check(rows[0].get("kind") == "production-start", "Production journal has no start")
    check(rows[-1].get("kind") == "production-end", "Production journal has no end")
    check_platform(rows[0], source, platform)
    cases = []
    current = None
    for row in rows[1:-1]:
        kind = row.get("kind")
        if kind == "started":
            check(current is None, "Unmatched production start")
            current = row
        elif kind == "completed":
            check(current is not None, "Production completion has no start")
            check(row.get("case") == current.get("case"), "Production case differs")
            check(row.get("attempt") == current.get("attempt"), "Production attempt differs")
            result = row.get("result")
            check(isinstance(result, dict), "Missing production result")
            cases.append((row["case"], row["attempt"], result))
            current = None
        else:
            raise ValueError(f"Unknown production event {kind}")
    check(current is None, "Production journal ended with an open case")
    return cases


def check_production(cases, config, platform):
    expected_cases = [item["name"] for item in config["production_cases"]]
    observed_case_order = []
    for case, _, _ in cases:
        if case not in observed_case_order:
            observed_case_order.append(case)
    check(observed_case_order == expected_cases, "Production case order differs")
    by_case = {}
    for case, attempt, result in cases:
        by_case.setdefault(case, []).append((attempt, result))
        check(result.get("Outcome") == "characterized" and result.get("Error") is None,
              "Production characterization failed")
        accounting = result.get("Accepted")
        check(accounting == result.get("Completed", 0) + result.get("Failed", 0),
              "Production connection accounting is not balanced")
        check(result.get("Active") == 0, "Production connection remained active")
        check(result.get("RawRead") == "clean-eof", "Production raw peer read differs")
        check(str(result.get("TlsRead", "")).startswith("clean-eof"), "Production TLS peer read differs")
    check(tuple(by_case) == tuple(expected_cases), "Production case inventory differs")
    for case, attempts in by_case.items():
        check([attempt for attempt, _ in attempts] == list(range(1, len(attempts) + 1)),
              f"Production attempt order differs in {case}")
        check(len(attempts) <= 2, f"Production attempt bound exceeded in {case}")
    check(len(by_case["P2-deep-default-timeout"]) == 1, "P2 attempt count differs")

    for case, attempts in by_case.items():
        for _, result in attempts:
            name = case
            if platform == "windows":
                if name == "P1-small-result-parked":
                    check(result["ConnectionOutcome"] == "ConnectionCompleted:closed", "P1 outcome differs")
                    check(result["MessageProcessed"] and "sig=0x11" in result["MessageDetail"], "P1 message differs")
                elif name == "P2-deep-default-timeout":
                    check(result["ConnectionOutcome"] == "ConnectionFailed:timeout", "P2 outcome differs")
                    check(not result["MessageProcessed"], "P2 unexpectedly processed the result")
                    check(29900 <= result["ConnectionEndMs"] <= 31000, "P2 default timeout differs")
                elif name == "P3-deep-750ms-timeout":
                    check(result["ConnectionOutcome"] == "ConnectionFailed:timeout", "P3 outcome differs")
                    check(not result["MessageProcessed"], "P3 unexpectedly processed the result")
                    check(700 <= result["ConnectionEndMs"] <= 1500, "P3 timeout differs")
                elif name == "P4-deep-listener-stop":
                    check(result["ConnectionOutcome"] == "ConnectionCompleted:listener-shutdown", "P4 outcome differs")
                    check(not result["MessageProcessed"], "P4 unexpectedly processed the result")
                    check(450 <= result["ConnectionEndMs"] <= 1000, "P4 listener-stop bound differs")
                elif name == "P5-goodbye":
                    check(result["ConnectionOutcome"] == "ConnectionCompleted:closed", "P5 outcome differs")
                    check(result["MessageProcessed"] and "sig=0x02" in result["MessageDetail"], "P5 goodbye differs")
                elif name == "P6-cooperative-receipt":
                    check(result["ConnectionOutcome"] == "ConnectionCompleted:closed", "P6 outcome differs")
                    check(result["MessageProcessed"] and "sig=0x11" in result["MessageDetail"], "P6 message differs")
                    check(result["FailureCode"] == "08N06", "P6 status differs")
                    check(result["FailureMessage"] == "Neo.ClientError.Request.Invalid", "P6 Neo4j code differs")
                    check(result["PeerUnreadAtEnd"] == 0, "P6 peer did not consume the failure")
            else:
                check(result["ConnectionOutcome"] == "ConnectionCompleted:closed", f"{name} WSL2 outcome differs")
                check(result["MessageProcessed"], f"{name} WSL2 did not process the result")
                check(result["ElapsedMs"] < 1000, f"{name} WSL2 completion was unexpectedly slow")
                if name == "P1-small-result-parked":
                    check("sig=0x11" in result["MessageDetail"], "WSL2 P1 message differs")
                elif name == "P5-goodbye":
                    check("sig=0x02" in result["MessageDetail"], "WSL2 P5 goodbye differs")
                elif name == "P6-cooperative-receipt":
                    check(result["FailureCode"] == "08N06", "WSL2 P6 status differs")
                    check(result["FailureMessage"] == "Neo.ClientError.Request.Invalid", "WSL2 P6 code differs")
                    check(result["PeerUnreadAtEnd"] == 0, "WSL2 P6 peer did not consume the failure")


def production_classification(cases):
    return [(case, result["ConnectionOutcome"], result["MessageProcessed"], result["MessageDetail"],
             result["FailureCode"], result["FailureMessage"]) for case, _, result in cases]


def verify(revision):
    global CONFIG
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    check(git("show", "-s", "--format=%P", commit).decode().split() == [PARENT],
          "Unexpected investigation parent")
    check(git("show", "-s", "--format=%T", PARENT).decode().strip() == PARENT_TREE,
          "Unexpected parent tree")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    check((manifest.get("parent_commit"), manifest.get("parent_tree"), manifest.get("hash_convention"))
          == (PARENT, PARENT_TREE, HASH_CONVENTION), "Unexpected manifest identity or convention")
    inventory = {f"{PACKAGE}/{name}" for name in NAMES}
    tree_paths = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    check(tree_paths == inventory, "Unexpected evidence inventory")
    expected = inventory | {REPORT}
    entries = manifest.get("entries", [])
    paths = [entry.get("path") for entry in entries]
    check(len(paths) == len(set(paths)), "Duplicate manifest path")
    check(set(paths) == expected - {MANIFEST}, "Missing, unexpected or incomplete hash entries")
    check(paths == sorted(paths), "Manifest entries are not in canonical path order")
    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r", "--name-status",
                      PARENT, commit).decode().splitlines())
    check(changes == {f"A\t{path}" for path in expected}, "Unexpected commit path or status")
    for entry in entries:
        path = entry["path"]
        data = blob(path)
        check(len(data) == entry["bytes"] and hashlib.sha256(data).hexdigest() == entry["sha256"],
              f"Blob digest or byte count differs at {path}")

    source = json.loads(blob(f"{PACKAGE}/source-identity.json"))
    canonical = source["canonical_repository"]
    check((canonical["head"], canonical["tree"], canonical["branch"]) == (PARENT, PARENT_TREE, "main"),
          "Unexpected canonical repository identity")
    config_data = blob(f"{PACKAGE}/configuration.json")
    config_identity = source["predeclared_configuration"]
    check(len(config_data) == config_identity["bytes"], "Configuration byte count differs")
    check(hashlib.sha256(config_data).hexdigest() == config_identity["sha256"],
          "Predeclared configuration identity differs")
    CONFIG = json.loads(config_data)
    for name, expected_hash in source["fixtures"].items():
        data = blob(f"{PACKAGE}/{name}")
        check(hashlib.sha256(data).hexdigest() == expected_hash, f"Fixture identity differs at {name}")
    for patch in source["replay_patches"]:
        data = blob(patch["path"])
        check(hashlib.sha256(data).hexdigest() == patch["sha256"], f"Replay patch differs at {patch['path']}")
    check(source["wsl2_listener_source_sha256"] == source["gate_wsl2_listener_sha256"],
          "WSL2 listener source differs from the recorded gate source")

    commission = json.loads(blob(f"{PACKAGE}/commissioning-matrix.json"))
    check(commission["commission"] == "Q-B5B-09-TLS-E1" and commission["established_before_execution"] is True,
          "Unexpected commissioning identity")
    check(tuple(cell["id"] for cell in commission["cells"]) == CELLS + tuple(f"P{i}" for i in range(1, 7)),
          "Commissioning cell inventory differs")
    check(tuple(item["id"] for item in commission["negative_controls"]) == ("NC-1", "NC-2"),
          "Negative-control inventory differs")
    check(commission["repeatability"]["discovery_attempts_per_cell"] == 4
          and commission["repeatability"]["maximum_runs_per_cell"] == 6
          and commission["repeatability"]["cap_cell_attempts"] == 3,
          "Commissioning repeatability bounds differ")

    reconciliation = json.loads(blob(f"{PACKAGE}/commissioning-budget-reconciliation.json"))
    check(reconciliation["original_aggregate_fields"]["windows_platform_attempts_total"]
          == commission["budgets"]["windows_platform_attempts_total"]
          and reconciliation["original_aggregate_fields"]["wsl2_campaign_attempts_total"]
          == commission["budgets"]["wsl2_campaign_attempts_total"],
          "Budget reconciliation does not preserve the aggregate fields")
    expected_counts = {
        "windows_final_candidate": 30,
        "windows_preserved_first_run": 11,
        "windows_negative_controls": 26,
        "windows_mechanism_cases": 3,
        "windows_production_characterization": 11,
        "windows_total_records": 81,
        "wsl2_final_candidate": 26,
        "wsl2_candidate_repeat": 26,
        "wsl2_mechanism_cases": 3,
        "wsl2_production_characterization": 22,
        "wsl2_total_records": 77,
    }
    check(reconciliation["executed_attempt_counts"] == expected_counts, "Budget reconciliation counts differ")

    matrix = json.loads(blob(f"{PACKAGE}/matrix.json"))
    check(matrix["commission"] == "Q-B5B-09-TLS-E1", "Matrix commission differs")
    check(set(matrix["qualifying_outcomes"]) == QUALIFYING_OUTCOMES, "Matrix qualifying outcomes differ")
    windows = parse_campaign(blob(f"{PACKAGE}/windows-campaign.jsonl"), CELLS, CONFIG, source, "windows")
    wsl2 = parse_campaign(blob(f"{PACKAGE}/wsl2-campaign.jsonl"), CELLS, CONFIG, source, "wsl2")
    check_final_campaign(windows, matrix, "windows")
    check_final_campaign(wsl2, matrix, "wsl2")
    wsl2_repeat = parse_campaign(blob(f"{PACKAGE}/wsl2-campaign-repeat.jsonl"), CELLS, CONFIG, source, "wsl2")
    for original, repeat in zip(wsl2, wsl2_repeat):
        check([run["result"]["Outcome"] for run in original["runs"]]
              == [run["result"]["Outcome"] for run in repeat["runs"]],
              f"WSL2 repeat classification differs in {original['cell']}")
        check(original["end"]["qualified"] == repeat["end"]["qualified"],
              f"WSL2 repeat qualification differs in {original['cell']}")

    check_first_run(blob(f"{PACKAGE}/windows-campaign-first-run.jsonl"), CONFIG, source)
    check_negative_controls(blob(f"{PACKAGE}/nc1-cipher-cap-tamper.jsonl"), CONFIG, source,
                            "nc1-cipher-cap-tamper")
    check_negative_controls(blob(f"{PACKAGE}/nc2-deadline-renewal.jsonl"), CONFIG, source,
                            "nc2-deadline-renewal")
    check_mechanism(blob(f"{PACKAGE}/windows-mechanism.jsonl"), source, "windows")
    check_mechanism(blob(f"{PACKAGE}/wsl2-mechanism.jsonl"), source, "wsl2")

    windows_production = parse_production(blob(f"{PACKAGE}/windows-production.jsonl"), source, "windows")
    wsl2_production = parse_production(blob(f"{PACKAGE}/wsl2-production.jsonl"), source, "wsl2")
    check_production(windows_production, CONFIG, "windows")
    check_production(wsl2_production, CONFIG, "wsl2")
    wsl2_production_repeat = parse_production(blob(f"{PACKAGE}/wsl2-production-repeat.jsonl"), source, "wsl2")
    check_production(wsl2_production_repeat, CONFIG, "wsl2")
    check(production_classification(wsl2_production) == production_classification(wsl2_production_repeat),
          "WSL2 production repeat classification differs")

    for name in TRANSCRIPT_NAMES:
        text = blob(f"{PACKAGE}/transcripts/{name}").decode("utf-8")
        check("Passed!" in text and "Failed:" in text and "Passed:" in text,
              f"Transcript does not contain the runner result: {name}")

    runner_requirements = {
        "run-wsl2-candidate.sh": ("TlsE1MechanismTests", "TlsE1Tests.FixedCampaign"),
        "run-wsl2-candidate-repeat.sh": ("TlsE1Tests.FixedCampaign",),
        "run-wsl2-production.sh": ("ProductionTlsCloseTests",),
        "run-wsl2-production-repeat.sh": ("ProductionTlsCloseTests",),
    }
    for name, requirements in runner_requirements.items():
        text = blob(f"{PACKAGE}/{name}").decode("utf-8")
        check("DOTNET_ROLL_FORWARD=Major" in text and "dotnet vstest" in text,
              f"WSL2 runner identity differs at {name}")
        check(all(requirement in text for requirement in requirements), f"WSL2 runner filter differs at {name}")

    actual_counts = {
        "windows_final_candidate": sum(len(summary["runs"]) for summary in windows),
        "wsl2_final_candidate": sum(len(summary["runs"]) for summary in wsl2),
        "wsl2_candidate_repeat": sum(len(summary["runs"]) for summary in wsl2_repeat),
    }
    check(actual_counts["windows_final_candidate"] == expected_counts["windows_final_candidate"],
          "Windows final attempt count differs")
    check(actual_counts["wsl2_final_candidate"] == expected_counts["wsl2_final_candidate"],
          "WSL2 final attempt count differs")
    check(actual_counts["wsl2_candidate_repeat"] == expected_counts["wsl2_candidate_repeat"],
          "WSL2 repeat attempt count differs")

    tree = git("show", "-s", "--format=%T", commit).decode().strip()
    print(f"PASS {len(entries)}/{len(entries)} blob hashes, exact parent, additions-only inventory, "
          f"bounded journals, matrix reconciliation, mechanism controls, and production characterization")
    print("NOTE commission-record qualification: aggregate attempt fields of 21 are stale; "
          "the explicit per-cell bounds govern and are reconciled in the evidence package")
    print(f"Commit {commit}")
    print(f"Tree   {tree}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [investigation-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (subprocess.CalledProcessError, OSError, KeyError, TypeError, ValueError) as error:
        sys.exit(f"FAIL {error}")
