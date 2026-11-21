#!/usr/bin/env python3
"""Generate the Q-B5B-09-PLAIN-OWNER-01 evidence manifest over Git blob payloads.

Hashes are taken from the **Git index**, so the recorded SHA-256 and byte counts are the
bytes that the commit will actually contain, independent of checkout line-ending
normalization and independent of the working tree.

Run after staging the owner package and before committing:

    git add -f docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01
    git add docs/decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md
    python docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/generate-manifest.py
    git add -f docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/manifest.json
"""

import hashlib
import json
import subprocess
import sys
from pathlib import Path

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parent.parent.parent.parent

PACKAGE_DIR = "docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01"
DECISION = "docs/decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md"

EXCLUDED = {
    PACKAGE_DIR + "/manifest.json",
    PACKAGE_DIR + "/verify-evidence.py",
}

BASE = "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f"
SESSION_A = "96a1a3cd0f059b554c6e7ab2ce099ef29b161ace"
SESSION_B = "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f"

PINNED = {
    "ownerAuthorization": "fac10f6c5ece37dbc3f38c9c831c668d49a3d290",
    "typedLinger": "dac52d55ade0a9ae703b801c8a4066e8aef2bd58",
    "originalPlaintext": "20fbc461c0257620db28fb78a463218b242455bc",
    "independentQ1": "9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c",
    "r1Implementation": "6a40c4b84993dec77deb165cef31e419df05be2e",
    "r1Evidence": "d7e5ac14704a9576813e32692b29050ef92ee7b8",
    "independentQ2SessionA": SESSION_A,
    "independentQ2SessionB": SESSION_B,
}


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout


def staged_paths():
    raw = git("ls-files", "-s", "--", PACKAGE_DIR, DECISION).decode()
    paths = []
    for line in raw.splitlines():
        meta, path = line.split("\t", 1)
        paths.append((meta.split()[1], path))
    return paths


def blob_bytes(git_blob):
    return git("cat-file", "blob", git_blob)


def source_blob_id(rev, path):
    data = git_optional("rev-parse", "%s:%s" % (rev, path))
    return data.decode().strip() if data else None


def check_preserved_identity(staged):
    """Every restored file must commit with the same Git blob ID as its source."""
    problems = []
    sources = {
        "q2-session-a": SESSION_A,
        "q2-session-b": SESSION_B,
    }
    source_root = "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2"
    for name, rev in sources.items():
        prefix = PACKAGE_DIR + "/" + name + "/"
        for git_blob, path in staged:
            if not path.startswith(prefix):
                continue
            rel = path[len(prefix):]
            if rel == "PROVENANCE.md":
                continue
            if rel == ".gitattributes":
                continue
            if rel.startswith(".gitattributes.session-"):
                expected = source_blob_id(rev, source_root + "/.gitattributes")
            elif rel.startswith("report/"):
                expected = source_blob_id(rev, "docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md")
            else:
                expected = source_blob_id(rev, source_root + "/" + rel)
            if expected != git_blob:
                problems.append("%s: staged %s expected %s" % (path, git_blob, expected))
    return problems


def git_optional(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


def main():
    staged = staged_paths()
    problems = check_preserved_identity(staged)
    if problems:
        raise RuntimeError("restored files would not commit byte-exactly:\n  " +
                           "\n  ".join(problems))

    entries = []
    for git_blob, path in staged:
        if path in EXCLUDED:
            continue
        data = blob_bytes(git_blob)
        entries.append({
            "path": path,
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
            "gitBlob": git_blob,
        })
    entries.sort(key=lambda item: item["path"])

    if not any(entry["path"] == DECISION for entry in entries):
        raise RuntimeError("owner decision is not staged: %s" % DECISION)

    manifest = {
        "package": "Q-B5B-09-PLAIN-OWNER-01",
        "disposition": "PLAINTEXT REMEDIATION - ACCEPTED WITH QUALIFICATIONS - CLOSED",
        "parentQualificationRemainsOpen": "Q-B5B-09",
        "hashSemantics": "SHA-256 and byte count over exact Git blob payload bytes",
        "baseCommit": BASE,
        "baseTree": git("rev-parse", "%s^{tree}" % BASE).decode().strip(),
        "pinnedRevisions": PINNED,
        "preservedPackages": {
            "q2-session-a": {
                "commit": SESSION_A,
                "source": "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2",
                "restored": PACKAGE_DIR + "/q2-session-a",
            },
            "q2-session-b": {
                "commit": SESSION_B,
                "source": "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2",
                "restored": PACKAGE_DIR + "/q2-session-b",
            },
        },
        "discrepancies": [
            {"id": "D-01", "subject": "session B modified session A's evidence package in place"},
            {"id": "D-02", "subject": "session B manifest carries no hash or byte content"},
            {"id": "D-03", "subject": "session B verifier cannot pass on its own commit"},
            {"id": "D-04", "subject": "session A verifier is checkout-dependent (CRLF)"},
            {"id": "D-05", "subject": "session B report links to a non-existent OBS-01 proposal"},
            {"id": "D-06", "subject": "suite naming and total-count inconsistency in session B"},
            {"id": "D-07", "subject": "session B format check was scoped to the four R1 paths"},
            {"id": "D-08", "subject": "negative-control platform coverage differs"},
            {"id": "D-09", "subject": "observer-retention measurement exists only in session A"},
            {"id": "D-10", "subject": "session B backpressure files contain canonical-oracle output"},
            {"id": "D-11", "subject": "session B attributes rewrite inherited CRLF evidence blobs; package is not checkout-clean"},
        ],
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
