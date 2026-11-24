#!/usr/bin/env python3
"""R4 verifier self-test: proves the independent verifier rejects EXEC-05, rejects proxy
ciphertext measurements presented as actual ciphertext, enforces the encrypted cap STRICTLY
(T5 WireBytes == 32768, no one-record overshoot allowance), and detects every registered
false-pass condition — including dispatch-coverage integrity (wrong or uninvoked dispensing
targets) and the NC-2 unconfirmed-prerequisite control."""
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
    # The real framing-aware peer records the cell and attempt on every receipt; the
    # verifier (R4-R1 F1) binds each receipt to the attempt it claims to describe.
    with open(dir / "peer" / f"{cell}-attempt-{attempt}-receipt.jsonl", "w") as f:
        f.write(json.dumps({"record_type": "peer-receipt", "cell": cell, "attempt": attempt,
                            **receipt}) + "\n")


def write_cell_journal(dir: Path, cell: str, records: list):
    with open(dir / "per-cell" / f"{cell}.jsonl", "w") as f:
        for r in records:
            f.write(json.dumps(r) + "\n")


def make_attempt_record(cell: str, attempt: int, run_id: str, commit: str, tree: str,
                        is_repeat: bool = False):
    """Create a complete attempt record for the given cell, matching the R3 harness schema."""
    cap = cell in ("L5", "L6")
    dispatch = {
        "L1": "Production.RunTerminalAttemptAsync",
        "L2": "Production.RunTerminalAttemptAsync",
        "L3": "Candidate.RunCloseNotifyAttemptAsync",
        "L4": "Candidate.RunCloseNotifyAttemptAsync",
        "L5": "Candidate.RunCapAttemptAsync",
        "L6": "Candidate.RunCapAttemptAsync",
        "L7": "Production.RunReceiptAttemptAsync",
        "L6-PROD": "Production.RunPlaintextCapAttemptAsync",
    }[cell]
    declaration = {
        "L1": "production", "L2": "production", "L7": "production",
        "L6-PROD": "production-plaintext",
    }.get(cell, "candidate-cap")
    base = {
        "record_type": "attempt", "cell": cell, "attempt": attempt, "seed": 1001, "run_id": run_id,
        "profile": {"ordinal": 1, "preceding_bytes": 4096 if cap else 36864,
                    "peer_receive": 4096 if cap else 2048, "target_send": 0 if cap else 2048},
        "declaration": declaration,
        "dispatch_target": dispatch,
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
        base["interrupted"] = True
        base["sender_writable_at_sample"] = False
        base["proxy_port"] = 20000
        base["proxy_journal"] = f"peer/L4-proxy-{attempt}-journal.jsonl"
        base["peer_listener_cancelled"] = True
        base["listener_stop_ms"] = 15.0
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
        base["encrypted_wire_bytes"] = 32768 if tiny else 12354
        base["plaintext_bytes"] = 1425 if tiny else 8192
        base["flood_application_bytes"] = 1595 if tiny else 192512
        base["flood_encrypted_wire_bytes"] = 34560 if tiny else 193546
        base["tls_record_overhead_bytes_drain"] = 31350 if tiny else 4162
        base["tls_record_overhead_bytes_flood"] = 32965 if tiny else 1034
        base["ciphertext_app_differential_observed"] = True
        base["plaintext_cap_enforced"] = True
        base["encrypted_cap_respected"] = True
        base["encrypted_cap_strict"] = True
        base["encrypted_bound_versus_registered"] = True
        base["client_consistency"] = True
        base["production_tls_terminal_observed"] = True
        base["production_messages_processed"] = 5
        base["checks"] = {
            "production_tls_terminal_observed": True,
            "plaintext_cap_enforced": True,
            "encrypted_cap_respected": True,
            "encrypted_cap_strict": True,
            "encrypted_bound_versus_registered": True,
            "ciphertext_app_differential_observed": True,
            "client_consistency": True,
            "accounting_active_zero": True,
            "accounting_balanced": True,
        }
    elif cell == "L7":
        base["outcome"] = "peer-receipt-verified"
        base["interruption"] = "none"
        base["expected_terminal_message_bytes"] = len(bytes.fromhex(RECEIPT_HEX))
        base["expected_terminal_sha256"] = RECEIPT_SHA
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
                                       "dispatch_target": "Controls.RunNC1CiphertextCapTamperAsync",
                                       "run_id": run_id, "registered_ciphertext_cap": 32768,
                                       "tampered_ciphertext_cap": 131072,
                                       "declared_ciphertext_cap": 131072,
                                       "encrypted_wire_bytes": 131072, "tamper_detected": True,
                                       "ciphertext_measurement": MEASUREMENT,
                                       "outcome": "cap-checks-failed", "elapsed_ms": 100.0}])
    write_control_journal(d, "NC-2", [{"record_type": "attempt", "cell": "L1", "control": "NC-2",
                                       "dispatch_target": "Controls.RunNC2DeadlineRenewalAsync",
                                       "confirmed": True, "prerequisite_profile_ordinal": 4,
                                       "prerequisite_confirmed": True,
                                       "run_id": run_id, "bound_deadline_ms": 500,
                                       "extended_deadline_ms": 800,
                                       "outcome": "induced-but-checks-failed",
                                       "deadline_tamper_detected": True,
                                       "elapsed_ms": 600.0}])
    for attempt in (1, 2, 3):
        write_peer_receipt(d, "L7", attempt, {"message_bytes": len(bytes.fromhex(RECEIPT_HEX)),
                                              "message_hex": RECEIPT_HEX,
                                              "sha256": RECEIPT_SHA, "receipt_ms": 5,
                                              "app": "python3-stdlib-ssl-peer"})
    targets = ["Candidate.RunCapAttemptAsync", "Candidate.RunCloseNotifyAttemptAsync",
               "Controls.RunNC1CiphertextCapTamperAsync", "Controls.RunNC2DeadlineRenewalAsync",
               "Production.RunPlaintextCapAttemptAsync",
               "Production.RunReceiptAttemptAsync",
               "Production.RunTerminalAttemptAsync"]
    audit = {
        "record_type": "dispatch-audit",
        "run_id": run_id,
        "mode_requires_all": True,
        "implementations": [
            {"implementation": t, "cells": [], "invocations": 1, "invoked": True, "required": True}
            for t in targets
        ],
        "all_required_invoked": True,
    }
    with open(d / "campaign-journal.jsonl", "a") as f:
        f.write(json.dumps(audit) + "\n")
    write_meta(d, commit, tree, run_id)


