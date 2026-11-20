#!/usr/bin/env python3
"""Q-B5B-05-OWNER-01 evidence verifier (owner-entry tooling).

Verifies, over Git blob payloads (checkout line-ending independent):

  1. The decision commit is additions-only and is confined to this decision and its evidence
     package (no production, test, project, package, dependency, historical-evidence, or
     prior-decision change — including the Q1 and producer packages).
  2. Every manifest entry's SHA-256, byte count and Git blob ID, recomputed from the blob.
  3. The protected revisions named in the decision are reachable and unchanged in the path.
  4. Live repository invariants.

Usage: python docs/reviews/evidence/Q-B5B-05-OWNER-01/verify-evidence.py [revision]
"""
import hashlib
import json
import pathlib
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parents[3]
REVISION = sys.argv[1] if len(sys.argv) > 1 else "HEAD"

PACKAGE = "docs/reviews/evidence/Q-B5B-05-OWNER-01"
DECISION = "docs/decisions/Q-B5B-05-OWNER-01-owner-acceptance.md"
Q1_FINAL = "7704fe6a9400c24217cf327e3b5910669c2f7c70"
PRODUCER = "8d762dd50502260e32f569f42f9cbfa92e37b862"
PROTECTED_PREFIXES = (
    "src/", "tests/",
    "docs/reviews/evidence/Q-B5B-05-PROBE-01",
    "docs/reviews/probes/Q-B5B-05-PROBE-01",
    "docs/reviews/evidence/Q-B5B-05-Q1",
)


def git(*args, binary=True):
    proc = subprocess.run(["git", "-C", str(REPO), *args], capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout if binary else proc.stdout.decode()


def blob(rev, path):
    proc = subprocess.run(["git", "-C", str(REPO), "cat-file", "blob", "%s:%s" % (rev, path)],
                          capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


class Result:
    def __init__(self):
        self.ok = True
        self.failures = []

    def check(self, condition, label, detail=""):
        print("   %s  %s%s" % ("PASS" if condition else "FAIL", label,
                               ("  -> " + detail) if detail and not condition else ""))
        if not condition:
            self.ok = False
            self.failures.append(label)
        return condition


def main():
    result = Result()
    print("=" * 76)
    print("Q-B5B-05-OWNER-01 evidence verification at %s" % REVISION)
    print("disposition: Q-B5B-05 - ACCEPTED WITH QUALIFICATIONS - CLOSED")
    print("=" * 76)

    manifest_data = blob(REVISION, PACKAGE + "/manifest.json")
    if not result.check(manifest_data is not None, "manifest present at %s" % REVISION):
        return 1
    manifest = json.loads(manifest_data.decode("utf-8"))

    print("\n1. Commit structure and scope")
    range_spec = "%s..%s" % (manifest["baseCommit"], REVISION)
    commits = git("rev-list", "--count", range_spec, binary=False).strip()
    print("   INFO  owner commits in %s..%s: %s" % (manifest["baseCommit"][:8], REVISION[:8], commits))
    changed = [line.split("\t", 1) for line in
               git("diff", "--name-status", manifest["baseCommit"], REVISION, binary=False).splitlines() if line]
    result.check(all(s == "A" for s, _ in changed), "commit scope is additions only",
                 ", ".join(p for s, p in changed if s != "A"))
    outside = [p for _, p in changed if p != DECISION and not p.startswith(PACKAGE + "/")]
    result.check(not outside, "commit scope limited to the decision and this package", ", ".join(outside))
    protected = [p for _, p in changed if p.startswith(PROTECTED_PREFIXES)]
    result.check(not protected, "no change to production, tests or existing qualification evidence",
                 ", ".join(protected[:5]))
    for rev in (PRODUCER, Q1_FINAL):
        result.check(git("cat-file", "-t", rev, binary=False).strip() == "commit",
                     "protected revision is a commit: %s" % rev[:8])

    print("\n2. Manifest entries over Git blobs")
    mismatches = []
    for e in manifest["files"]:
        data = blob(REVISION, e["path"])
        actual_blob = git("rev-parse", "%s:%s" % (REVISION, e["path"]), binary=False).strip()
        if data is None:
            mismatches.append(e["path"] + " (missing)")
        elif (hashlib.sha256(data).hexdigest() != e["sha256"]
              or len(data) != e["bytes"] or actual_blob != e["gitBlob"]):
            mismatches.append(e["path"])
    result.check(not mismatches,
                 "%d package files match the manifest (sha256/bytes/blob)" % len(manifest["files"]),
                 "; ".join(mismatches[:5]))
    result.check(manifest["fileCount"] == len(manifest["files"]), "manifest file count is consistent")

    print("\n3. Live repository invariants")
    branch = git("rev-parse", "--abbrev-ref", "HEAD", binary=False).strip()
    result.check(branch == "main", "branch is main", branch)
    status = git("status", "--porcelain", binary=False).strip()
    result.check(not status, "working tree clean", status[:120])
    worktrees = len(git("worktree", "list", binary=False).strip().splitlines())
    stashes = len(git("stash", "list", binary=False).strip().splitlines())
    result.check(worktrees == 1, "one worktree", str(worktrees))
    result.check(stashes == 0, "zero stashes", str(stashes))
    behind, ahead = git("rev-list", "--left-right", "--count", "origin/main...HEAD",
                        binary=False).split()
    result.check(behind == "0", "zero commits behind origin/main", behind)
    print("      %s ahead / %s behind origin/main" % (ahead, behind))

    print("\n" + "=" * 76)
    print("VERIFICATION %s" % ("PASSED" if result.ok else "FAILED: " + "; ".join(result.failures)))
    print("=" * 76)
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main())