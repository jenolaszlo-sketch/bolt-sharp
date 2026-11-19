#!/usr/bin/env python3
"""Q-B5B-05-Q1 evidence manifest generator (reviewer-owned, independent of the producer tooling).

Hashes every package file except this generator, the verifier and the manifest itself, over Git
blob payloads, so the manifest is independent of checkout line-ending normalization.

Usage: python docs/reviews/evidence/Q-B5B-05-Q1/generate-manifest.py <revision>

Hashes the staged index blobs of the package files (so the manifest reflects exactly the
content that will be committed), over Git blob payloads.
"""
import hashlib
import json
import pathlib
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parents[4]
REVISION = sys.argv[1] if len(sys.argv) > 1 else "HEAD"
PACKAGE = "docs/reviews/evidence/Q-B5B-05-Q1"
REPORT = "docs/reviews/Q-B5B-05-Q1-independent-qualification.md"
SELF_EXCLUDED = {
    PACKAGE + "/manifest.json",
    PACKAGE + "/generate-manifest.py",
    PACKAGE + "/verify-evidence.py",
}

def git(*args, binary=True):
    proc = subprocess.run(["git", "-C", str(REPO), *args], capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout if binary else proc.stdout.decode()


def index_entries(path):
    lines = git("ls-files", "-s", "--", path, binary=False).strip().splitlines()
    out = {}
    for line in lines:
        meta, p = line.split("\t", 1)
        mode, blob, stage = meta.split()
        out[p] = blob
    return out


def blob(rev, path):
    proc = subprocess.run(["git", "-C", str(REPO), "cat-file", "blob", "%s:%s" % (rev, path)],
                          capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


paths = sorted(p for p in index_entries(PACKAGE) if p not in SELF_EXCLUDED)
if REPORT in index_entries(REPORT):
    paths.insert(0, REPORT)

files = []
for path in paths:
    blob_id = index_entries(path)[path]
    proc = subprocess.run(["git", "-C", str(REPO), "cat-file", "blob", blob_id],
                          capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("blob missing: %s" % blob_id)
    data = proc.stdout
    files.append({
        "gitBlob": blob_id,
        "path": path,
        "sha256": hashlib.sha256(data).hexdigest(),
        "bytes": len(data),
    })

manifest = {
    "baseCommit": REVISION if REVISION != "HEAD" else git("rev-parse", "HEAD", binary=False).strip(),
    "commission": "Q-B5B-05-Q1",
    "disposition": "SUPPORTED WITH QUALIFICATIONS",
    "evidencePackage": PACKAGE,
    "fileCount": len(files),
    "files": files,
    "totalBytes": sum(f["bytes"] for f in files),
}
out = REPO / PACKAGE / "manifest.json"
out.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
print("manifest written: %d files, %d bytes" % (len(files), manifest["totalBytes"]))