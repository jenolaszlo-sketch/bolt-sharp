#!/usr/bin/env python3
"""Generate the Q-B5B-05-PROBE-01 evidence manifest over Git blob payloads.

Hashes are taken from the Git index, so the recorded SHA-256 and byte counts are the bytes the
commit will actually contain, independent of checkout line-ending normalization.

Run after staging the evidence package, the producer report and the probe project:

    git add -f docs/reviews/evidence/Q-B5B-05-PROBE-01
    git add docs/reviews/Q-B5B-05-PROBE-01-producer-report.md
    git add -f docs/reviews/probes/Q-B5B-05-PROBE-01
    python docs/reviews/evidence/Q-B5B-05-PROBE-01/generate-manifest.py
    git add -f docs/reviews/evidence/Q-B5B-05-PROBE-01/manifest.json
"""

import hashlib
import json
import subprocess
import sys
from pathlib import Path

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parents[3]

PACKAGE_DIR = "docs/reviews/evidence/Q-B5B-05-PROBE-01"
PROBE_DIR = "docs/reviews/probes/Q-B5B-05-PROBE-01"
REPORT = "docs/reviews/Q-B5B-05-PROBE-01-producer-report.md"

EXCLUDED = {
    PACKAGE_DIR + "/manifest.json",
    PACKAGE_DIR + "/verify-evidence.py",
    PACKAGE_DIR + "/q5b05-evidence.json",
}

BASE = "e0b762a5ae716544c7d3a5fa3f4e7c08372b7ac1"

TRX_EXPECTED = {
    "q5b05-probe.trx": (13, 13, 0),
    "q5b05-probe-wsl2.trx": (13, 13, 0),
    "q5b05-core-windows.trx": (1257, 1257, 0),
    "q5b05-core-wsl2.trx": (1257, 1257, 0),
    "q5b05-driver-interop-windows.trx": (43, 43, 0),
    "q5b05-driver-interop-wsl2.trx": (43, 43, 0),
}


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout


def staged_paths():
    raw = git("ls-files", "-s", "--", PACKAGE_DIR, PROBE_DIR, REPORT).decode()
    out = []
    for line in raw.splitlines():
        meta, path = line.split("\t", 1)
        out.append((meta.split()[1], path))
    return out


def main():
    staged = staged_paths()

    has_probe = any(p.startswith(PROBE_DIR + "/") for _, p in staged)
    has_report = any(p == REPORT for _, p in staged)
    has_evidence = any(p.startswith(PACKAGE_DIR + "/") for _, p in staged)
    if not (has_probe and has_report and has_evidence):
        raise RuntimeError("probe source, producer report and evidence must all be staged")

    entries = []
    for git_blob, path in staged:
        if path in EXCLUDED:
            continue
        data = git("cat-file", "blob", git_blob)
        entries.append({
            "path": path,
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
            "gitBlob": git_blob,
        })
    entries.sort(key=lambda item: item["path"])

    # Build artifacts under bin/ and obj/ are ignored; nothing else may be staged.
    artifacts = [p for _, p in staged if "/bin/" in p or "/obj/" in p]
    if artifacts:
        raise RuntimeError("build artifacts must not be staged: %s" % ", ".join(artifacts[:5]))

    manifest = {
        "commission": "Q-B5B-05-PROBE-01",
        "disposition": "PRODUCTION LIMITATION IDENTIFIED - OWNER DECISION REQUIRED",
        "parentQualificationStatus": "Q-B5B-05 remains OPEN",
        "hashSemantics": "SHA-256 and byte count over exact Git blob payload bytes",
        "baseCommit": BASE,
        "baseTree": git("rev-parse", "%s^{tree}" % BASE).decode().strip(),
        "producerReport": REPORT,
        "probeProject": PROBE_DIR,
        "evidencePackage": PACKAGE_DIR,
        "trxExpected": {name: list(values) for name, values in TRX_EXPECTED.items()},
        "negativeControls": {"total": 4, "detected": 4},
        "fileCount": len(entries),
        "totalBytes": sum(entry["bytes"] for entry in entries),
        "files": entries,
    }

    (EVIDENCE / "manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print("manifest: %d files, %d bytes" % (manifest["fileCount"], manifest["totalBytes"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())