"""Verify the committed B5-B-TS1-OWNER-01 evidence and scope."""

import hashlib
import json
import subprocess
import sys

PACKAGE = "docs/reviews/evidence/B5-B-TS1-OWNER-01"
DECISION = "docs/decisions/B5-B-TS1-OWNER-01-owner-closure.md"
MANIFEST = f"{PACKAGE}/manifest.json"
Q1_COMMIT = "b196db8f0bc612b7f1ef3f78412480239c3f04db"
Q1_TREE = "d7815b8ad338610e8343aefa6e948359b52b8fc2"


def git(*args):
    return subprocess.check_output(["git", *args])


def verify(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    parent = git("show", "-s", "--format=%P", commit).decode().strip()
    if parent != Q1_COMMIT or git("rev-parse", f"{parent}^{{tree}}").decode().strip() != Q1_TREE:
        raise ValueError("Expected the exact Q1 commit and tree as the sole parent")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    if manifest["hash_convention"] != "SHA-256 over exact Git blob payload bytes":
        raise ValueError("Unexpected manifest hashing convention")
    if manifest["parent_commit"] != parent or manifest["parent_tree"] != Q1_TREE:
        raise ValueError("Manifest does not pin the verified Q1 parent and tree")

    package = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    required = {f"{PACKAGE}/{name}" for name in (
        ".gitattributes", "README.md", "manifest.json", "verification.json", "verify-evidence.py"
    )}
    if package != required:
        raise ValueError("Unexpected owner evidence package contents")
    expected = package | {DECISION}
    entries = manifest["entries"]
    listed = {entry["path"] for entry in entries}
    if listed != expected - {MANIFEST} or len(entries) != len(listed):
        raise ValueError("Incomplete, unexpected, or duplicate manifest entries")

    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r",
                      "--name-status", parent, commit).decode().splitlines())
    if changes != {f"A\t{path}" for path in expected}:
        raise ValueError("Commit must only add the owner decision and evidence files")

    for entry in entries:
        path = entry["path"]
        data = blob(path)
        if len(data) != entry["bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ValueError(f"SHA-256 or byte-count mismatch for {path}")

    print(f"PASS {len(entries)}/{len(entries)} blob hashes, complete inventory, exact Q1 parent, "
          f"and {len(changes)} additions only")
    print(f"Commit {commit}")
    print(f"Tree   {git('rev-parse', commit + '^{tree}').decode().strip()}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [owner-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (subprocess.CalledProcessError, OSError, ValueError, KeyError, TypeError) as error:
        sys.exit(f"FAIL {error}")
