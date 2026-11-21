"""Verify the committed Q-B5B-09-INV-01 evidence and additions-only scope."""

import hashlib
import json
import subprocess
import sys

PACKAGE = "docs/reviews/evidence/Q-B5B-09-INV-01"
REPORT = "docs/reviews/Q-B5B-09-INV-01-investigation.md"
MANIFEST = f"{PACKAGE}/manifest.json"
PARENT = "29d521cea9e65fe2d240421156454444e0d055ea"
PARENT_TREE = "5063e16258354ecebd3587ac2d7e38bdd75d7e5a"
CONVENTION = "SHA-256 over exact Git blob payload bytes"
EVIDENCE_NAMES = {
    ".gitattributes", "README.md", "driver-baseline.log", "driver-baseline.trx",
    "manifest.json", "matrix-summary.txt", "summarize.py", "tcp-close-harness.py",
    "verify-evidence.py", "windows.json", "wsl2.json",
}


def git(*args):
    return subprocess.check_output(["git", *args])


def verify(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    parents = git("show", "-s", "--format=%P", commit).decode().split()
    if parents != [PARENT] or git("rev-parse", f"{PARENT}^{{tree}}").decode().strip() != PARENT_TREE:
        raise ValueError("Expected the exact TS1 owner-closure parent and tree")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    if (manifest["parent_commit"], manifest["parent_tree"], manifest["hash_convention"]) != (
        PARENT, PARENT_TREE, CONVENTION
    ):
        raise ValueError("Unexpected manifest parent identity or hash convention")

    required = {f"{PACKAGE}/{name}" for name in EVIDENCE_NAMES}
    inventory = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    if inventory != required:
        raise ValueError("Unexpected evidence inventory")
    expected = inventory | {REPORT}
    entries = manifest["entries"]
    listed = {item["path"] for item in entries}
    if listed != expected - {MANIFEST} or len(entries) != len(listed):
        raise ValueError("Incomplete, unexpected, or duplicate manifest entries")

    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r", "--name-status",
                      PARENT, commit).decode().splitlines())
    if changes != {f"A\t{path}" for path in expected}:
        raise ValueError("Commit must only add the investigation report and evidence files")
    for item in entries:
        path = item["path"]
        data = blob(path)
        if len(data) != item["bytes"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
            raise ValueError(f"SHA-256 or byte-count mismatch for {path}")

    print(f"PASS {len(entries)}/{len(entries)} blob hashes, complete inventory, "
          f"exact owner parent, {len(changes)} additions only")
    print(f"Commit {commit}")
    print(f"Tree   {git('rev-parse', commit + '^{tree}').decode().strip()}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [investigation-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (subprocess.CalledProcessError, OSError, ValueError, KeyError, TypeError) as error:
        sys.exit(f"FAIL {error}")
