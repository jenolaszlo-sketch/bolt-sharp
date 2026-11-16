"""Verify the Q-B5B-09-INV-02 committed evidence."""

from io import BytesIO
import hashlib
import json
import subprocess
import sys
import zipfile

PACKAGE = "docs/reviews/evidence/Q-B5B-09-INV-02"
REPORT = "docs/reviews/Q-B5B-09-INV-02-investigation.md"
MANIFEST = f"{PACKAGE}/manifest.json"
PARENT = "23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa"
PARENT_TREE = "bd841c86886587d38276fba2798ef13404ce002d"
CONVENTION = "SHA-256 over exact Git blob payload bytes"
NAMES = {
    ".gitattributes", "README.md", "manifest.json", "source-identity.json",
    "execution-records.zip", "matrix-observations.txt", "prototype.patch",
    "wsl-listener-adapter.patch", "Inv02TerminalTests.cs.txt", "Inv02DriverTests.cs.txt",
    "controls-results.json", "cancel-timing-results.json", "verify-evidence.py",
}


def git(*args):
    return subprocess.check_output(["git", *args])


def verify(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    parents = git("show", "-s", "--format=%P", commit).decode().split()
    if parents != [PARENT] or git("rev-parse", f"{PARENT}^{{tree}}").decode().strip() != PARENT_TREE:
        raise ValueError("Expected the exact INV-01 parent and tree")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    if (manifest["parent_commit"], manifest["parent_tree"], manifest["hash_convention"]) != (
        PARENT, PARENT_TREE, CONVENTION
    ):
        raise ValueError("Unexpected manifest identity or hash convention")

    expected_package = {f"{PACKAGE}/{name}" for name in NAMES}
    inventory = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    if inventory != expected_package:
        raise ValueError("Unexpected package inventory")
    expected = inventory | {REPORT}
    entries = manifest["entries"]
    listed = {entry["path"] for entry in entries}
    if listed != expected - {MANIFEST} or len(entries) != len(listed):
        raise ValueError("Incomplete, unexpected or duplicate manifest entries")

    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r", "--name-status",
                      PARENT, commit).decode().splitlines())
    if changes != {f"A\t{path}" for path in expected}:
        raise ValueError("Commit must only add the investigation report and evidence package")
    for entry in entries:
        path = entry["path"]
        data = blob(path)
        if len(data) != entry["bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ValueError(f"SHA-256 or byte-count mismatch for {path}")

    source = json.loads(blob(f"{PACKAGE}/source-identity.json"))
    if (source["commit"], source["tree"]) != (PARENT, PARENT_TREE):
        raise ValueError("Unexpected source commit or tree")
    with zipfile.ZipFile(BytesIO(blob(f"{PACKAGE}/execution-records.zip"))) as records:
        names = records.namelist()
        if len(names) != source["execution_archive_members"] or len(names) != len(set(names)):
            raise ValueError("Unexpected execution record count")
        if any("/" in name or "\\" in name or not name.endswith((".log", ".trx")) for name in names):
            raise ValueError("Unexpected execution archive member")
        if records.testzip() is not None:
            raise ValueError("Execution archive CRC failure")

    print(f"PASS {len(entries)}/{len(entries)} blob hashes, valid archive, exact INV-01 parent, "
          f"{len(changes)} additions only")
    print(f"Commit {commit}")
    print(f"Tree   {git('rev-parse', commit + '^{tree}').decode().strip()}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [investigation-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (subprocess.CalledProcessError, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        sys.exit(f"FAIL {error}")
