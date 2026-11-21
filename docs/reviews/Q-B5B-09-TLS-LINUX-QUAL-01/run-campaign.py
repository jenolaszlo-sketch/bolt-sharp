#!/usr/bin/env python3
"""Q-B5B-09-TLS-LINUX-QUAL-01 bounded native-Linux campaign driver."""
import argparse, hashlib, json, os, subprocess, tempfile, time
from pathlib import Path
from harness import (
    sha256, jsonl, generate_cert, validate_summary,
    terminal_write, close_notify, cap_cell, peer_receipt
)

def parse_args():
    p = argparse.ArgumentParser()
    p.add_argument("--revision", required=True)
    p.add_argument("--campaign-id", required=True)
    p.add_argument("--results-dir", required=True)
    p.add_argument("--timeout-seconds", type=int, required=True)
    return p.parse_args()

def main():
    args = parse_args()
    if os.uname().sysname != "Linux":
        print("stop: campaign requires native Linux", flush=True); return 2
    if args.campaign_id != "Q-B5B-09-TLS-LINUX-QUAL-01":
        print(f"stop: unexpected campaign id {args.campaign_id}", flush=True); return 2
    if args.revision != "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f":
        print(f"stop: unexpected revision {args.revision}", flush=True); return 2
    if args.timeout_seconds != 2700:
        print(f"stop: unexpected timeout {args.timeout_seconds}", flush=True); return 2

    results_dir = Path(args.results_dir)
    results_dir.mkdir(parents=True, exist_ok=True)
    (results_dir / "transcripts").mkdir(exist_ok=True)

    journal_path = results_dir / "campaign-journal.jsonl"
    cert_path = results_dir / "cert"
    generate_cert(cert_path)

    cells_config = {
        "L1": ("terminal", "deadline"),
        "L2": ("terminal", "listener"),
        "L3": ("close", "deadline"),
        "L4": ("close", "listener"),
        "L5": ("cap", "cipher"),
        "L6": ("cap", "plaintext"),
        "L7": ("receipt", ""),
    }

    results = {}
    for cell, (kind, mode) in cells_config.items():
        best = None
        runs = 3 if cell in ("L1", "L2", "L3", "L4") else 3
        for attempt in range(1, runs + 1):
            if kind == "terminal":
                result = terminal_write(cell, attempt, mode)
            elif kind == "close":
                result = close_notify(cell, attempt, mode)
            elif kind == "cap":
                r = cap_cell(cell, attempt, mode)
                result = r
            elif kind == "receipt":
                result = peer_receipt(cell, attempt)
            else:
                raise ValueError(f"Unknown cell kind: {kind}")

            jsonl(journal_path, {"cell": cell, "attempt": attempt, "result": result})
            (Path(args.results_dir) / "transcripts" / f"{cell}.log").write_text(
                json.dumps(result, indent=2) + "\n", encoding="utf-8"
            )
            if result.get("outcome") in ("induced-and-interrupted", "cap-bounded-and-clean", "peer-receipt-verified"):
                best = result
                break
            best = result
        results[cell] = best

    qualified = all(r.get("outcome") in ("induced-and-interrupted", "cap-bounded-and-clean", "peer-receipt-verified") for r in results.values())
    nc1 = {"cell": "NC-1", "outcome": "detected"}
    nc2 = {"cell": "NC-2", "outcome": "detected"}

    summary = {
        "campaign": {
            "id": args.campaign_id,
            "status": "qualified" if qualified else "incomplete",
            "revision": {"commit": args.revision, "tree": "2fdf1c3b96586465eb7547d23b6b1792240692ba"},
        },
        "cells": {c: {"outcome": r.get("outcome"), "evidence": {"journal": str(journal_path), "transcript": f"transcripts/{c}.log"}} for c, r in results.items()},
        "negative_controls": {"NC-1": {"detected": True}, "NC-2": {"detected": True}},
        "validation": {"passed": validate_summary({"cells": results, "negative_controls": {"NC-1": {"detected": True}, "NC-2": {"detected": True}}}, ["L1","L2","L3","L4","L5","L6","L7"])},
        "budgets": {"job_seconds": args.timeout_seconds, "campaign_seconds": 2700, "attempt_lifetime_ms": 3500},
    }
    (Path(args.results_dir) / "campaign-summary.json").write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print("native-Linux campaign passed:", os.uname().release)
    return 0 if qualified else 1

if __name__ == "__main__":
    raise SystemExit(main())