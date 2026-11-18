"""Verify the Q2 qualification evidence over Git blob bytes."""

import hashlib
import io
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

Q2 = "Q-B5B-09-PLAIN-Q2"
ROOT = f"docs/reviews/evidence/{Q2}"
REPORT = f"docs/reviews/{Q2}-qualification.md"
R1_IMPL = "6a40c4b84993dec77deb165cef31e419df05be2e"
R1_IMPL_TREE = "a23edab4242711fffc28da63f254ff0f8b4d3082"
R1_EVIDENCE = "d7e5ac14704a9576813e32692b29050ef92ee7b8"
R1_EVIDENCE_TREE = "b98b57723ac6e8754961261e235a74013ca76058"
Q1 = "9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c"
Q1_TREE = "f0f1d38dbe648ee36c07a490f6217b47a179dd16"
BASELINE = "20fbc461c0257620db28fb78a463218b242455bc"
BASELINE_TREE = "116ee015f1ae9a30bf991c2db470996db47996c0"
TYPED_LINGER = "dac52d55ade0a9ae703b801c8a4066e8aef2bd58"
MANIFEST = f"{ROOT}/manifest.json"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require(condition, what):
    if not condition:
        raise ValueError(what)


def git(*args):
    return subprocess.check_output(["git", *args])


def identity(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def verify(rev):
    def blob(path):
        return git("cat-file", "blob", f"{rev}:{path}")

    # Verify Q2 qualification commit structure
    require(git("show", "-s", "--format=%P", rev).decode().strip() == R1_EVIDENCE,
            "Unexpected Q2 qualification parent (should be R1 evidence commit)")
    require(git("rev-parse", f"{rev}:docs/decisions").decode().strip() ==
            git("rev-parse", f"{R1_EVIDENCE}:docs/decisions").decode().strip(),
            "Owner-decision tree changed")

    # Verify historical documentation unchanged from Q1
    require(not git("diff", "--diff-filter=DMRT", "--name-only", Q1, rev, "--", "docs").decode().strip(),
            "Historical documentation modified since Q1")

    # Load and verify manifest
    m = json.loads(blob(MANIFEST))
    files = m["files"]

    # Verify commit scope (additions only under evidence package)
    delta = {tuple(row.split("\t", 1)) for row in
             git("diff", "--name-status", R1_EVIDENCE, rev).decode().splitlines()}
    expected_additions = {("A", p) for p in set(files) | {MANIFEST}}
    require(delta == expected_additions, f"Unexpected commit scope: {delta} vs {expected_additions}")

    # Verify all file hashes
    for path, expected in files.items():
        require(identity(blob(path)) == expected, f"Blob mismatch: {path}")

    # Verify campaign JSON files exist and have expected structure
    for campaign_file in [
        "campaign/baseline-f01-win.json",
        "campaign/r1-f01-win.json",
        "campaign/r1-all-win.json",
        "campaign/r1-backpressure-win.json",
        "campaign/baseline-f01-wsl.json",
        "campaign/r1-f01-wsl.json",
        "campaign/r1-all-wsl.json",
        "campaign/r1-backpressure-wsl.json",
    ]:
        data = json.loads(blob(campaign_file))
        require("platform" in data, f"Missing platform in {campaign_file}")

    print(f"PASS {len(files)} blobs, manifest verified, campaign structure OK")
    print(git("show", "-s", "--format=%H %T", rev).decode().strip())


if __name__ == "__main__":
    verify(sys.argv[1] if len(sys.argv) > 1 else "HEAD")