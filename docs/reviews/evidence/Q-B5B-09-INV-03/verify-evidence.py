"""Verify the committed INV-03 report and additions-only evidence package."""

from io import BytesIO
import hashlib
import json
import subprocess
import sys
import zipfile

PACKAGE = "docs/reviews/evidence/Q-B5B-09-INV-03"
REPORT = "docs/reviews/Q-B5B-09-INV-03-investigation.md"
MANIFEST = f"{PACKAGE}/manifest.json"
PARENT = "4beafcdd4ce48e0c0a4f8a6e05a52db328d7e017"
PARENT_TREE = "a3a8bc2d651ec81c95e59167a914de65b268745e"
CONVENTION = "SHA-256 over exact Git blob payload bytes"
NAMES = {
    ".gitattributes", "README.md", "manifest.json", "source-identity.json",
    "Inv03QualificationTests.cs.txt", "campaign-summary.json", "controls.json",
    "execution-records.zip", "native-linux.sh", "real-write-observation.patch",
    "refinement.patch", "verify-evidence.py",
}


def git(*args):
    return subprocess.check_output(["git", *args])


def verify(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    parents = git("show", "-s", "--format=%P", commit).decode().split()
    if parents != [PARENT] or git("rev-parse", f"{PARENT}^{{tree}}").decode().strip() != PARENT_TREE:
        raise ValueError("Expected exact INV-02 parent and tree")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    if (manifest["parent_commit"], manifest["parent_tree"], manifest["hash_convention"]) != (
        PARENT, PARENT_TREE, CONVENTION
    ):
        raise ValueError("Unexpected manifest parent or hash convention")

    package = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    if package != {f"{PACKAGE}/{name}" for name in NAMES}:
        raise ValueError("Unexpected package inventory")
    expected = package | {REPORT}
    entries = manifest["entries"]
    listed = {item["path"] for item in entries}
    if listed != expected - {MANIFEST} or len(entries) != len(listed):
        raise ValueError("Incomplete, unexpected or duplicate manifest entries")

    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r", "--name-status",
                      PARENT, commit).decode().splitlines())
    if changes != {f"A\t{path}" for path in expected}:
        raise ValueError("Commit must only add the new report and evidence package")

    for entry in entries:
        path = entry["path"]
        data = blob(path)
        if len(data) != entry["bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ValueError(f"SHA-256 or byte-count mismatch for {path}")

    source = json.loads(blob(f"{PACKAGE}/source-identity.json"))
    with zipfile.ZipFile(BytesIO(blob(f"{PACKAGE}/execution-records.zip"))) as records:
        names = records.namelist()
        if len(names) != source["inv03_execution_archive_members"] or len(names) != len(set(names)):
            raise ValueError("Unexpected execution archive member count")
        if any("/" in name or "\\" in name or not name.endswith((".log", ".trx")) for name in names):
            raise ValueError("Unexpected execution archive member")
        if records.testzip() is not None:
            raise ValueError("Execution archive CRC failure")
    print(f"PASS {len(entries)}/{len(entries)} blob hashes, exact INV-02 parent, "
          f"valid execution archive, {len(changes)} additions only")
    print(f"Commit {commit}")
    print(f"Tree   {git('rev-parse', commit + '^{tree}').decode().strip()}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [investigation-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (subprocess.CalledProcessError, OSError, KeyError, ValueError, TypeError, zipfile.BadZipFile) as error:
        sys.exit(f"FAIL {error}")
