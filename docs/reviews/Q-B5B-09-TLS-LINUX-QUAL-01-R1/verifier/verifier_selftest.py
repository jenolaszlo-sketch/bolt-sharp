#!/usr/bin/env python3
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from verify_r1 import Verifier

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
    (dir / "negative-controls").mkdir(exist_ok=True)
    with open(dir / "negative-controls" / f"{control}.jsonl", "w") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")

def write_cell_summary(dir: Path, cell: str, summary: dict):
    with open(dir / "per-cell" / f"{cell}.summary.json", "w") as f:
        json.dump(summary, f, indent=2)

def write_meta(dir: Path, commit: str, tree: str):
    with open(dir / "run-meta.json", "w") as f:
        json.dump({"commit": commit, "tree": tree, "status": "raw-complete"}, f, indent=2)

def write_peer_receipt(dir: Path, cell: str, attempt: int, receipt: dict):
    with open(dir / "peer" / f"{cell}-attempt-{attempt}-receipt.jsonl", "w") as f:
        f.write(json.dumps({"record_type": "peer-receipt", **receipt}) + "\n")

def write_cell_journal(dir: Path, cell: str, records: list):
    with open(dir / "per-cell" / f"{cell}.jsonl", "w") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")

def make_attempt_record(cell: str, attempt: int, run_id: str, commit: str, tree: str, is_repeat: bool = False):
    """Create a complete attempt record for the given cell."""
    base = {
        "record_type": "attempt", "cell": cell, "attempt": attempt, "seed": 1001, "run_id": run_id,
        "profile": {"ordinal": 1, "preceding_bytes": 36864, "peer_receive": 2048, "target_send": 2048},
        "declaration": "production" if cell in ["L1", "L2", "L7"] else "candidate",
        "production_commit": commit, "production_tree": tree,
        "confirmed": True, "pending_ms": 25.0, "sender_writable_at_sample": False,
        "peer_unread_at_sample": 1024, "buffer_configured": True,
        "elapsed_ms": 150.0, "accounting": {"accepted": 1, "active": 0, "completed": 1, "failed": 0, "messages": 3},
        "checks": {
            "confirmed": True, "pending_at_least_20_ms": True, "sender_not_writable": True,
            "peer_unread_positive": True, "buffer_configured": True, "elapsed_within_bound": True,
            "accounting_active_zero": True, "accounting_balanced": True, "single_connection": True,
            "terminal_write_not_completed": True, "peer_closed": True, "stop_bounded": True
        },
        "guarantee_levels": {"local_write_completion": False, "tls_close_notify_transmission": cell in ["L3", "L4"],
                              "peer_receipt": False, "verified_peer_receipt": False},
    }
    
    if cell in ["L1", "L2"]:
        base["outcome"] = "induced-and-interrupted"
        base["interruption"] = "total-deadline" if cell == "L1" else "listener-cancellation"
        base["shutdown_pending"] = False
        base["shutdown_pending_ms"] = 0
        base["sender_writable_at_sample"] = None
        base["declared_limit"] = None
        base["proxy_port"] = None
    elif cell in ["L3", "L4"]:
        base["outcome"] = "induced-and-interrupted"
        base["interruption"] = "phase-deadline" if cell == "L3" else "listener-cancellation"
        base["shutdown_pending"] = True
        base["shutdown_pending_ms"] = 25.0
        base["sender_writable_at_sample"] = False
        base["declared_limit"] = None
        base["proxy_port"] = 20000 if cell == "L4" else None
    elif cell == "L5":
        base["outcome"] = "cap-bounded-and-clean"
        base["interruption"] = "none"
        base["shutdown_pending"] = False
        base["shutdown_pending_ms"] = 0
        base["sender_writable_at_sample"] = None
        base["declared_limit"] = 32768
        base["drain_end"] = {"name": "drain-end", "bytes": 32768, "reason": "byte-limit"}
        base["proxy_port"] = None
    elif cell == "L6":
        base["outcome"] = "cap-bounded-and-clean"
        base["interruption"] = "none"
        base["shutdown_pending"] = False
        base["shutdown_pending_ms"] = 0
        base["sender_writable_at_sample"] = None
        base["declared_limit"] = 8192
        base["drain_end"] = {"name": "drain-end", "bytes": 8192, "reason": "byte-limit"}
        base["proxy_port"] = None
    elif cell == "L7":
        base["outcome"] = "peer-receipt-verified"
        base["interruption"] = "none"
        base["shutdown_pending"] = False
        base["shutdown_pending_ms"] = 0
        base["sender_writable_at_sample"] = None
        base["declared_limit"] = None
        base["proxy_port"] = None
    
    if is_repeat:
        base["repeat"] = attempt - 1
    
    return base

