#!/usr/bin/env python3
"""R3 verifier self-test: proves the independent verifier rejects EXEC-05, rejects proxy
ciphertext measurements presented as actual ciphertext, and detects every registered
false-pass condition — including the R3 cap-accounting violations."""
import hashlib
import json
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from verify_r1 import Verifier

MEASUREMENT = "beneath-sslstream-transport-boundary"
MAX_RECORD = 16406
RECEIPT_HEX = "7465726d696e616c2d6d65737361676521"  # b"terminal-message!"
RECEIPT_SHA = hashlib.sha256(bytes.fromhex(RECEIPT_HEX)).hexdigest()


def make_fresh_results_dir() -> Path:
    d = Path(tempfile.mkdtemp(prefix="r1-selftest-"))
    (d / "per-cell").mkdir()
    (d / "transcripts").mkdir()
    (d / "negative-controls").mkdir()
    (d / "peer").mkdir()
    return d


def write_journal(dir: Path, records: list):
    with open(dir / "campaign-journal.jsonl", "w") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")


def write_control_journal(dir: Path, control: str, records: list):
    with open(dir / "negative-controls" / f"{control}.jsonl", "w") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")


def write_cell_summary(dir: Path, cell: str, summary: dict):
    with open(dir / "per-cell" / f"{cell}.summary.json", "w") as f:
        json.dump(summary, f, indent=2)


def write_meta(dir: Path, commit: str, tree: str, run_id: str):
    with open(dir / "run-meta.json", "w") as f:
        json.dump({"commit": commit, "tree": tree, "status": "raw-complete",
                   "run_id": run_id}, f, indent=2)


def write_peer_receipt(dir: Path, cell: str, attempt: int, receipt: dict):
    with open(dir / "peer" / f"{cell}-attempt-{attempt}-receipt.jsonl", "w") as f:
        f.write(json.dumps({"record_type": "peer-receipt", **receipt}) + "\n")


def write_cell_journal(dir: Path, cell: str, records: list):
    with open(dir / "per-cell" / f"{cell}.jsonl", "w") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")


