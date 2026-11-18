#!/usr/bin/env python3
"""Materialize both independent Q2 qualification packages byte-exactly.

The parallel Q2 qualification at commit 3c2144a modified files of the package committed
at 96a1a3c in place rather than adding a separate package, so the directory
`docs/reviews/evidence/Q-B5B-09-PLAIN-Q2` at HEAD is a merge of two sessions and is
neither session's package.

This script restores each session's package verbatim from its pinned commit, using
`git cat-file blob` payload bytes only. It never reads the working tree, never modifies
history and never overwrites an existing evidence file outside this owner package.

Usage:
    python docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/extract-q2-packages.py [--check]

`--check` verifies the materialized copies against the pinned commits without writing.
"""

import hashlib
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
OWNER_EVIDENCE = Path(__file__).resolve().parent

SOURCE_PACKAGE = "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2"
SOURCE_REPORT = "docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md"

SESSIONS = [
    {
        "id": "a",
        "commit": "96a1a3cd0f059b554c6e7ab2ce099ef29b161ace",
        "label": "Session A - first independent Q2 qualification",
    },
    {
        "id": "b",
        "commit": "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f",
        "label": "Session B - parallel independent Q2 qualification",
    },
]


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout


def blob(commit, path):
    return git("cat-file", "blob", "%s:%s" % (commit, path))


def tree_of(commit):
    return git("rev-parse", "%s^{tree}" % commit).decode().strip()


def listing(commit):
    raw = git("ls-tree", "-r", "--name-only", commit, "--", SOURCE_PACKAGE)
    return [line for line in raw.decode().splitlines() if line]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def materialize(session, check_only):
    commit = session["commit"]
    target_root = OWNER_EVIDENCE / ("q2-session-" + session["id"])
    inventory = []

    entries = [(path, blob(commit, path)) for path in listing(commit)]
    entries.append((SOURCE_REPORT, blob(commit, SOURCE_REPORT)))

    for path, data in entries:
        if path == SOURCE_REPORT:
            rel = "report/Q-B5B-09-PLAIN-Q2-qualification.md"
        elif path.endswith("/.gitattributes"):
            # The source package's own .gitattributes is preserved byte-exactly under a
            # documented name, and an owner guard takes the governing role. Session B's
            # attributes ('* text=auto') contradict the CRLF blobs it inherited from
            # session A, and would rewrite evidence bytes on checkout or re-add.
            rel = ".gitattributes.session-" + session["id"]
        else:
            rel = path[len(SOURCE_PACKAGE) + 1:]
        inventory.append({
            "sourcePath": path,
            "restoredPath": rel,
            "bytes": len(data),
            "sha256": digest(data),
            "gitBlob": git("rev-parse", "%s:%s" % (commit, path)).decode().strip(),
        })

        destination = target_root / rel
        if check_only:
            if not destination.exists():
                raise RuntimeError("missing materialized file: %s" % destination)
            current = destination.read_bytes()
            if current != data:
                raise RuntimeError("byte mismatch: %s" % destination)
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(data)

    guard = target_root / ".gitattributes"
    guard_bytes = b"* -text\n"
    if check_only:
        if not guard.exists() or guard.read_bytes() != guard_bytes:
            raise RuntimeError("guard mismatch: %s" % guard)
    else:
        guard.write_bytes(guard_bytes)

    return {
        "id": session["id"],
        "label": session["label"],
        "commit": commit,
        "tree": tree_of(commit),
        "fileCount": len(inventory),
        "totalBytes": sum(item["bytes"] for item in inventory),
        "files": inventory,
    }


def main():
    check_only = "--check" in sys.argv
    summaries = [materialize(session, check_only) for session in SESSIONS]

    lines = []
    lines.append("# Q2 Session Package Provenance")
    lines.append("")
    lines.append("Materialized byte-exactly from pinned Git commits by")
    lines.append("`extract-q2-packages.py`. Source payload bytes come from `git cat-file blob`;")
    lines.append("no working-tree file was read and no history was rewritten.")
    lines.append("")
    for summary in summaries:
        lines.append("## %s (`q2-session-%s/`)" % (summary["label"], summary["id"]))
        lines.append("")
        lines.append("- Commit: `%s`" % summary["commit"])
        lines.append("- Tree: `%s`" % summary["tree"])
        lines.append("- Source package: `%s`" % SOURCE_PACKAGE)
        lines.append("- Source report: `%s`" % SOURCE_REPORT)
        lines.append("- Files restored: %d" % summary["fileCount"])
        lines.append("- Total bytes: %d" % summary["totalBytes"])
        lines.append("")
        lines.append("The source package's own `.gitattributes` is preserved byte-exactly as")
        lines.append("`.gitattributes.session-%s`. The governing `.gitattributes` in this" % summary["id"])
        lines.append("directory is an owner guard containing `* -text`, so the preserved bytes")
        lines.append("are byte-stable across checkouts and are never rewritten by a re-add.")
        lines.append("")
        lines.append("| Restored path | Git blob | Bytes | SHA-256 |")
        lines.append("|---|---|---|---|")
        for item in summary["files"]:
            lines.append("| `%s` | `%s` | %d | `%s` |" % (
                item["restoredPath"], item["gitBlob"], item["bytes"], item["sha256"]))
        lines.append("")

    provenance = "\n".join(lines).encode("utf-8")
    for summary in summaries:
        target = OWNER_EVIDENCE / ("q2-session-" + summary["id"]) / "PROVENANCE.md"
        if check_only:
            if not target.exists() or target.read_bytes() != provenance:
                raise RuntimeError("provenance mismatch: %s" % target)
        else:
            target.write_bytes(provenance)

    for summary in summaries:
        print("%s: %s commit=%s files=%d bytes=%d" % (
            "CHECK" if check_only else "RESTORED",
            summary["id"], summary["commit"][:7], summary["fileCount"], summary["totalBytes"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