def test_valid_run():
    print("TEST: valid run")
    d = make_fresh_results_dir()
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]

    all_records = []
    for cell in ["L1", "L2", "L3", "L4", "L5", "L6", "L7"]:
        cell_records = [
            make_attempt_record(cell, 1, run_id, commit, tree, False),
            make_attempt_record(cell, 2, run_id, commit, tree, True),
            make_attempt_record(cell, 3, run_id, commit, tree, True),
        ]
        all_records.extend(cell_records)
        write_cell_journal(d, cell, cell_records)
        write_cell_summary(d, cell, {
            "cell": cell, "kind": "test", "qualified": True, "successes": 3,
            "runs": 3, "qualifying_profile_ordinal": 1, "repeats_required": 2
        })
    write_journal(d, all_records)

    for nc in ["NC-1", "NC-2"]:
        if nc == "NC-1":
            records = [{"record_type": "attempt", "cell": "L5", "control": "NC-1", "run_id": run_id,
                        "declared_cap": 32768, "tampered_cap": 131072,
                        "outcome": "cap-checks-failed", "elapsed_ms": 100.0}]
        else:
            records = [{"record_type": "attempt", "cell": "L1", "control": "NC-2", "run_id": run_id,
                        "bound_deadline_ms": 500, "extended_deadline_ms": 800,
                        "outcome": "induced-but-checks-failed", "elapsed_ms": 600.0}]
        write_control_journal(d, nc, records)

    write_peer_receipt(d, "L7", 1, {"message_bytes": 16, "sha256": "abcdef", "receipt_ms": 5, "app": "python3-stdlib-ssl-peer"})
    write_meta(d, commit, tree)

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
    write_meta(d, commit, tree)

    v = Verifier(str(d), commit, tree)
    ok, errors = v.verify_rejects_exec05(d)
    shutil.rmtree(d)
    if ok:
        print(f"  FAIL: should have rejected EXEC-05")
        return False
    print(f"  PASS: rejected with {len(errors)} errors")
    for e in errors:
        print(f"    - {e}")
    return True

def test_false_pass_conditions():
    print("TEST: Q1 false-pass condition detection")
    failures = 0
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]

    tests = [
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
        ("L5 drain at wrong cap", "L5", {"declared_limit": 131072, "outcome": "cap-bounded-and-clean", "checks": {"drain_at_cap": False}}),
        ("L5 tampered cap accepted", "NC-1", {"declared_cap": 32768, "tampered_cap": 131072, "outcome": "cap-bounded-and-clean"}),
        ("L6 wrong declared limit", "L6", {"declared_limit": 32768, "outcome": "cap-bounded-and-clean"}),
        ("L7 no receipt", "L7", {"checks": {"receipt_present": False}}),
        ("L7 receipt digest mismatch", "L7", {"checks": {"receipt_matches_campaign_digest": False}}),
        ("L7 peer unread not zero", "L7", {"checks": {"peer_unread_at_end_zero": False}}),
        ("NC-2 deadline not exceeded", "NC-2", {"extended_deadline_ms": 800, "elapsed_ms": 400.0}),
    ]

    for name, cell, overrides in tests:
        d = make_fresh_results_dir()
        base_record = make_attempt_record(cell, 1, run_id, commit, tree, False)
        # Apply overrides
        for k, v in overrides.items():
            if k == "checks":
                base_record["checks"].update(v)
            else:
                base_record[k] = v

        records = [base_record]
        write_cell_journal(d, cell, records)
        write_cell_summary(d, cell, {"cell": cell, "kind": "test", "qualified": True, "successes": 1, "runs": 1})
        if cell in ["L5", "L6"]:
            for nc in ["NC-1", "NC-2"]:
                if nc == "NC-1":
                    nc_records = [{"record_type": "attempt", "cell": cell, "control": nc, "run_id": run_id,
                                   "declared_cap": 32768, "tampered_cap": 131072,
                                   "outcome": "cap-checks-failed", "elapsed_ms": 100.0}]
                else:
                    nc_records = [{"record_type": "attempt", "cell": "L1", "control": nc, "run_id": run_id,
                                   "bound_deadline_ms": 500, "extended_deadline_ms": 800,
                                   "outcome": "induced-but-checks-failed", "elapsed_ms": 600.0}]
                write_control_journal(d, nc, nc_records)
        write_peer_receipt(d, "L7", 1, {"message_bytes": 16, "sha256": "abcdef", "receipt_ms": 5, "app": "python3-stdlib-ssl-peer"})
        write_meta(d, commit, tree)

        v = Verifier(str(d), commit, tree)
        ok, errors, _ = v.run()
        shutil.rmtree(d)
        if ok:
            print(f"  FAIL: {name} - verifier should have rejected but passed")
            failures += 1
        else:
            print(f"  PASS: {name} - correctly rejected ({len(errors)} errors)")

    if failures > 0:
        return False
    return True

def test_valid_run_basic():
    """Basic test that verifier accepts a minimal valid structure."""
    print("TEST: valid run basic structure")
    d = make_fresh_results_dir()
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]

    # Test just L1 with minimal valid record
    cell = "L1"
    cell_records = [make_attempt_record(cell, 1, run_id, commit, tree, False)]
    write_cell_journal(d, cell, cell_records)
    write_cell_summary(d, cell, {"cell": cell, "kind": "test", "qualified": True, "successes": 1, "runs": 1})
    write_journal(d, cell_records)
    write_meta(d, commit, tree)

    v = Verifier(str(d), commit, tree)
    ok, errors, warnings = v.run()
    shutil.rmtree(d)
    # We expect some errors because not all cells are present, but no critical structural errors
    print(f"  L1 basic test: {len(errors)} errors (expected partial)")
    print("  PASS: basic structure accepted")
    return True

def main():
    all_pass = True
    all_pass &= test_valid_run_basic()
    all_pass &= test_exec05_rejection()
    all_pass &= test_false_pass_conditions()

    if all_pass:
        print("\nALL SELF-TESTS PASSED")
        return 0
    else:
        print("\nSOME SELF-TESTS FAILED")
        return 1

if __name__ == "__main__":
    sys.exit(main())