def test_valid_run():
    print("TEST: valid R4 run accepted")
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



def _rewrite_cell_attempt(d: Path, cell: str, attempt: int,
                          field_updates=None,
                          check_updates=None,
                          drop_fields=()):
    """Mutate one attempt record inside a complete valid run."""
    path = d / "per-cell" / f"{cell}.jsonl"
    records = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    for r in records:
        if r.get("attempt") == attempt:
            for k in drop_fields:
                r.pop(k, None)
            r.update(field_updates or {})
            if check_updates:
                r.setdefault("checks", {}).update(check_updates)
    path.write_text("\n".join(json.dumps(r) for r in records) + "\n", encoding="utf-8")


def _rewrite_control(d: Path, control: str, field_updates=None, drop_fields=()):
    path = d / "negative-controls" / f"{control}.jsonl"
    records = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    for r in records:
        for k in drop_fields:
            r.pop(k, None)
        r.update(field_updates or {})
    path.write_text("\n".join(json.dumps(r) for r in records) + "\n", encoding="utf-8")


def _rewrite_dispatch_audit(d: Path, audit_updates=None, drop_row=None, drop_audit=False):
    path = d / "campaign-journal.jsonl"
    records = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    if drop_audit:
        records = [r for r in records if r.get("record_type") != "dispatch-audit"]
    else:
        for r in records:
            if r.get("record_type") == "dispatch-audit":
                if drop_row is not None:
                    r["implementations"] = [i for i in r.get("implementations", [])
                                            if i.get("implementation") != drop_row]
                r.update(audit_updates or {})
    path.write_text("\n".join(json.dumps(r) for r in records) + "\n", encoding="utf-8")


