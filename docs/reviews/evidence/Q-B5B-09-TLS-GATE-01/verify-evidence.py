"""Verify committed TLS-GATE-01 evidence and the attempt budget."""

from collections import Counter
from io import BytesIO
import hashlib
import json
import subprocess
import sys
import zipfile

PACKAGE = "docs/reviews/evidence/Q-B5B-09-TLS-GATE-01"
REPORT = "docs/reviews/Q-B5B-09-TLS-GATE-01-report.md"
MANIFEST = f"{PACKAGE}/manifest.json"
PARENT = "44093f65df2b49de380e4debd64b9606f9e11733"
PARENT_TREE = "4137367cf955671e1a31a8890c9ca2e54cc30215"
HASH_CONVENTION = "SHA-256 over exact Git blob payload bytes"
NAMES = {
    ".gitattributes", "README.md", "manifest.json", "configuration.json",
    "execution-records.zip", "link-observation.patch", "TlsGate01Tests.cs.txt",
    "source-identity.json", "windows.jsonl", "wsl2.jsonl", "matrix.json",
    "verify-evidence.py",
}


def git(*args):
    return subprocess.check_output(["git", *args])


def check(condition, message):
    if not condition:
        raise ValueError(message)


def verify(revision):
    commit = git("rev-parse", "--verify", "--end-of-options", f"{revision}^{{commit}}").decode().strip()
    check(git("show", "-s", "--format=%P", commit).decode().split() == [PARENT],
          "Unexpected gate parent")
    check(git("rev-parse", f"{PARENT}^{{tree}}").decode().strip() == PARENT_TREE,
          "Unexpected parent tree")

    def blob(path):
        return git("cat-file", "blob", f"{commit}:{path}")

    manifest = json.loads(blob(MANIFEST))
    check((manifest["parent_commit"], manifest["parent_tree"], manifest["hash_convention"])
          == (PARENT, PARENT_TREE, HASH_CONVENTION), "Unexpected manifest identity or convention")
    inventory = {f"{PACKAGE}/{name}" for name in NAMES}
    tree_paths = set(git("ls-tree", "-r", "--name-only", commit, "--", PACKAGE).decode().splitlines())
    check(tree_paths == inventory, "Unexpected evidence inventory")
    expected = inventory | {REPORT}
    entries = manifest["entries"]
    paths = {entry["path"] for entry in entries}
    check(paths == expected - {MANIFEST} and len(paths) == len(entries),
          "Missing, unexpected or duplicate hash entries")
    changes = set(git("diff-tree", "--no-commit-id", "--no-renames", "-r", "--name-status",
                      PARENT, commit).decode().splitlines())
    check(changes == {f"A\t{path}" for path in expected}, "Unexpected commit path or status")
    for entry in entries:
        path = entry["path"]
        data = blob(path)
        check(len(data) == entry["bytes"] and hashlib.sha256(data).hexdigest() == entry["sha256"],
              f"Blob digest or byte count differs at {path}")

    source = json.loads(blob(f"{PACKAGE}/source-identity.json"))
    config = blob(f"{PACKAGE}/configuration.json")
    check(hashlib.sha256(config).hexdigest() == source["predeclared_configuration_sha256"],
          "Predeclared configuration identity differs")
    cfg = json.loads(config)
    check(cfg["maximum_attempts_per_cell_per_platform"] == 3, "Unexpected bound")
    matrix = json.loads(blob(f"{PACKAGE}/matrix.json"))
    for platform in ("windows", "wsl2"):
        rows = [json.loads(line) for line in blob(f"{PACKAGE}/{platform}.jsonl").splitlines()]
        start = Counter()
        completed = Counter()
        observations = {key: [] for key in cfg["cells"]}
        for row in rows[:-1]:
            cell = row["cell"]["Name"]
            check(cell in observations, "Unknown cell")
            number = row["profile"]["Ordinal"]
            if row["kind"] == "started":
                start[cell] += 1
                check(number == start[cell] and number <= 3, "Attempt order/budget failure")
            elif row["kind"] == "completed":
                completed[cell] += 1
                check(number == completed[cell] == start[cell], "Unmatched attempt completion")
                observations[cell].append(row["result"]["Outcome"])
            else:
                raise ValueError("Unknown journal event")
        check(start == completed and rows[-1]["kind"] == "campaign-end", "Incomplete journal")
        check(sum(start.values()) == matrix[platform]["attempt_count"], "Matrix count differs")
        for cell, outcomes in observations.items():
            check(outcomes == [item["outcome"] for item in matrix[platform]["cells"][cell]],
                  "Matrix and journal outcomes differ")

    with zipfile.ZipFile(BytesIO(blob(f"{PACKAGE}/execution-records.zip"))) as archive:
        names = archive.namelist()
        check(len(names) == len(set(names)) == source["execution_record_members"],
              "Archive size or duplicate names")
        check(all("/" not in name and "\\" not in name and name.endswith((".log", ".trx")) for name in names),
              "Unexpected archive member")
        check(archive.testzip() is None, "Archive CRC failed")
    print(f"PASS {len(entries)}/{len(entries)} blob hashes, exact INV-03 parent, valid archive, "
          f"bounded journals, {len(changes)} additions only")
    print(f"Commit {commit}")
    print(f"Tree   {git('rev-parse', commit + '^{tree}').decode().strip()}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [investigation-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (subprocess.CalledProcessError, OSError, KeyError, TypeError, ValueError, zipfile.BadZipFile) as error:
        sys.exit(f"FAIL {error}")
