"""Verify the committed Q-B5B-09-OWNER-01 decision, scope and evidence."""

import hashlib
import json
import subprocess
import sys

PACKAGE = "docs/reviews/evidence/Q-B5B-09-OWNER-01"
MANIFEST = f"{PACKAGE}/manifest.json"
PARENT = "608eb3f0e07b059eb3bddd86de9235d422e1540c"
PARENT_TREE = "86ea9450d0fd5935af5a3ab851553934e39bfa20"
HASH_CONVENTION = "SHA-256 over exact Git blob payload bytes"
DECISIONS = {
    "docs/decisions/Q-B5B-09-OWNER-01-production-remediation-scope-decision.md",
    "docs/decisions/Q-B5B-09-PLAIN-IMPL-01-implementation-specification.md",
}
NAMES = {
    ".gitattributes", "README.md", "audit-reviewed-inputs.py", "manifest.json",
    "verification.json", "verify-evidence.py",
}


def git(*args):
    return subprocess.check_output(["git", *args])


def check(condition, message):
    if not condition:
        raise ValueError(message)


def verify(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    check(git("show", "-s", "--format=%P", commit).decode().split() == [PARENT], "Unexpected owner parent")
    check(git("rev-parse", f"{PARENT}^{{tree}}").decode().strip() == PARENT_TREE, "Unexpected parent tree")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    check((manifest["parent_commit"], manifest["parent_tree"], manifest["hash_convention"])
          == (PARENT, PARENT_TREE, HASH_CONVENTION), "Unexpected hash or parent convention")

    inventory = {f"{PACKAGE}/{name}" for name in NAMES}
    tracked = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    check(tracked == inventory, "Unexpected package inventory")
    expected = inventory | DECISIONS
    entries = manifest["entries"]
    paths = {item["path"] for item in entries}
    check(paths == expected - {MANIFEST} and len(paths) == len(entries), "Invalid evidence entries")

    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r", "--name-status",
                      PARENT, commit).decode().splitlines())
    check(changes == {f"A\t{p}" for p in expected}, "Commit exceeds the additions-only owner scope")
    for entry in entries:
        path = entry["path"]
        data = blob(path)
        check(len(data) == entry["bytes"] and hashlib.sha256(data).hexdigest() == entry["sha256"],
              f"Blob digest or byte count mismatch: {path}")

    record = json.loads(blob(f"{PACKAGE}/verification.json"))
    audit = subprocess.run([sys.executable, "-"], input=blob(f"{PACKAGE}/audit-reviewed-inputs.py"),
                           capture_output=True, check=True)
    check(json.loads(audit.stdout) == record, "Reviewed input audit differs from its record")
    print(f"PASS {len(entries)}/{len(entries)} owner blob hashes, prior evidence replay, "
          f"exact TLS-GATE-01 parent, {len(changes)} additions only")
    print(f"Commit {commit}")
    print(f"Tree   {git('rev-parse', commit + '^{tree}').decode().strip()}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [owner-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except subprocess.CalledProcessError as error:
        sys.exit(f"FAIL {error.stderr.decode(errors='replace') if error.stderr else error}")
    except (OSError, KeyError, TypeError, ValueError) as error:
        sys.exit(f"FAIL {error}")