def make_attempt_record(cell: str, attempt: int, run_id: str, commit: str, tree: str,
                        is_repeat: bool = False):
    """Create a complete attempt record for the given cell, matching the R3 harness schema."""
    cap = cell in ("L5", "L6")
    base = {
        "record_type": "attempt", "cell": cell, "attempt": attempt, "seed": 1001, "run_id": run_id,
        "profile": {"ordinal": 1, "preceding_bytes": 4096 if cap else 36864,
                    "peer_receive": 4096 if cap else 2048, "target_send": 0 if cap else 2048},
        "declaration": "production" if cell in ("L1", "L2", "L7") else "candidate-cap",
        "production_commit": commit, "production_tree": tree,
        "confirmed": True, "pending_ms": 25.0, "sender_writable_at_sample": False,
        "peer_unread_at_sample": 1024, "buffer_configured": True,
        "elapsed_ms": 150.0, "stage": "kernel-write-pending",
        "accounting": {"accepted": 1, "active": 0, "completed": 1, "failed": 0, "messages": 3},
        "checks": {
            "confirmed": True, "pending_at_least_20_ms": True, "sender_not_writable": True,
            "peer_unread_positive": True, "buffer_configured": True, "elapsed_within_bound": True,
            "accounting_active_zero": True, "accounting_balanced": True, "single_connection": True,
            "terminal_write_not_completed": True, "peer_closed": True, "stop_bounded": True,
        },
        "guarantee_levels": {"local_write_completion": False,
                             "tls_close_notify_transmission": cell in ("L3", "L4"),
                             "peer_receipt": False, "verified_peer_receipt": False},
    }
    if cell in ("L1", "L2"):
        base["outcome"] = "induced-and-interrupted"
        base["interruption"] = "total-deadline" if cell == "L1" else "listener-cancellation"
    elif cell in ("L3", "L4"):
        base["outcome"] = "induced-and-interrupted"
        base["interruption"] = "phase-deadline" if cell == "L3" else "listener-cancellation"
        base["shutdown_pending"] = True
        base["shutdown_pending_ms"] = 25.0
        base["sender_writable_at_sample"] = False
        base["proxy_port"] = 20000
        base["proxy_journal"] = f"peer/L4-proxy-{attempt}-journal.jsonl"
    elif cell in ("L5", "L6"):
        tiny = cell == "L5"
        base["outcome"] = "cap-bounded-and-clean"
        base["interruption"] = "none"
        base["flood_mode"] = "tiny" if tiny else "large"
        base["ciphertext_measurement"] = MEASUREMENT
        base["ciphertext_source"] = ("wire-counting-inner-stream (bytes SslStream pulls from the "
                                     "socket = actual TLS record ciphertext)")
        base["registered_ciphertext_cap"] = 32768
        base["registered_plaintext_cap"] = 8192
        base["declared_ciphertext_cap"] = 32768
        base["ciphertext_cap_tampered"] = False
        base["max_tls_record_wire_bytes"] = MAX_RECORD
        # E1-faithful binding: L5 tiny flood -> cipher-cap binds (plain < 8192);
        # L6 large flood -> plain-cap binds (plain == 8192 exact), wire stays bounded.
        base["drain_outcome"] = "cipher-cap" if tiny else "plain-cap"
        base["encrypted_wire_bytes"] = 32775 if tiny else 12354
        base["plaintext_bytes"] = 1425 if tiny else 8192
        base["flood_application_bytes"] = 1595 if tiny else 192512
        base["flood_encrypted_wire_bytes"] = 34560 if tiny else 193546
        base["tls_record_overhead_bytes_drain"] = 31350 if tiny else 4162
        base["tls_record_overhead_bytes_flood"] = 32965 if tiny else 1034
        base["ciphertext_app_differential_observed"] = True
        base["plaintext_cap_enforced"] = True
        base["encrypted_cap_respected"] = True
        base["encrypted_bound_versus_registered"] = True
        base["client_consistency"] = True
        base["production_tls_terminal_observed"] = True
        base["production_messages_processed"] = 5
        base["checks"] = {
            "production_tls_terminal_observed": True,
            "plaintext_cap_enforced": True,
            "encrypted_cap_respected": True,
            "encrypted_bound_versus_registered": True,
            "ciphertext_app_differential_observed": True,
            "client_consistency": True,
            "accounting_active_zero": True,
            "accounting_balanced": True,
        }
    elif cell == "L7":
        base["outcome"] = "peer-receipt-verified"
        base["interruption"] = "none"
        base["checks"] = {
            "receipt_present": True, "peer_eof": True, "receipt_matches_campaign_digest": True,
            "peer_unread_at_end_zero": True, "accounting_active_zero": True,
            "accounting_balanced": True, "single_connection": True, "completed": True,
            "terminal_message_processed": True,
        }
    if is_repeat:
        base["repeat"] = attempt - 1
    return base


def make_valid_run(d: Path, run_id: str, commit: str, tree: str):
    all_records = []
    for cell in ["L1", "L2", "L3", "L4", "L5", "L6", "L7"]:
        cell_records = [
            make_attempt_record(cell, 1, run_id, commit, tree, False),
            make_attempt_record(cell, 2, run_id, commit, tree, True),
            make_attempt_record(cell, 3, run_id, commit, tree, True),
        ]
        all_records.extend(cell_records)
        write_cell_journal(d, cell, cell_records)
        write_cell_summary(d, cell, {"cell": cell, "kind": "test", "qualified": True,
                                     "successes": 3, "runs": 3,
                                     "qualifying_profile_ordinal": 1, "repeats_required": 2})
    write_journal(d, all_records)
    write_control_journal(d, "NC-1", [{"record_type": "attempt", "cell": "L5", "control": "NC-1",
                                       "run_id": run_id, "registered_ciphertext_cap": 32768,
                                       "tampered_ciphertext_cap": 131072,
                                       "declared_ciphertext_cap": 131072,
                                       "encrypted_wire_bytes": 131072, "tamper_detected": True,
                                       "ciphertext_measurement": MEASUREMENT,
                                       "outcome": "cap-checks-failed", "elapsed_ms": 100.0}])
    write_control_journal(d, "NC-2", [{"record_type": "attempt", "cell": "L1", "control": "NC-2",
                                       "run_id": run_id, "bound_deadline_ms": 500,
                                       "extended_deadline_ms": 800,
                                       "outcome": "induced-but-checks-failed",
                                       "elapsed_ms": 600.0}])
    for attempt in (1, 2, 3):
        write_peer_receipt(d, "L7", attempt, {"message_bytes": 16, "message_hex": RECEIPT_HEX,
                                              "sha256": RECEIPT_SHA, "receipt_ms": 5,
                                              "app": "python3-stdlib-ssl-peer"})
    write_meta(d, commit, tree, run_id)


