#!/usr/bin/env python3
"""Independent fail-closed verifier for the R1/R2/R3 qualification harness evidence.

R3 semantics: L5/L6 ciphertext evidence must be measured at the transport boundary
beneath SslStream (actual TLS record ciphertext). Proxy or application-byte substitutes
are rejected.

R4 semantics (strict): the encrypted cap is enforced STRICTLY at the transport boundary
— the original E1 contract (T5 WireBytes == 32768; NC-1 WireBytes == 131072; T6
WireBytes <= 32768). The plaintext cap is enforced exactly. No one-record overshoot
allowance is accepted. Every registered cell and negative control must additionally
record the implementation it was dispatched to (the R4 dispatch-coverage audit), and the
campaign journal must show every required implementation actually invoked.
"""
import argparse
import json
import sys
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple

MEASUREMENT_BOUNDARY = "beneath-sslstream-transport-boundary"
REGISTERED_CIPHERTEXT_CAP = 32768
REGISTERED_PLAINTEXT_CAP = 8192
REGISTERED_NC1_TAMPERED_CAP = 131072

DISPATCH_TARGETS = {
    "L1": "Production.RunTerminalAttemptAsync",
    "L2": "Production.RunTerminalAttemptAsync",
    "L3": "Candidate.RunCloseNotifyAttemptAsync",
    "L4": "Candidate.RunCloseNotifyAttemptAsync",
    "L5": "Candidate.RunCapAttemptAsync",
    "L6": "Candidate.RunCapAttemptAsync",
    "L7": "Production.RunReceiptAttemptAsync",
    "L6-PROD": "Production.RunPlaintextCapAttemptAsync",
    "NC-1": "Controls.RunNC1CiphertextCapTamperAsync",
    "NC-2": "Controls.RunNC2DeadlineRenewalAsync",
}