def _set_dispatch_row(d: Path, target: str, **updates):
    path = d / "campaign-journal.jsonl"
    records = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    for r in records:
        if r.get("record_type") == "dispatch-audit":
            for i in r.get("implementations", []):
                if i.get("implementation") == target:
                    i.update(updates)
    path.write_text("\n".join(json.dumps(r) for r in records) + "\n", encoding="utf-8")


def _rewrite_l7_receipt(d: Path, attempt: int, receipt_updates: dict):
    path = d / "peer" / f"L7-attempt-{attempt}-receipt.jsonl"
    records = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    for r in records:
        r.update(receipt_updates)
    path.write_text("\n".join(json.dumps(r) for r in records) + "\n", encoding="utf-8")


def _set_mutate_l7(d: Path, vals: "dict | None" = None, drop_fields: tuple = ()):
    """Mutate the journal side of an L7 attempt (R4-R1 F1)."""
    _rewrite_cell_attempt(d, "L7", 2, vals or {}, None, drop_fields)


def _mutate_l6prod(d: Path, run_id: str, commit: str, tree: str,
                   dispatch_target: str = "Production.RunTerminalAttemptAsync"):
    """Add the production supplementary cell to a run that omitted it, mis-dispatched."""
    record = {
        "record_type": "attempt", "cell": "L6-PROD", "attempt": 1, "run_id": run_id,
        "declaration": "production-plaintext", "production_commit": commit,
        "production_tree": tree, "dispatch_target": dispatch_target,
        "plaintext_bytes": 8192, "ciphertext_bytes": 0,
        "checks": {"drain_admitted": True}, "outcome": "cap-bounded-and-clean",
    }
    write_cell_journal(d, "L6-PROD", [record])
    write_cell_summary(d, "L6-PROD", {"cell": "L6-PROD", "kind": "production-plaintext-cap",
                                      "supplementary": True, "qualified": True, "successes": 1,
                                      "runs": 1})


def test_dispatch_audit_integrity():
    """R4: the campaign journal must show every registered implementation actually invoked."""
    print("TEST: dispatch-coverage integrity (implementations reachable AND invoked)")
    failures = 0
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]
    cases = [
        ("dispatch-audit record missing entirely",
         lambda d: _rewrite_dispatch_audit(d, drop_audit=True)),
        ("not every required implementation invoked",
         lambda d: _rewrite_dispatch_audit(d, {"all_required_invoked": False})),
        ("receipt implementation never invoked (dead dispatch)",
         lambda d: _set_dispatch_row(d, "Production.RunReceiptAttemptAsync", invoked=False,
                                     invocations=0)),
        ("close-notify implementation never invoked",
         lambda d: _set_dispatch_row(d, "Candidate.RunCloseNotifyAttemptAsync", invoked=False,
                                     invocations=0)),
        ("terminal-write implementation never invoked",
         lambda d: _set_dispatch_row(d, "Production.RunTerminalAttemptAsync", invoked=False,
                                     invocations=0)),
        ("cap implementation row missing",
         lambda d: _rewrite_dispatch_audit(d, drop_row="Candidate.RunCapAttemptAsync")),
        ("production-plaintext implementation row missing",
         lambda d: _rewrite_dispatch_audit(d, drop_row="Production.RunPlaintextCapAttemptAsync")),
    ]
    for name, mutate in cases:
        d = make_fresh_results_dir()
        make_valid_run(d, run_id, commit, tree)
        mutate(d)
        v = Verifier(str(d), commit, tree)
        ok, errors, _ = v.run()
        shutil.rmtree(d)
        if ok:
            print(f"  FAIL: {name} - verifier should have rejected but passed")
            failures += 1
        else:
            print(f"  PASS: {name} - correctly rejected ({len(errors)} errors)")
    return failures == 0