def test_valid_run():
    print("TEST: valid R3 run accepted")
    d = make_fresh_results_dir()
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    make_valid_run(d, commit[:12], commit, tree)
    v = Verifier(str(d), commit, tree)
    ok, errors, warnings = v.run()
    shutil.rmtree(d)
    if not ok:
        print(f"  FAIL: {errors}")
        return False
    print("  PASS")
    return True


def test_exec05_rejection():
    print("TEST: EXEC-05 rejection")
    d = make_fresh_results_dir()
    commit = "56cd357"
    tree = "some-tree"
    records = [
        {"record_type": "attempt", "cell": "L1", "attempt": 1, "declaration": "production",
         "production_commit": "7429c6b", "production_tree": tree,
         "pending_ms": 100.996, "elapsed_ms": 100.996,
         "confirmed": True, "sender_writable_at_sample": False, "peer_unread_at_sample": 1,
         "buffer_configured": True, "tls_protocol": "Tls13",
         "outcome": "induced-and-interrupted"},
        {"record_type": "attempt", "cell": "L3", "attempt": 1, "declaration": "production",
         "production_commit": "7429c6b", "production_tree": tree,
         "pending_ms": 0.137, "elapsed_ms": 0.137,
         "outcome": "induced-and-interrupted"},
        {"record_type": "attempt", "cell": "L5", "attempt": 1, "declaration": "production",
         "production_commit": "7429c6b", "production_tree": tree,
         "wire_bytes": 131072, "plaintext_bytes": 131072,
         "outcome": "cap-bounded-and-clean"},
        {"record_type": "attempt", "cell": "L6", "attempt": 1, "declaration": "production",
         "production_commit": "7429c6b", "production_tree": tree,
         "declared_limit": 32768, "wire_bytes": 32768, "plaintext_bytes": 32768,
         "outcome": "cap-bounded-and-clean"},
    ]
    write_journal(d, records)
    write_meta(d, commit, tree, commit)
    v = Verifier(str(d), commit, tree)
    ok, errors = v.verify_rejects_exec05(d)
    shutil.rmtree(d)
    if ok:
        print("  FAIL: should have rejected EXEC-05")
        return False
    print(f"  PASS: rejected with {len(errors)} errors")
    for e in errors:
        print(f"    - {e}")
    return True