QUALIFYING_OUTCOMES = {
    "L1": "induced-and-interrupted",
    "L2": "induced-and-interrupted",
    "L3": "induced-and-interrupted",
    "L4": "induced-and-interrupted",
    "L5": "cap-bounded-and-clean",
    "L6": "cap-bounded-and-clean",
    "L7": "peer-receipt-verified",
}
ALLOWED_NON_QUALIFYING = {
    "precursor-not-observed", "environment", "attempt-watchdog",
    "induced-but-checks-failed", "cap-checks-failed", "peer-receipt-failed",
}


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
            # utf-8-sig tolerates a UTF-8 BOM (older harness runs emitted one) and strips it.
            with open(path, "r", encoding="utf-8-sig") as f:
                for line in f:
                    line = line.strip()
                    if line:
                        records.append(json.loads(line))
        return records

    def load_cell_summary(self, cell: str) -> Optional[Dict[str, Any]]:
        path = self.results_dir / "per-cell" / f"{cell}.summary.json"
        if path.exists():
            with open(path, "r", encoding="utf-8-sig") as f:
                return json.load(f)
        return None

    def _load_attempts(self, cell: str) -> List[Dict[str, Any]]:
        journal = self.results_dir / "per-cell" / f"{cell}.jsonl"
        records = self.load_journal(journal)
        return [r for r in records if r.get("record_type") == "attempt" and r.get("cell") == cell]

    def load_peer_receipts(self, cell: str, attempt: int) -> List[Dict[str, Any]]:
        """Return EVERY peer-receipt record in the attempt's journal (R4-R2 F6).

        The previous implementation returned only the FIRST matching record, which
        silently ignored duplicate, contradictory, or unrelated appended receipt
        records. Receipt journals legitimately contain other diagnostic records
        (peer-start, tls, peer-eof) with no record_type, so only records explicitly
        typed as peer-receipt are counted.
        """
        path = self.results_dir / "peer" / f"{cell}-attempt-{attempt}-receipt.jsonl"
        if not path.exists():
            return []
        return [rec for rec in self.load_journal(path)
                if isinstance(rec, dict) and rec.get("record_type") == "peer-receipt"]

    def _qualified_cell(self, cell: str) -> Optional[Tuple[Dict[str, Any], List[Dict[str, Any]]]]:
        """Load a qualified cell and partition its attempts into qualifying vs discovery."""
        summary = self.load_cell_summary(cell)
        if not summary:
            self.error(f"{cell}: summary missing")
            return None
        if not summary.get("qualified", False):
            self.error(f"{cell}: summary not qualified (successes={summary.get('successes')})")
            return None
        attempts = self._load_attempts(cell)
        qualifying = [a for a in attempts if a.get("outcome") == QUALIFYING_OUTCOMES[cell]]
        for a in attempts:
            if a.get("outcome") not in QUALIFYING_OUTCOMES[cell] and \
                    a.get("outcome") not in ALLOWED_NON_QUALIFYING:
                self.error(f"{cell} attempt {a.get('attempt')}: unregistered outcome "
                           f"{a.get('outcome')!r}")
        repeats_required = summary.get("repeats_required", 2)
        expected = 1 + repeats_required
        if len(qualifying) != expected:
            self.error(f"{cell}: expected {expected} qualifying attempts "
                       f"(1 discovery + {repeats_required} repeats), found {len(qualifying)}")
            return None
        # Repeat-classification equality: all qualifying attempts on one profile ordinal.
        ordinals = {a.get("profile", {}).get("ordinal") for a in qualifying}
        if ordinals != {summary.get("qualifying_profile_ordinal")}:
            self.error(f"{cell}: qualifying attempts span profile ordinals {ordinals}, "
                       f"summary declares {summary.get('qualifying_profile_ordinal')}")
        # Repeat records must exist and be classified as repeats when repeats are required.
        if repeats_required > 0:
            repeat_marks = sorted(a.get("repeat") or 0 for a in qualifying if a.get("repeat"))
            if repeat_marks != list(range(1, repeats_required + 1)):
                self.error(f"{cell}: repeat classification incomplete: {repeat_marks}")
        return summary, qualifying

    def verify_fresh_run(self):
        journal = self.results_dir / "campaign-journal.jsonl"
        if not journal.exists():
            self.error("campaign-journal.jsonl missing")
            return
        records = self.load_journal(journal)
        # Run scoping: every record belongs to exactly ONE run id; revision binding is
        # enforced against run-meta.json (commit/tree), not the operator-chosen run label.
        run_ids = set(r.get("run_id") for r in records if "run_id" in r)
        if len(run_ids) != 1:
            self.error(f"journal contains {len(run_ids)} run_ids: {run_ids}")
        meta_path = self.results_dir / "run-meta.json"
        if meta_path.exists():
            with open(meta_path, "r", encoding="utf-8-sig") as f:
                meta_run_id = json.load(f).get("run_id")
            if run_ids and meta_run_id not in run_ids:
                self.error(f"run-meta run_id {meta_run_id} does not match the journal run_id(s) {run_ids}")

    def verify_commit_tree(self):
        meta_path = self.results_dir / "run-meta.json"
        if not meta_path.exists():
            self.error("run-meta.json missing")
            return
        with open(meta_path, "r", encoding="utf-8-sig") as f:
            meta = json.load(f)
        if meta.get("commit") != self.commit:
            self.error(f"meta commit {meta.get('commit')} != expected {self.commit}")
        if meta.get("tree") != self.tree:
            self.error(f"meta tree {meta.get('tree')} != expected {self.tree}")
        if meta.get("status") != "raw-complete":
            self.error(f"meta status {meta.get('status')} != raw-complete")

    # ---------------------------------------------------------------- dispatch audit
    def verify_dispatch_audit(self):
        """R4: every registered cell/control implementation must be reachable AND invoked."""
        journal = self.results_dir / "campaign-journal.jsonl"
        records = self.load_journal(journal)
        audits = [r for r in records if r.get("record_type") == "dispatch-audit"]
        if not audits:
            self.error("dispatch-audit record missing from the campaign journal")
            return
        audit = audits[-1]
        rows = audit.get("implementations")
        if not isinstance(rows, list) or not rows:
            self.error("dispatch-audit record contains no implementation rows")
            return
        by_target = {r.get("implementation"): r for r in rows if isinstance(r, dict)}
        for target in sorted(set(DISPATCH_TARGETS.values())):
            row = by_target.get(target)
            if row is None:
                self.error(f"dispatch-audit: registered implementation {target} is not audited")
                continue
            cells = [c for c, t in DISPATCH_TARGETS.items() if t == target]
            if row.get("invoked") is not True:
                self.error(f"dispatch-audit: implementation {target} (cells {cells}) was never "
                            f"invoked — registered dispatch is unreachable")
        if self.mode == "full" and audit.get("all_required_invoked") is not True:
            self.error("dispatch-audit: not every required implementation was invoked in full mode")

    # ---------------------------------------------------------------- L1 / L2
    def _terminal_write_oracle(self, cell: str, a: Dict[str, Any]):
        checks = a.get("checks", {})
        attempt = a.get("attempt")
        # R4 dispatch coverage: the registered implementation must be the one that ran.
        if a.get("dispatch_target") != DISPATCH_TARGETS[cell]:
            self.error(f"{cell} attempt {attempt}: dispatch_target {a.get('dispatch_target')!r} "
                        f"!= {DISPATCH_TARGETS[cell]!r} (registered implementation not invoked)")
        if not checks.get("confirmed", False):
            self.error(f"{cell} attempt {attempt}: Confirmed=false")
        if checks.get("pending_at_least_20_ms") is not True:
            self.error(f"{cell} attempt {attempt}: PendingMs < 20ms")
        if checks.get("sender_not_writable") is not True:
            self.error(f"{cell} attempt {attempt}: SenderWritableAtSample=true")
        if checks.get("peer_unread_positive") is not True:
            self.error(f"{cell} attempt {attempt}: PeerUnreadAtSample <= 0")
        if checks.get("buffer_configured") is not True:
            self.error(f"{cell} attempt {attempt}: BufferConfigured=false")
        if checks.get("elapsed_within_bound") is not True:
            self.error(f"{cell} attempt {attempt}: ElapsedMs > 500ms bound")
        if checks.get("accounting_active_zero") is not True:
            self.error(f"{cell} attempt {attempt}: Active != 0")
        if checks.get("accounting_balanced") is not True:
            self.error(f"{cell} attempt {attempt}: Accepted != Completed+Failed")
        if checks.get("single_connection") is not True:
            self.error(f"{cell} attempt {attempt}: not single connection")
        if checks.get("terminal_write_not_completed") is not True:
            self.error(f"{cell} attempt {attempt}: terminal write completed (messages > 3)")
        if checks.get("peer_closed") is not True:
            self.error(f"{cell} attempt {attempt}: peer not closed")
        if checks.get("stop_bounded") is not True:
            self.error(f"{cell} attempt {attempt}: stop not bounded")
        if a.get("production_commit") != self.commit:
            self.error(f"{cell} attempt {attempt}: production commit not recorded")
        if a.get("stage") != "kernel-write-pending":
            self.error(f"{cell} attempt {attempt}: stage != kernel-write-pending")

    def verify_cell_l1(self):
        loaded = self._qualified_cell("L1")
        if loaded is None:
            return
        _, qualifying = loaded
        for a in qualifying:
            self._terminal_write_oracle("L1", a)
            if a.get("interruption") != "total-deadline":
                self.error(f"L1 attempt {a.get('attempt')}: interruption != total-deadline")

    def verify_cell_l2(self):
        loaded = self._qualified_cell("L2")
        if loaded is None:
            return
        _, qualifying = loaded
        for a in qualifying:
            self._terminal_write_oracle("L2", a)
            if a.get("interruption") != "listener-cancellation":
                self.error(f"L2 attempt {a.get('attempt')}: interruption not listener-cancellation")

    # ---------------------------------------------------------------- L3 / L4
    def _close_notify_oracle(self, cell: str, a: Dict[str, Any]):
        attempt = a.get("attempt")
        # R4 dispatch coverage: the registered implementation must be the one that ran.
        if a.get("dispatch_target") != DISPATCH_TARGETS[cell]:
            self.error(f"{cell} attempt {attempt}: dispatch_target {a.get('dispatch_target')!r} "
                        f"!= {DISPATCH_TARGETS[cell]!r} (registered implementation not invoked)")
        if not a.get("shutdown_pending", False):
            self.error(f"{cell} attempt {attempt}: live shutdown not observed pending")
        if a.get("shutdown_pending_ms", 0) < 20:
            self.error(f"{cell} attempt {attempt}: shutdown pending < 20ms")
        if a.get("sender_writable_at_sample", True) is not False:
            self.error(f"{cell} attempt {attempt}: sender writable at sample")
        if a.get("buffer_configured") is not True:
            self.error(f"{cell} attempt {attempt}: BufferConfigured=false")
        if a.get("interrupted") is not True:
            self.error(f"{cell} attempt {attempt}: attempt not interrupted")
        levels = a.get("guarantee_levels", {})
        if levels.get("tls_close_notify_transmission") is not True:
            self.error(f"{cell} attempt {attempt}: tls_close_notify_transmission must be "
                       f"true (candidate live shutdown evidence)")

    def verify_cell_l3(self):
        loaded = self._qualified_cell("L3")
        if loaded is None:
            return
        _, qualifying = loaded
        for a in qualifying:
            self._close_notify_oracle("L3", a)
            if a.get("interruption") != "phase-deadline":
                self.error(f"L3 attempt {a.get('attempt')}: interruption != phase-deadline")

    def verify_cell_l4(self):
        loaded = self._qualified_cell("L4")
        if loaded is None:
            return
        _, qualifying = loaded
        for a in qualifying:
            self._close_notify_oracle("L4", a)
            if a.get("interruption") != "listener-cancellation":
                self.error(f"L4 attempt {a.get('attempt')}: interruption not listener-cancellation")
            if not a.get("proxy_port"):
                self.error(f"L4 attempt {a.get('attempt')}: no gated proxy port recorded")
            proxy_journal = a.get("proxy_journal")
            if not proxy_journal:
                self.error(f"L4 attempt {a.get('attempt')}: no proxy journal path recorded")
            if a.get("peer_listener_cancelled") is not True:
                self.error(f"L4 attempt {a.get('attempt')}: peer listener was not cancelled")

    # ---------------------------------------------------------------- L5 / L6
    def _verify_cap_attempt(self, cell: str, a: Dict[str, Any], plaintext_exact: Optional[int]):
        """Shared oracle for L5/L6 candidate cap attempts (E1 semantics, strict in R4)."""
        attempt = a.get("attempt")
        # R4 dispatch coverage: the registered implementation must be the one that ran.
        if a.get("dispatch_target") != DISPATCH_TARGETS[cell]:
            self.error(f"{cell} attempt {attempt}: dispatch_target {a.get('dispatch_target')!r} "
                        f"!= {DISPATCH_TARGETS[cell]!r} (registered implementation not invoked)")
        # Proxy rejection (R3 requirement 8): the ciphertext evidence must declare the
        # transport boundary beneath SslStream; proxy/app-byte substitutes are rejected.
        measurement = a.get("ciphertext_measurement")
        if measurement != MEASUREMENT_BOUNDARY:
            self.error(f"{cell} attempt {attempt}: ciphertext measurement {measurement!r} is not "
                       f"'{MEASUREMENT_BOUNDARY}' (proxy or estimated evidence rejected)")
        source = str(a.get("ciphertext_source", ""))
        if "proxy" in source.lower() or "application bytes" in source.lower():
            self.error(f"{cell} attempt {attempt}: ciphertext_source declares a proxy measurement: {source!r}")

        wire = a.get("encrypted_wire_bytes")
        plain = a.get("plaintext_bytes")
        declared_cap = a.get("declared_ciphertext_cap")
        overhead = a.get("tls_record_overhead_bytes_drain")
        if wire is None:
            self.error(f"{cell} attempt {attempt}: encrypted_wire_bytes (actual TLS ciphertext) missing")
            return
        if plain is None:
            self.error(f"{cell} attempt {attempt}: plaintext_bytes missing")
            return
        try:
            wire = int(wire)
            plain = int(plain)
        except (TypeError, ValueError):
            self.error(f"{cell} attempt {attempt}: non-numeric ciphertext counters")
            return

        if wire <= 0:
            self.error(f"{cell} attempt {attempt}: no ciphertext measured at the transport boundary")
        # The declared cap must equal the registered cap inside the qualifying cell.
        if declared_cap != REGISTERED_CIPHERTEXT_CAP:
            self.error(f"{cell} attempt {attempt}: declared ciphertext cap {declared_cap} "
                       f"!= registered {REGISTERED_CIPHERTEXT_CAP}")
        # STRICT encrypted enforcement (R4, the original E1 contract): inbound reads are
        # clipped to the remaining encrypted budget at the transport boundary, so the
        # tiny-flood cell must terminate EXACTLY at the declared cap and the large-flood cell
        # must stay inside the registered cap. A one-record overshoot allowance is NOT part of
        # the registered contract (E1 verifier: WireBytes == 32768) and is rejected.
        if cell == "L5":
            if wire != REGISTERED_CIPHERTEXT_CAP:
                self.error(f"{cell} attempt {attempt}: encrypted wire bytes {wire} != the strict "
                            f"registered cap {REGISTERED_CIPHERTEXT_CAP} (cap not enforced exactly)")
            if a.get("drain_outcome") != "cipher-cap":
                self.error(f"{cell} attempt {attempt}: drain terminated at {a.get('drain_outcome')!r} "
                            f"(expected the encrypted cap to bind for the tiny-flood cell)")
            # R4-R1 F2: the tiny-flood cell must actually have decrypted something at the
            # transport boundary. A cell claiming the encrypted cap bound while reporting zero
            # plaintext is an empty observation, not a cap measurement. This mirrors the binding
            # E1 contract, which requires a positive exact decrypted-byte value for T5.
            if plain <= 0:
                self.error(f"{cell} attempt {attempt}: plaintext bytes {plain} is not greater than "
                            f"zero — the tiny-flood drain decrypted nothing at the transport "
                            f"boundary (an empty observation, not a cap measurement)")
        else:
            if wire > REGISTERED_CIPHERTEXT_CAP:
                self.error(f"{cell} attempt {attempt}: encrypted wire bytes {wire} exceed the "
                            f"registered {REGISTERED_CIPHERTEXT_CAP} cap")
            if a.get("drain_outcome") != "plain-cap":
                self.error(f"{cell} attempt {attempt}: drain terminated at {a.get('drain_outcome')!r} "
                            f"(expected the plaintext cap to bind for the large-flood cell)")
        if a.get("checks", {}).get("encrypted_cap_strict") is not True:
            self.error(f"{cell} attempt {attempt}: checks.encrypted_cap_strict is not true")
        # Controlled differential (R3 requirement 5): TLS ciphertext and application byte
        # counts must differ; zero overhead means the counter is not wire measurement.
        if overhead is None or int(overhead) <= 0:
            self.error(f"{cell} attempt {attempt}: no TLS record overhead measured — ciphertext "
                       f"counter indistinguishable from an application-byte proxy")
        flood_app = a.get("flood_application_bytes")
        flood_wire = a.get("flood_encrypted_wire_bytes")
        if flood_app is None or flood_wire is None or int(flood_app) <= 0 or int(flood_wire) < int(flood_app):
            self.error(f"{cell} attempt {attempt}: flooding client counters inconsistent "
                       f"(app={flood_app}, wire={flood_wire})")
        # Plaintext enforcement: exact clip at the registered 8192-byte budget.
        if plaintext_exact is None:
            if plain >= REGISTERED_PLAINTEXT_CAP:
                self.error(f"{cell} attempt {attempt}: plaintext {plain} reached the "
                           f"{REGISTERED_PLAINTEXT_CAP} cap (encrypted cap must bind first for "
                           f"the tiny-flood cell)")
        else:
            if plain != plaintext_exact:
                self.error(f"{cell} attempt {attempt}: plaintext {plain} != exact enforced cap "
                           f"{plaintext_exact}")
        # Production TLS terminal path preserved inside the cell (R1 continuity).
        if a.get("production_tls_terminal_observed") is not True:
            self.error(f"{cell} attempt {attempt}: production TLS terminal path not observed")
        # The candidate drain must have terminated at a cap, not at EOF or a fault.
        if a.get("drain_outcome") not in ("cipher-cap", "plain-cap"):
            self.error(f"{cell} attempt {attempt}: drain terminated at {a.get('drain_outcome')!r} "
                       f"(expected a registered cap bound)")
        checks = a.get("checks", {})
        for required in ("plaintext_cap_enforced", "encrypted_cap_strict",
                         "encrypted_bound_versus_registered", "ciphertext_app_differential_observed",
                         "client_consistency", "accounting_active_zero", "accounting_balanced"):
            if checks.get(required) is not True:
                self.error(f"{cell} attempt {attempt}: checks.{required} is not true")

    def verify_cell_l5(self):
        loaded = self._qualified_cell("L5")
        if loaded is None:
            return
        _, qualifying = loaded
        # Cap cells carry discovery bound 3 and no repeats per the registered plan.
        if not (1 <= len(qualifying) <= 3):
            self.error(f"L5: qualifying attempt count {len(qualifying)} outside the registered 1..3 bound")
        for a in qualifying:
            self._verify_cap_attempt("L5", a, plaintext_exact=None)

    def verify_cell_l6(self):
        loaded = self._qualified_cell("L6")
        if loaded is None:
            return
        _, qualifying = loaded
        if not (1 <= len(qualifying) <= 3):
            self.error(f"L6: qualifying attempt count {len(qualifying)} outside the registered 1..3 bound")
        for a in qualifying:
            self._verify_cap_attempt("L6", a, plaintext_exact=REGISTERED_PLAINTEXT_CAP)

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
            # R4 dispatch coverage: the production plaintext cap implementation must be the
            # one that ran for the supplementary cell.
            if a.get("dispatch_target") != DISPATCH_TARGETS[cell]:
                self.error(f"{cell} attempt {a.get('attempt')}: dispatch_target "
                            f"{a.get('dispatch_target')!r} != {DISPATCH_TARGETS[cell]!r} "
                            f"(registered implementation not invoked)")
            if a.get("plaintext_bytes") != REGISTERED_PLAINTEXT_CAP:
                self.warn(f"{cell} attempt {a.get('attempt')}: plaintext drain != {REGISTERED_PLAINTEXT_CAP}")
            if a.get("ciphertext_bytes") != 0:
                self.warn(f"{cell} attempt {a.get('attempt')}: ciphertext bytes != 0")
            if a.get("checks", {}).get("drain_admitted") is not True:
                self.error(f"{cell} attempt {a.get('attempt')}: production drain was never admitted "
                            f"(permit pool exhausted by the listener configuration)")

    # ---------------------------------------------------------------- L7
    def verify_cell_l7(self):
        loaded = self._qualified_cell("L7")
        if loaded is None:
            return
        _, qualifying = loaded
        for a in qualifying:
            attempt = a.get("attempt")
            # R4 dispatch coverage: L7 must have reached the receipt path (this was dead code
            # in R1-R3 — every production cell was routed through the terminal-write path).
            if a.get("dispatch_target") != DISPATCH_TARGETS["L7"]:
                self.error(f"L7 attempt {attempt}: dispatch_target {a.get('dispatch_target')!r} "
                            f"!= {DISPATCH_TARGETS['L7']!r} (registered receipt implementation "
                            f"not invoked — dead dispatch)")
            checks = a.get("checks", {})
            for required in ("receipt_present", "peer_eof", "receipt_matches_campaign_digest",
                             "peer_unread_at_end_zero", "accounting_active_zero",
                             "accounting_balanced", "single_connection", "completed",
                             "terminal_message_processed"):
                if checks.get(required) is not True:
                    self.error(f"L7 attempt {attempt}: checks.{required} is not true")
            # The peer receipt journal itself must exist, re-validate, and be BOUND to the
            # harness expectation (R4-R1 F1). The R3/R4 defect class was a harness-side
            # expectation that disagreed with the bytes the peer actually received; the
            # verifier previously only rejected a receipt that was self-inconsistent. A
            # missing, malformed, or contradictory value now fails verification.
            # R4-R2 F6: exactly ONE peer-receipt record per qualifying attempt. The
            # previous loader returned only the first match, which silently accepted
            # duplicate, contradictory, or unrelated appended receipt records.
            receipts = self.load_peer_receipts("L7", attempt)
            if not receipts:
                self.error(f"L7 attempt {attempt}: peer receipt journal record missing")
            else:
                receipt = receipts[0]
                if len(receipts) > 1:
                    self.error(f"L7 attempt {attempt}: exactly one peer-receipt record is "
                               f"required, found {len(receipts)} (duplicate, contradictory, or "
                               f"unrelated appended receipt records are rejected)")
                    for extra in receipts:
                        if extra.get("attempt") != attempt or extra.get("cell") != "L7":
                            self.error(f"L7 attempt {attempt}: a receipt record in this journal "
                                       f"belongs to attempt {extra.get('attempt')!r} cell "
                                       f"{extra.get('cell')!r} (receipt is not bound to this "
                                       f"attempt)")
                import hashlib
                hex_msg = receipt.get("message_hex", "")
                digest = None
                if not isinstance(hex_msg, str) or hex_msg == "":
                    self.error(f"L7 attempt {attempt}: peer receipt message_hex is missing")
                else:
                    try:
                        digest = hashlib.sha256(bytes.fromhex(hex_msg)).hexdigest()
                    except ValueError:
                        self.error(f"L7 attempt {attempt}: peer receipt message_hex is malformed "
                                   f"and cannot be re-encoded")
                    if digest is not None and digest != receipt.get("sha256"):
                        self.error(f"L7 attempt {attempt}: peer receipt digest does not match its own hex payload")
                # Association: the receipt must belong to this attempt and to this cell.
                if receipt.get("attempt") != attempt:
                    self.error(f"L7 attempt {attempt}: peer receipt is associated with attempt "
                               f"{receipt.get('attempt')!r} (receipt is not bound to this attempt)")
                if receipt.get("cell") != "L7":
                    self.error(f"L7 attempt {attempt}: peer receipt is associated with cell "
                               f"{receipt.get('cell')!r}")
                # Expected-observation binding: each declared expectation must equal the
                # observation recorded by the framing-aware peer, and the declared byte count
                # must equal the actual framed payload length.
                expected_sha = a.get("expected_terminal_sha256")
                if expected_sha is None:
                    self.error(f"L7 attempt {attempt}: expected_terminal_sha256 missing — the "
                               f"harness did not declare the terminal message it expects")
                elif not (isinstance(expected_sha, str) and self._is_sha256_hex(expected_sha)):
                    self.error(f"L7 attempt {attempt}: expected_terminal_sha256 "
                               f"{expected_sha!r} is not a 64-character lowercase hex sha256")
                elif digest is not None and expected_sha != receipt.get("sha256"):
                    self.error(f"L7 attempt {attempt}: expected_terminal_sha256 {expected_sha} "
                               f"does not match the peer receipt sha256 {receipt.get('sha256')} "
                               f"(the harness expectation disagrees with the bytes the peer received)")
                expected_bytes = a.get("expected_terminal_message_bytes")
                receipt_bytes = receipt.get("message_bytes")
                if expected_bytes is None:
                    self.error(f"L7 attempt {attempt}: expected_terminal_message_bytes missing — "
                               f"the harness did not declare the terminal message length")
                elif not self._is_int(expected_bytes):
                    self.error(f"L7 attempt {attempt}: expected_terminal_message_bytes "
                               f"{expected_bytes!r} is not an integer")
                elif receipt_bytes is None:
                    self.error(f"L7 attempt {attempt}: peer receipt message_bytes missing")
                elif not self._is_int(receipt_bytes):
                    self.error(f"L7 attempt {attempt}: peer receipt message_bytes "
                               f"{receipt_bytes!r} is not an integer")
                else:
                    if expected_bytes != receipt_bytes:
                        self.error(f"L7 attempt {attempt}: expected_terminal_message_bytes "
                                   f"{expected_bytes} does not match the peer receipt message_bytes "
                                   f"{receipt_bytes} (the harness expectation disagrees with the "
                                   f"bytes the peer received)")
                    elif isinstance(hex_msg, str) and expected_bytes != len(hex_msg) // 2:
                        self.error(f"L7 attempt {attempt}: expected_terminal_message_bytes "
                                   f"{expected_bytes} does not match the actual framed payload "
                                   f"length {len(hex_msg) // 2}")

    @staticmethod
    def _is_sha256_hex(value: Any) -> bool:
        return isinstance(value, str) and len(value) == 64 and \
            all(c in "0123456789abcdef" for c in value)

    @staticmethod
    def _is_int(value: Any) -> bool:
        return isinstance(value, int) and not isinstance(value, bool)

    # ---------------------------------------------------------------- controls
    def verify_negative_controls(self):
        for nc in ["NC-1", "NC-2"]:
            path = self.results_dir / "negative-controls" / f"{nc}.jsonl"
            records = self.load_journal(path)
            if not records:
                self.error(f"{nc}: no records")
                continue
            for r in records:
                if r.get("dispatch_target") != DISPATCH_TARGETS[nc]:
                    self.error(f"{nc}: dispatch_target {r.get('dispatch_target')!r} "
                                f"!= {DISPATCH_TARGETS[nc]!r} (registered control implementation "
                                f"not invoked)")
                if nc == "NC-1":
                    if r.get("registered_ciphertext_cap") != REGISTERED_CIPHERTEXT_CAP or \
                            r.get("tampered_ciphertext_cap") != REGISTERED_NC1_TAMPERED_CAP:
                        self.error(f"{nc}: cap tamper values not recorded")
                    # Actual execution (R3): the tampered drain must have measured real
                    # ciphertext wire bytes beyond the registered cap.
                    wire = r.get("encrypted_wire_bytes")
                    if wire is None:
                        self.error(f"{nc}: no encrypted_wire_bytes observed — control not actually executed")
                    else:
                        try:
                            wire = int(wire)
                        except (TypeError, ValueError):
                            wire = 0
                        if wire != REGISTERED_NC1_TAMPERED_CAP:
                            self.error(f"{nc}: observed wire bytes {wire} != the tampered cap "
                                        f"{REGISTERED_NC1_TAMPERED_CAP} (strict enforcement not "
                                        f"exercised by the tamper control)")
                        if wire <= REGISTERED_CIPHERTEXT_CAP:
                            self.error(f"{nc}: tamper not detected — observed wire bytes {wire} "
                                       f"did not exceed the registered {REGISTERED_CIPHERTEXT_CAP} cap")
                    if r.get("tamper_detected") is not True:
                        self.error(f"{nc}: tamper_detected flag not recorded as true")
                    if r.get("ciphertext_measurement") != MEASUREMENT_BOUNDARY:
                        self.error(f"{nc}: ciphertext measurement is not the transport boundary beneath SslStream")
                elif nc == "NC-2":
                    if r.get("bound_deadline_ms") != 500 or r.get("extended_deadline_ms") != 800:
                        self.error(f"{nc}: deadline values not recorded")
                    if r.get("elapsed_ms", 0) <= 500:
                        self.error(f"{nc}: extended deadline not exceeded")
                    if r.get("outcome") != "induced-but-checks-failed":
                        self.error(f"{nc}: outcome {r.get('outcome')!r} != induced-but-checks-failed "
                                   f"(registered control detection not recorded)")
                    # R4: the deadline-renewal control depends on a profile that actually induces
                    # the terminal-write pendency. An unconfirmed prerequisite (the R3 defect, which
                    # ran on a profile that never induced pendency and reported the precursor as
                    # unobserved with elapsed ~2.6 ms) is rejected, so the extended deadline must
                    # have been genuinely exercised.
                    if r.get("prerequisite_confirmed") is not True:
                        self.error(f"{nc}: prerequisite not confirmed — the deadline-renewal control "
                                   f"was not run on a profile that induced the terminal-write "
                                   f"pendency it depends on")
                    if r.get("deadline_tamper_detected") is not True:
                        self.error(f"{nc}: deadline_tamper_detected not recorded true — control "
                                   f"execution not evidenced")

    def verify_repeatability(self):
        cells = ["L1", "L2", "L3", "L4", "L5", "L6", "L7"]
        for cell in cells:
            summary = self.load_cell_summary(cell)
            if not summary:
                continue
            if not summary.get("qualified", False):
                continue
            # Repeats apply only to cells registered with repeat_on_qualified (L1-L4, L7);
            # cap cells (L5/L6) carry discovery bound 3 and no repeats.
            repeats_required = summary.get("repeats_required", 2)
            expected_successes = 1 + repeats_required
            if summary.get("successes") != expected_successes:
                self.error(f"{cell}: expected {expected_successes} successes "
                           f"(1 discovery + {repeats_required} repeats), got {summary.get('successes')}")
            if summary.get("runs", 0) > 6:
                self.error(f"{cell}: runs {summary.get('runs')} > max 6")

    def verify_guarantee_levels(self):
        journal = self.results_dir / "campaign-journal.jsonl"
        records = self.load_journal(journal)
        for a in records:
            if a.get("record_type") != "attempt" or a.get("cell") not in ("L1", "L3"):
                continue
            if a.get("outcome") != QUALIFYING_OUTCOMES[a.get("cell")]:
                continue
            levels = a.get("guarantee_levels", {})
            if a.get("cell") == "L1":
                if levels.get("local_write_completion") is not False:
                    self.error("L1: local_write_completion should be False")
                if levels.get("tls_close_notify_transmission") is not False:
                    self.error("L1: tls_close_notify_transmission should be False")
                if levels.get("peer_receipt") is not False:
                    self.error("L1: peer_receipt should be False")
                if levels.get("verified_peer_receipt") is not False:
                    self.error("L1: verified_peer_receipt should be False")
            if a.get("cell") == "L3":
                if levels.get("tls_close_notify_transmission") is not True:
                    self.error("L3: tls_close_notify_transmission should be True (candidate)")

    def run(self) -> Tuple[bool, List[str], List[str]]:
        self.verify_fresh_run()
        self.verify_commit_tree()
        self.verify_dispatch_audit()
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