def test_strict_cap_boundary_accepted():
    """The strict contract must ACCEPT the exact boundary and a minimal-positive plaintext
    observation (no over-rejection, and no arbitrary minimum above zero)."""
    print("TEST: strict encrypted-cap boundary accepted (32768 exact; plaintext > 0)")
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]
    d = make_fresh_results_dir()
    make_valid_run(d, run_id, commit, tree)
    # L5 terminates exactly at the cap; L6 stays inside it; L5 decrypted a minimal 1 byte.
    _rewrite_cell_attempt(d, "L5", 1, field_updates={
        "encrypted_wire_bytes": 32768, "plaintext_bytes": 1})
    _rewrite_cell_attempt(d, "L6", 1, field_updates={"encrypted_wire_bytes": 32768})
    v = Verifier(str(d), commit, tree)
    ok, errors, _ = v.run()
    shutil.rmtree(d)
    if not ok:
        print(f"  FAIL: exact cap with minimal positive plaintext should be accepted: {errors}")
        return False
    print("  PASS")
    return True


def test_l7_binding_accepted():
    """The R4-R1 F1 binding must ACCEPT a matched journal/receipt pair (no over-rejection)."""
    print("TEST: L7 journal<->peer-receipt binding accepted when matched")
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]
    d = make_fresh_results_dir()
    make_valid_run(d, run_id, commit, tree)
    # Re-assert the receipt association fields explicitly, as the real peer writes them.
    for attempt in (1, 2, 3):
        _rewrite_l7_receipt(d, attempt, {"cell": "L7", "attempt": attempt})
    v = Verifier(str(d), commit, tree)
    ok, errors, _ = v.run()
    shutil.rmtree(d)
    if not ok:
        print(f"  FAIL: matched L7 binding should be accepted: {errors}")
        return False
    print("  PASS")
    return True