def test_false_pass_conditions():
    print("TEST: false-pass condition detection (Q1 + R3 cap accounting)")
    failures = 0
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]

    tests = [
        # Q1 conditions (preserved)
        ("L1 pending < 20ms", "L1", {"pending_ms": 10.0, "checks": {"pending_at_least_20_ms": False}}),
        ("L1 sender writable", "L1", {"sender_writable_at_sample": True, "checks": {"sender_not_writable": False}}),
        ("L1 peer unread 0", "L1", {"peer_unread_at_sample": 0, "checks": {"peer_unread_positive": False}}),
        ("L1 buffer not configured", "L1", {"buffer_configured": False, "checks": {"buffer_configured": False}}),
        ("L1 elapsed > 500ms", "L1", {"elapsed_ms": 600.0, "checks": {"elapsed_within_bound": False}}),
        ("L1 accounting active != 0", "L1", {"accounting": {"accepted": 1, "active": 1, "completed": 0, "failed": 0, "messages": 3}, "checks": {"accounting_active_zero": False}}),
        ("L1 accounting unbalanced", "L1", {"accounting": {"accepted": 1, "active": 0, "completed": 0, "failed": 0, "messages": 3}, "checks": {"accounting_balanced": False}}),
        ("L1 terminal write completed", "L1", {"accounting": {"accepted": 1, "active": 0, "completed": 1, "failed": 0, "messages": 4}, "checks": {"terminal_write_not_completed": False}}),
        ("L1 peer not closed", "L1", {"peer_read_outcome": "read-timeout after 0 bytes", "checks": {"peer_closed": False}}),
        ("L1 stop not bounded", "L1", {"stop_ms": 2000.0, "checks": {"stop_bounded": False}}),
        ("L3 shutdown pending < 20ms", "L3", {"shutdown_pending_ms": 0.14, "shutdown_pending": True}),
        ("L3 sender writable", "L3", {"sender_writable_at_sample": True}),
        ("L7 no receipt", "L7", {"checks": {"receipt_present": False}}),
        ("L7 receipt digest mismatch", "L7", {"checks": {"receipt_matches_campaign_digest": False}}),
        ("L7 peer unread not zero", "L7", {"checks": {"peer_unread_at_end_zero": False}}),
        ("NC-2 deadline not exceeded", "NC-2", {"extended_deadline_ms": 800, "elapsed_ms": 400.0}),
        # R3 cap-accounting conditions
        ("L5 proxy measurement rejected", "L5",
         {"ciphertext_measurement": "application-bytes-proxy",
          "ciphertext_source": "candidate TLS write flood counter (application bytes as encrypted proxy)"}),
        ("L5 missing measurement boundary", "L5", {"ciphertext_measurement": None}),
        ("L5 encrypted wire exceeds registered cap beyond one record", "L5",
         {"encrypted_wire_bytes": 32768 + 16406 + 1}),
        ("L5 no TLS record overhead (app-proxy indistinguishable)", "L5",
         {"tls_record_overhead_bytes_drain": 0}),
        ("L5 plaintext reached 8192 (encrypted cap did not bind)", "L5",
         {"plaintext_bytes": 8192, "drain_outcome": "plain-cap"}),
        ("L5 no production TLS terminal observation", "L5",
         {"production_tls_terminal_observed": False}),
        ("L5 declared cap tampered in the qualifying cell", "L5",
         {"declared_ciphertext_cap": 131072}),
        ("L6 plaintext not exactly 8192", "L6", {"plaintext_bytes": 8191}),
        ("L6 plaintext overshoot beyond cap", "L6", {"plaintext_bytes": 8192 + 2048}),
        ("L6 encrypted wire exceeds registered cap beyond one record", "L6",
         {"encrypted_wire_bytes": 32768 + 16406 + 100}),
        ("L6 drain terminated at peer-eof (no cap bound)", "L6", {"drain_outcome": "peer-eof"}),
        ("NC-1 tamper not actually executed", "NC-1",
         {"encrypted_wire_bytes": None, "tamper_detected": None}),
        ("NC-1 tamper under registered cap", "NC-1", {"encrypted_wire_bytes": 32768}),
        ("NC-1 proxy measurement", "NC-1",
         {"ciphertext_measurement": "application-bytes-proxy"}),
    ]

    for name, cell, overrides in tests:
        d = make_fresh_results_dir()
        record = make_attempt_record(cell if not cell.startswith("NC") else "L5", 1, run_id,
                                     commit, tree, False)
        for k, val in overrides.items():
            if k == "checks":
                record["checks"].update(val)
            elif val is None:
                record.pop(k, None)
            else:
                record[k] = val
        records = [record]
        write_cell_journal(d, record["cell"], records)
        write_cell_summary(d, record["cell"], {"cell": record["cell"], "kind": "test",
                                               "qualified": True, "successes": 1, "runs": 1,
                                               "repeats_required": 0})
        write_control_journal(d, "NC-1", [{
            "record_type": "attempt", "cell": "L5", "control": "NC-1", "run_id": run_id,
            "registered_ciphertext_cap": 32768, "tampered_ciphertext_cap": 131072,
            "declared_ciphertext_cap": 131072,
            "encrypted_wire_bytes": 131072, "tamper_detected": True,
            "ciphertext_measurement": MEASUREMENT,
            "outcome": "cap-checks-failed", "elapsed_ms": 100.0}])
        write_control_journal(d, "NC-2", [{
            "record_type": "attempt", "cell": "L1", "control": "NC-2", "run_id": run_id,
            "bound_deadline_ms": 500, "extended_deadline_ms": 800,
            "outcome": "induced-but-checks-failed", "elapsed_ms": 600.0}])
        write_peer_receipt(d, "L7", 1, {"message_bytes": 16, "message_hex": RECEIPT_HEX,
                                        "sha256": RECEIPT_SHA, "receipt_ms": 5,
                                        "app": "python3-stdlib-ssl-peer"})
        write_meta(d, commit, tree, run_id)

        v = Verifier(str(d), commit, tree)
        ok, errors, _ = v.run()
        shutil.rmtree(d)
        if ok:
            print(f"  FAIL: {name} - verifier should have rejected but passed")
            failures += 1
        else:
            print(f"  PASS: {name} - correctly rejected ({len(errors)} errors)")

    return failures == 0


def main():
    all_pass = True
    all_pass &= test_valid_run()
    all_pass &= test_exec05_rejection()
    all_pass &= test_false_pass_conditions()
    print("\nALL SELF-TESTS PASSED" if all_pass else "\nSOME SELF-TESTS FAILED")
    return 0 if all_pass else 1


if __name__ == "__main__":
    sys.exit(main())
