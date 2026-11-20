#!/usr/bin/env python3
"""Q-B5B-09-TLS-LINUX-QUAL-01 preparation driver.

This is a preparation-only driver. It validates the native-Linux gate and writes
the campaign summary. It does not implement the bounded campaign and must not be
dispatched before owner authorization and an independently reviewed campaign
driver are available.
"""

import argparse
import json
import os
from pathlib import Path

PLAN = Path(__file__).with_name("campaign-plan.json")


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--revision", required=True)
    parser.add_argument("--campaign-id", required=True)
    parser.add_argument("--results-dir", required=True)
    parser.add_argument("--timeout-seconds", type=int, required=True)
    return parser.parse_args()


def main():
    args = parse_args()
    system = os.uname().sysname
    if system != "Linux":
        print(f"stop: campaign requires native Linux; runner is {system}", flush=True)
        return 2
    if args.campaign_id != "Q-B5B-09-TLS-LINUX-QUAL-01":
        print(f"stop: unexpected campaign id {args.campaign_id}", flush=True)
        return 2
    if args.revision != "7429c6bb36ad9bf6a337e87870d7f4c3d7e90d7f":
        print(f"stop: unexpected revision {args.revision}", flush=True)
        return 2
    if args.timeout_seconds != 2700:
        print(f"stop: unexpected timeout {args.timeout_seconds}", flush=True)
        return 2

    results = Path(args.results_dir)
    results.mkdir(parents=True, exist_ok=True)
    plan = json.loads(PLAN.read_text(encoding="utf-8"))
    summary = {
        "campaign": {
            "id": args.campaign_id,
            "status": "prepared",
            "revision": {"commit": args.revision, "tree": "2fdf1c3b96586465eb7547d23b6b1792240692ba"},
            "note": "preparation driver; owner-authorized native-Linux campaign not executed",
        },
        "matrix": plan["matrix"],
        "budgets": plan.get("budgets", {}),
    }
    (results / "campaign-summary.json").write_text(
        json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8"
    )
    print(f"native-Linux preparation driver passed: {os.uname().release}")
    print(f"wrote {results / 'campaign-summary.json'}")


if __name__ == "__main__":
    raise SystemExit(main())