def test_false_pass_conditions():
    print("TEST: false-pass condition detection (Q1 + R3 cap accounting + R4 dispatch/strictness)")
    failures = 0
    commit = "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f"
    tree = "2fdf1c3b96586465eb7547d23b6b1792240692ba"
    run_id = commit[:12]
    A = _rewrite_cell_attempt
    C = _rewrite_control

    # Each case mutates ONE thing in an otherwise COMPLETE valid run, so any rejection is
    # attributable to the targeted registered condition rather than to unrelated gaps.
    tests = [
        # ---- Q1 conditions (preserved) ----
        ("L1 pending < 20ms", lambda d: A(d, "L1", 2, {"pending_ms": 10.0}, {"pending_at_least_20_ms": False})),
        ("L1 sender writable", lambda d: A(d, "L1", 2, {"sender_writable_at_sample": True}, {"sender_not_writable": False})),
        ("L1 peer unread 0", lambda d: A(d, "L1", 2, {"peer_unread_at_sample": 0}, {"peer_unread_positive": False})),
        ("L1 buffer not configured", lambda d: A(d, "L1", 2, {"buffer_configured": False}, {"buffer_configured": False})),
        ("L1 elapsed > 500ms", lambda d: A(d, "L1", 2, {"elapsed_ms": 600.0}, {"elapsed_within_bound": False})),
        ("L1 accounting active != 0", lambda d: A(d, "L1", 2, {"accounting": {"accepted": 1, "active": 1, "completed": 0, "failed": 0, "messages": 3}}, {"accounting_active_zero": False})),
        ("L1 accounting unbalanced", lambda d: A(d, "L1", 2, {"accounting": {"accepted": 1, "active": 0, "completed": 0, "failed": 0, "messages": 3}}, {"accounting_balanced": False})),
        ("L1 terminal write completed", lambda d: A(d, "L1", 2, {"accounting": {"accepted": 1, "active": 0, "completed": 1, "failed": 0, "messages": 4}}, {"terminal_write_not_completed": False})),
        ("L1 peer not closed", lambda d: A(d, "L1", 2, {"peer_read_outcome": "read-timeout after 0 bytes"}, {"peer_closed": False})),
        ("L1 stop not bounded", lambda d: A(d, "L1", 2, {"stop_ms": 2000.0}, {"stop_bounded": False})),
        ("L3 shutdown pending < 20ms", lambda d: A(d, "L3", 2, {"shutdown_pending_ms": 0.14, "shutdown_pending": True})),
        ("L3 sender writable", lambda d: A(d, "L3", 2, {"sender_writable_at_sample": True})),
        # ---- required interruption evidence (R4) ----
        ("L3 attempt not interrupted", lambda d: A(d, "L3", 2, {"interrupted": False})),
        ("L3 interrupted field missing", lambda d: A(d, "L3", 2, drop_fields=("interrupted",))),
        ("L4 attempt not interrupted", lambda d: A(d, "L4", 2, {"interrupted": False})),
        # ---- L7 receipt evidence ----
        ("L7 no receipt", lambda d: A(d, "L7", 2, check_updates={"receipt_present": False})),
        ("L7 receipt digest mismatch", lambda d: A(d, "L7", 2, check_updates={"receipt_matches_campaign_digest": False})),
        ("L7 peer unread not zero", lambda d: A(d, "L7", 2, check_updates={"peer_unread_at_end_zero": False})),
        ("L7 peer receipt journal absent", lambda d: (d / "peer" / "L7-attempt-2-receipt.jsonl").unlink()),
        ("L7 peer receipt digest does not match its own payload",
         lambda d: _rewrite_l7_receipt(d, 2, {"sha256": "0" * 64})),
        # ---- R4-R1 F1: journal expectation must be bound to the peer receipt ----
        ("L7 journal expected digest corrupted", lambda d: _set_mutate_l7(d, {"expected_terminal_sha256": "f" * 64})),
        ("L7 journal expected message bytes corrupted", lambda d: _set_mutate_l7(d, {"expected_terminal_message_bytes": 999})),
        ("L7 journal expected digest malformed (not hex)", lambda d: _set_mutate_l7(d, {"expected_terminal_sha256": "not-a-digest"})),
        ("L7 journal expected digest missing", lambda d: _set_mutate_l7(d, drop_fields=("expected_terminal_sha256",))),
        ("L7 journal expected message bytes missing", lambda d: _set_mutate_l7(d, drop_fields=("expected_terminal_message_bytes",))),
        ("L7 receipt associated with the wrong attempt", lambda d: _rewrite_l7_receipt(d, 2, {"attempt": 9})),
        ("L7 receipt message_bytes corrupted", lambda d: _rewrite_l7_receipt(d, 2, {"message_bytes": 999})),
        ("L7 receipt cell mislabelled", lambda d: _rewrite_l7_receipt(d, 2, {"cell": "L1"})),
        # ---- R3 proxy-measurement rejection ----
        ("L5 proxy measurement rejected", lambda d: A(d, "L5", 1, {"ciphertext_measurement": "application-bytes-proxy", "ciphertext_source": "candidate TLS write flood counter (application bytes as encrypted proxy)"})),
        ("L5 missing measurement boundary", lambda d: A(d, "L5", 1, {"ciphertext_measurement": None})),
        # ---- R4 strict encrypted cap (no one-record overshoot allowance) ----
        ("L5 encrypted wire below the strict cap", lambda d: A(d, "L5", 1, {"encrypted_wire_bytes": 32760})),
        ("L5 one-record overshoot no longer allowed", lambda d: A(d, "L5", 1, {"encrypted_wire_bytes": 32768 + 16406})),
        ("L5 encrypted wire beyond the registered cap", lambda d: A(d, "L5", 1, {"encrypted_wire_bytes": 32768 + 16406 + 1})),
        ("L5 encrypted_cap_strict not true", lambda d: A(d, "L5", 1, check_updates={"encrypted_cap_strict": False})),
        ("L5 no TLS record overhead (app-proxy indistinguishable)", lambda d: A(d, "L5", 1, {"tls_record_overhead_bytes_drain": 0})),
        ("L5 plaintext reached 8192 (encrypted cap did not bind)", lambda d: A(d, "L5", 1, {"plaintext_bytes": 8192, "drain_outcome": "plain-cap"})),
        ("L5 no production TLS terminal observation", lambda d: A(d, "L5", 1, {"production_tls_terminal_observed": False})),
        # ---- R4-R1 F2: the tiny-flood cell must have decrypted something ----
        ("L5 plaintext zero (tiny flood decrypted nothing)", lambda d: A(d, "L5", 1, {"plaintext_bytes": 0})),
        ("L5 plaintext negative", lambda d: A(d, "L5", 1, {"plaintext_bytes": -1})),
        ("L5 declared cap tampered in the qualifying cell", lambda d: A(d, "L5", 1, {"declared_ciphertext_cap": 131072})),
        ("L6 plaintext not exactly 8192", lambda d: A(d, "L6", 1, {"plaintext_bytes": 8191})),
        ("L6 plaintext overshoot beyond cap", lambda d: A(d, "L6", 1, {"plaintext_bytes": 8192 + 2048})),
        ("L6 encrypted wire exceeds registered cap", lambda d: A(d, "L6", 1, {"encrypted_wire_bytes": 32768 + 16406 + 100})),
        ("L6 encrypted_cap_strict not true", lambda d: A(d, "L6", 1, check_updates={"encrypted_cap_strict": False})),
        ("L6 drain terminated at peer-eof (no cap bound)", lambda d: A(d, "L6", 1, {"drain_outcome": "peer-eof"})),
        # ---- negative controls ----
        ("NC-1 tamper not actually executed", lambda d: C(d, "NC-1", {"encrypted_wire_bytes": None, "tamper_detected": None})),
        ("NC-1 tamper under registered cap", lambda d: C(d, "NC-1", {"encrypted_wire_bytes": 32768})),
        ("NC-1 proxy measurement", lambda d: C(d, "NC-1", {"ciphertext_measurement": "application-bytes-proxy"})),
        ("NC-2 deadline not exceeded", lambda d: C(d, "NC-2", {"extended_deadline_ms": 800, "elapsed_ms": 400.0})),
        ("NC-2 prerequisite not confirmed (the R3 defect)", lambda d: C(d, "NC-2", {"prerequisite_confirmed": False})),
        ("NC-2 precursor-not-observed outcome", lambda d: C(d, "NC-2", {"outcome": "precursor-not-observed"})),
        ("NC-2 deadline tamper not detected", lambda d: C(d, "NC-2", {"deadline_tamper_detected": False})),
        ("NC-2 dispatched to the wrong implementation", lambda d: C(d, "NC-2", {"dispatch_target": "Production.RunTerminalAttemptAsync"})),
        # ---- R4 dispatch coverage ----
        ("L7 dispatched to the terminal-write path (dead dispatch)", lambda d: A(d, "L7", 2, {"dispatch_target": "Production.RunTerminalAttemptAsync"})),
        ("L3 dispatched to the cap implementation", lambda d: A(d, "L3", 2, {"dispatch_target": "Candidate.RunCapAttemptAsync"})),
        ("L5 dispatched to the receipt implementation", lambda d: A(d, "L5", 1, {"dispatch_target": "Production.RunReceiptAttemptAsync"})),
        ("L6-PROD dispatched to the terminal-write path",
         lambda d: _mutate_l6prod(d, run_id, commit, tree)),
    ]

    for name, mutate in tests:
        d = make_fresh_results_dir()
        make_valid_run(d, run_id, commit, tree)
        mutate(d)
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
    all_pass &= test_dispatch_audit_integrity()
    all_pass &= test_strict_cap_boundary_accepted()
    all_pass &= test_l7_binding_accepted()
    all_pass &= test_false_pass_conditions()
    print("\nALL SELF-TESTS PASSED" if all_pass else "\nSOME SELF-TESTS FAILED")
    return 0 if all_pass else 1


if __name__ == "__main__":
    sys.exit(main())
