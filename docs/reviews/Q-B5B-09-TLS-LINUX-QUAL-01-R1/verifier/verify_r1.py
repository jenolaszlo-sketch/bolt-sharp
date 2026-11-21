#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

class Verifier:
    def __init__(self, results_dir: str, commit: str, tree: str, mode: str = "full"):
        self.results_dir = Path(results_dir)
        self.commit = commit
        self.tree = tree
        self.mode = mode
        self.errors: List[str] = []
        self.warnings: List[str] = []

    def error(self, msg: str):
        self.errors.append(msg)

    def warn(self, msg: str):
        self.warnings.append(msg)

    def load_journal(self, path: Path) -> List[Dict[str, Any]]:
        records = []
        if path.exists():
            with open(path, "r") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        records.append(json.loads(line))
        return records

    def load_cell_summary(self, cell: str) -> Optional[Dict[str, Any]]:
        path = self.results_dir / "per-cell" / f"{cell}.summary.json"
        if path.exists():
            with open(path, "r") as f:
                return json.load(f)
        return None

    def load_peer_receipt(self, cell: str, attempt: int) -> Optional[Dict[str, Any]]:
        path = self.results_dir / "peer" / f"{cell}-attempt-{attempt}-receipt.jsonl"
        if path.exists():
            with open(path, "r") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        rec = json.loads(line)
                        if rec.get("record_type") == "peer-receipt":
                            return rec
        return None

    def verify_fresh_run(self):
        journal = self.results_dir / "campaign-journal.jsonl"
        if not journal.exists():
            self.error("campaign-journal.jsonl missing")
            return
        records = self.load_journal(journal)
        run_ids = set(r.get("run_id") for r in records if "run_id" in r)
        if len(run_ids) != 1:
            self.error(f"journal contains {len(run_ids)} run_ids: {run_ids}")
        for r in records:
            if r.get("run_id") != self.commit[:12]:
                self.error(f"record run_id {r.get('run_id')} does not match commit prefix {self.commit[:12]}")

    def verify_commit_tree(self):
        meta_path = self.results_dir / "run-meta.json"
        if not meta_path.exists():
            self.error("run-meta.json missing")
            return
        with open(meta_path, "r") as f:
            meta = json.load(f)
        if meta.get("commit") != self.commit:
            self.error(f"meta commit {meta.get('commit')} != expected {self.commit}")
        if meta.get("tree") != self.tree:
            self.error(f"meta tree {meta.get('tree')} != expected {self.tree}")
        if meta.get("status") != "raw-complete":
            self.error(f"meta status {meta.get('status')} != raw-complete")

    def verify_cell_l1(self):
        cell = "L1"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified (successes={summary.get('successes')}, required={summary.get('repeats_required', 2) + 1})")
            return
        if summary.get("qualifying_profile_ordinal") not in [1, 2, 3, 4]:
            self.error(f"{cell}: invalid qualifying profile ordinal")
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        if len(attempts) < 3:
            self.error(f"{cell}: fewer than 3 attempts in per-cell journal")
        for a in attempts:
            checks = a.get("checks", {})
            if not checks.get("confirmed", False):
                self.error(f"{cell} attempt {a.get('attempt')}: Confirmed=false")
            if checks.get("pending_at_least_20_ms") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: PendingMs < 20ms")
            if checks.get("sender_not_writable") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: SenderWritableAtSample=true")
            if checks.get("peer_unread_positive") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: PeerUnreadAtSample <= 0")
            if checks.get("buffer_configured") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: BufferConfigured=false")
            if checks.get("elapsed_within_bound") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: ElapsedMs > 500ms bound")
            if checks.get("accounting_active_zero") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: Active != 0")
            if checks.get("accounting_balanced") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: Accepted != Completed+Failed")
            if checks.get("single_connection") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: not single connection")
            if checks.get("terminal_write_not_completed") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: terminal write completed (messages > 3)")
            if checks.get("peer_closed") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: peer not closed")
            if checks.get("stop_bounded") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: stop not bounded")
            if a.get("outcome") != "induced-and-interrupted":
                self.error(f"{cell} attempt {a.get('attempt')}: outcome {a.get('outcome')} != induced-and-interrupted")

    def verify_cell_l2(self):
        cell = "L2"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            checks = a.get("checks", {})
            if not checks.get("confirmed", False):
                self.error(f"{cell} attempt {a.get('attempt')}: Confirmed=false")
            if a.get("interruption") != "listener-cancellation":
                self.error(f"{cell} attempt {a.get('attempt')}: interruption not listener-cancellation")

    def verify_cell_l3(self):
        cell = "L3"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            if not a.get("shutdown_pending", False):
                self.error(f"{cell} attempt {a.get('attempt')}: shutdown not pending")
            if a.get("shutdown_pending_ms", 0) < 20:
                self.error(f"{cell} attempt {a.get('attempt')}: shutdown pending < 20ms")
            if a.get("sender_writable_at_sample", True):
                self.error(f"{cell} attempt {a.get('attempt')}: sender writable at sample")
            if a.get("outcome") != "induced-and-interrupted":
                self.error(f"{cell} attempt {a.get('attempt')}: outcome != induced-and-interrupted")

    def verify_cell_l4(self):
        cell = "L4"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            if not a.get("shutdown_pending", False):
                self.error(f"{cell} attempt {a.get('attempt')}: shutdown not pending")
            if a.get("shutdown_pending_ms", 0) < 20:
                self.error(f"{cell} attempt {a.get('attempt')}: shutdown pending < 20ms")
            if a.get("interruption") != "listener-cancellation":
                self.error(f"{cell} attempt {a.get('attempt')}: interruption not listener-cancellation")
            if not a.get("proxy_port"):
                self.error(f"{cell} attempt {a.get('attempt')}: no proxy port recorded")

    def verify_cell_l5(self):
        cell = "L5"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            checks = a.get("checks", {})
            if not checks.get("ciphertext_cap_respected", False):
                self.error(f"{cell} attempt {a.get('attempt')}: ciphertext cap not respected (ciphertext_bytes > 32768)")
            if not checks.get("flood_stopped_at_limit", False):
                self.error(f"{cell} attempt {a.get('attempt')}: flood did not stop at ciphertext limit")
            if a.get("declared_ciphertext_cap") != 32768:
                self.error(f"{cell} attempt {a.get('attempt')}: declared ciphertext cap != 32768")
            if a.get("outcome") != "cap-bounded-and-clean":
                self.error(f"{cell} attempt {a.get('attempt')}: outcome != cap-bounded-and-clean")
            # Verify plaintext_bytes is recorded (even if not capped for L5)
            if "plaintext_bytes" not in a:
                self.error(f"{cell} attempt {a.get('attempt')}: plaintext_bytes not recorded")
            if "ciphertext_bytes" not in a:
                self.error(f"{cell} attempt {a.get('attempt')}: ciphertext_bytes not recorded")

    def verify_cell_l6(self):
        cell = "L6"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            checks = a.get("checks", {})
            if not checks.get("ciphertext_cap_respected", False):
                self.error(f"{cell} attempt {a.get('attempt')}: ciphertext cap not respected (ciphertext_bytes > 32768)")
            if not checks.get("plaintext_cap_respected", False):
                self.error(f"{cell} attempt {a.get('attempt')}: plaintext cap not respected (plaintext_bytes > 8192)")
            if not checks.get("flood_stopped_at_limit", False):
                self.error(f"{cell} attempt {a.get('attempt')}: flood did not stop at ciphertext limit")
            if a.get("declared_ciphertext_cap") != 32768:
                self.error(f"{cell} attempt {a.get('attempt')}: declared ciphertext cap != 32768")
            if a.get("declared_plaintext_cap") != 8192:
                self.error(f"{cell} attempt {a.get('attempt')}: declared plaintext cap != 8192")
            if a.get("outcome") != "cap-bounded-and-clean":
                self.error(f"{cell} attempt {a.get('attempt')}: outcome != cap-bounded-and-clean")
            if "plaintext_bytes" not in a:
                self.error(f"{cell} attempt {a.get('attempt')}: plaintext_bytes not recorded")
            if "ciphertext_bytes" not in a:
                self.error(f"{cell} attempt {a.get('attempt')}: ciphertext_bytes not recorded")

    def verify_cell_l6_prod(self):
        cell = "L6-PROD"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.warn(f"{cell}: supplementary cell summary missing")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            if a.get("plaintext_bytes") != 8192:
                self.warn(f"{cell} attempt {a.get('attempt')}: plaintext drain != 8192")
            if a.get("ciphertext_bytes") != 0:
                self.warn(f"{cell} attempt {a.get('attempt')}: ciphertext bytes != 0")

    def verify_cell_l7(self):
        cell = "L7"
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return
        if not summary.get("qualified", False):
            self.error(f"{cell}: not qualified")
            return
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        attempts = [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]
        for a in attempts:
            checks = a.get("checks", {})
            if not checks.get("receipt_present", False):
                self.error(f"{cell} attempt {a.get('attempt')}: receipt not present")
            if not checks.get("peer_eof", False):
                self.error(f"{cell} attempt {a.get('attempt')}: peer EOF not recorded")
            if not checks.get("receipt_matches_campaign_digest", False):
                self.error(f"{cell} attempt {a.get('attempt')}: receipt digest mismatch")
            if not checks.get("peer_unread_at_end_zero", False):
                self.error(f"{cell} attempt {a.get('attempt')}: peer unread at end != 0")
            if not checks.get("accounting_active_zero", False):
                self.error(f"{cell} attempt {a.get('attempt')}: Active != 0")
            if not checks.get("accounting_balanced", False):
                self.error(f"{cell} attempt {a.get('attempt')}: Accepted != Completed+Failed")
            if not checks.get("single_connection", False):
                self.error(f"{cell} attempt {a.get('attempt')}: not single connection")
            if not checks.get("completed", False):
                self.error(f"{cell} attempt {a.get('attempt')}: not completed")
            if not checks.get("terminal_message_processed", False):
                self.error(f"{cell} attempt {a.get('attempt')}: terminal message not processed")
            if a.get("outcome") != "peer-receipt-verified":
                self.error(f"{cell} attempt {a.get('attempt')}: outcome != peer-receipt-verified")

    def verify_negative_controls(self):
        for nc in ["NC-1", "NC-2"]:
            path = self.results_dir / "negative-controls" / f"{nc}.jsonl"
            records = self.load_journal(path)
            if not records:
                self.error(f"{nc}: no records")
                continue
            for r in records:
                if nc == "NC-1":
                    if r.get("declared_ciphertext_cap") != 32768 or r.get("tampered_ciphertext_cap") != 131072:
                        self.error(f"{nc}: cap values not recorded")
                    if r.get("outcome") == "cap-bounded-and-clean":
                        self.error(f"{nc}: tampered cap accepted as clean")
                elif nc == "NC-2":
                    if r.get("bound_deadline_ms") != 500 or r.get("extended_deadline_ms") != 800:
                        self.error(f"{nc}: deadline values not recorded")
                    if r.get("elapsed_ms", 0) <= 500:
                        self.error(f"{nc}: extended deadline not exceeded")

    def verify_repeatability(self):
        cells = ["L1", "L2", "L3", "L4", "L5", "L6", "L7"]
        for cell in cells:
            summary = self.load_cell_summary(cell)
            if not summary:
                continue
            if not summary.get("qualified", False):
                continue
            if summary.get("successes") != 3:
                self.error(f"{cell}: expected 3 successes (1 discovery + 2 repeats), got {summary.get('successes')}")
            if summary.get("runs") > 6:
                self.error(f"{cell}: runs {summary.get('runs')} > max 6")

    def verify_guarantee_levels(self):
        journal = self.results_dir / "campaign-journal.jsonl"
        records = self.load_journal(journal)
        l1_attempts = [r for r in records if r.get("cell") == "L1" and r.get("record_type") == "attempt"]
        l3_attempts = [r for r in records if r.get("cell") == "L3" and r.get("record_type") == "attempt"]
        for a in l1_attempts + l3_attempts:
            levels = a.get("guarantee_levels", {})
            if a.get("cell") == "L1":
                if levels.get("local_write_completion") is not False:
                    self.error(f"L1: local_write_completion should be False")
                if levels.get("tls_close_notify_transmission") is not False:
                    self.error(f"L1: tls_close_notify_transmission should be False")
                if levels.get("peer_receipt") is not False:
                    self.error(f"L1: peer_receipt should be False")
                if levels.get("verified_peer_receipt") is not False:
                    self.error(f"L1: verified_peer_receipt should be False")
            if a.get("cell") == "L3":
                if levels.get("tls_close_notify_transmission") is not True:
                    self.error(f"L3: tls_close_notify_transmission should be True (candidate)")

    def run(self) -> Tuple[bool, List[str], List[str]]:
        self.verify_fresh_run()
        self.verify_commit_tree()
        self.verify_cell_l1()
        self.verify_cell_l2()
        self.verify_cell_l3()
        self.verify_cell_l4()
        self.verify_cell_l5()
        self.verify_cell_l6()
        self.verify_cell_l6_prod()
        self.verify_cell_l7()
        self.verify_negative_controls()
        self.verify_repeatability()
        self.verify_guarantee_levels()
        return len(self.errors) == 0, self.errors, self.warnings

    def verify_rejects_exec05(self, exec05_dir: Path) -> Tuple[bool, List[str]]:
        """Verify that the verifier rejects EXEC-05 evidence."""
        errors = []
        journal = exec05_dir / "campaign-journal.jsonl"
        if not journal.exists():
            return False, ["EXEC-05 journal not found"]
        records = self.load_journal(journal)
        if len(records) != 35:
            errors.append(f"EXEC-05 has {len(records)} records, expected 35 mixed")
        l1_records = [r for r in records if r.get("cell") == "L1"]
        for r in l1_records:
            if r.get("declaration") == "production" and "tls_protocol" in r:
                errors.append("EXEC-05 L1 claims TLS but has no TLS")
        l3_records = [r for r in records if r.get("cell") == "L3"]
        for r in l3_records:
            if r.get("pending_ms", 0) < 20:
                errors.append(f"EXEC-05 L3 pending_ms={r.get('pending_ms')} < 20ms")
        l5_records = [r for r in records if r.get("cell") == "L5"]
        for r in l5_records:
            if r.get("wire_bytes", 0) > 32768 and r.get("outcome") == "cap-bounded-and-clean":
                errors.append(f"EXEC-05 L5 wire_bytes={r.get('wire_bytes')} > 32768 but clean")
        l6_records = [r for r in records if r.get("cell") == "L6"]
        for r in l6_records:
            if r.get("declared_limit") != 8192:
                errors.append(f"EXEC-05 L6 declared_limit={r.get('declared_limit')} != 8192")
        return len(errors) == 0, errors


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--results", required=True)
    p.add_argument("--commit", required=True)
    p.add_argument("--tree", required=True)
    p.add_argument("--mode", default="full")
    p.add_argument("--exec05-dir", help="EXEC-05 results directory to verify rejection")
    args = p.parse_args()

    v = Verifier(args.results, args.commit, args.tree, args.mode)
    ok, errors, warnings = v.run()

    if args.exec05_dir:
        ok2, errors2 = v.verify_rejects_exec05(Path(args.exec05_dir))
        if not ok2:
            errors.extend(errors2)
            ok = False

    for w in warnings:
        print(f"WARN: {w}", file=sys.stderr)
    for e in errors:
        print(f"ERROR: {e}", file=sys.stderr)

    if ok:
        print("VERIFIER: PASS")
        return 0
    else:
        print("VERIFIER: FAIL")
        return 1


if __name__ == "__main__":
    sys.exit(main())