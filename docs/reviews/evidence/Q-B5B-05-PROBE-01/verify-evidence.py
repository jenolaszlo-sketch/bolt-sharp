#!/usr/bin/env python3
"""Q-B5B-05-PROBE-01 evidence verifier.

Verifies the producer evidence package and the probe source over Git blob payloads only, so the
result does not depend on checkout line-ending normalization.

Checks, in order:
  1. The commit's parent and tree, and that the scope is additions-only within the producer
     report, the probe project and the evidence package (no production or existing-test change).
  2. Every file recorded in manifest.json matches its SHA-256, byte count and Git blob ID.
  3. The probe project, the producer report and the evidence package are all present, and no
     build artifact is tracked.
  4. The six TRX campaign envelopes match the reported totals.
  5. Every per-platform correlated-trace artifact is present and carries its required markers.
  6. The official-driver observations carry their required markers.
  7. The credential-redaction and LOGON-redaction invariant holds across all evidence.
  8. q5b05-evidence.json matches the recomputed audit.

Usage:
    python docs/reviews/evidence/Q-B5B-05-PROBE-01/verify-evidence.py [revision]
"""

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parents[3]
REVISION = sys.argv[1] if len(sys.argv) > 1 else "HEAD"

PACKAGE_DIR = "docs/reviews/evidence/Q-B5B-05-PROBE-01"
PROBE_DIR = "docs/reviews/probes/Q-B5B-05-PROBE-01"
REPORT = "docs/reviews/Q-B5B-05-PROBE-01-producer-report.md"

spec = importlib.util.spec_from_file_location("probe_audit", EVIDENCE / "audit-probe-inputs.py")
audit_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit_module)


class Result:
    def __init__(self):
        self.ok = True
        self.failures = []

    def check(self, condition, label, detail=""):
        status = "PASS" if condition else "FAIL"
        print("   %s  %s%s" % (status, label, ("  -> " + detail) if detail and not condition else ""))
        if not condition:
            self.ok = False
            self.failures.append(label)
        return condition


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout


def blob(rev, path):
    proc = subprocess.run(["git", "cat-file", "blob", "%s:%s" % (rev, path)],
                          cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


def main():
    result = Result()

    print("=" * 76)
    print("Q-B5B-05-PROBE-01 evidence verification")
    print("revision    : %s" % REVISION)
    print("disposition: PRODUCTION LIMITATION IDENTIFIED - OWNER DECISION REQUIRED")
    print("=" * 76)

    manifest_data = blob(REVISION, PACKAGE_DIR + "/manifest.json")
    if not result.check(manifest_data is not None, "manifest present at %s" % REVISION):
        print("VERIFICATION FAILED")
        return 1
    manifest = json.loads(manifest_data.decode("utf-8"))
    base = manifest["baseCommit"]

    print("\n1. Commit structure and scope")
    parent = git("show", "-s", "--format=%P", REVISION).decode().strip()
    result.check(parent == base, "sole parent is the recorded base",
                 "parent=%s expected=%s" % (parent, base))
    changed = [line.split("\t", 1) for line in
               git("diff", "--name-status", base, REVISION).decode().splitlines() if line]
    outside = [path for status, path in changed
               if not (path == REPORT or path.startswith(PROBE_DIR + "/")
                       or path.startswith(PACKAGE_DIR + "/"))]
    result.check(not outside, "commit scope limited to the producer report, probe project and "
                              "evidence package", ", ".join(outside))
    result.check(all(status == "A" for status, _ in changed),
                 "commit scope is additions only",
                 ", ".join(path for status, path in changed if status != "A"))
    production = [path for status, path in changed if path.startswith(("src/", "tests/"))]
    result.check(not production, "no production or existing-test file changed",
                 ", ".join(production))

    print("\n2. Manifest hashes, byte counts and Git blob IDs")
    mismatches = []
    for entry in manifest["files"]:
        data = blob(REVISION, entry["path"])
        actual_blob = subprocess.run(
            ["git", "rev-parse", "%s:%s" % (REVISION, entry["path"])],
            cwd=REPO, capture_output=True, text=True).stdout.strip()
        if data is None:
            mismatches.append(entry["path"] + " (missing)")
        elif (hashlib.sha256(data).hexdigest() != entry["sha256"]
              or len(data) != entry["bytes"]
              or actual_blob != entry["gitBlob"]):
            mismatches.append(entry["path"] + " (hash/bytes/blob)")
    result.check(not mismatches,
                 "%d package files match the manifest" % len(manifest["files"]),
                 "; ".join(mismatches[:5]))
    result.check(manifest["fileCount"] == len(manifest["files"]), "manifest file count is consistent")
    result.check(sum(entry["bytes"] for entry in manifest["files"]) == manifest["totalBytes"],
                 "manifest byte total is consistent")

    print("\n3. Commission inventory")
    for path, label in ((REPORT, "producer report"), (PROBE_DIR + "/BoltSharp.Q5B05.Probe.csproj",
                                                      "probe project")):
        result.check(blob(REVISION, path) is not None, "%s present" % label)
    probe_files = git("ls-files", PROBE_DIR).decode().split()
    artifacts = [p for p in probe_files if "/bin/" in p or "/obj/" in p]
    result.check(not artifacts, "no build artifact tracked in the probe project",
                 ", ".join(artifacts[:5]))
    result.check(len(probe_files) == 9, "the probe project tracks 9 source files",
                 "observed %d" % len(probe_files))

    print("\n4. Campaign envelopes")
    audit = audit_module.audit()
    for name, counters in sorted(audit["trxCounters"].items()):
        result.check(counters["matches"], name,
                     "total=%s passed=%s failed=%s skipped=%s" %
                     (counters["total"], counters["passed"], counters["failed"], counters["skipped"]))

    print("\n5. Per-platform correlated traces")
    missing = [rel for rel, item in sorted(audit["platformArtifacts"].items()) if not item["present"]]
    result.check(not missing, "all 26 per-platform trace artifacts present",
                 ", ".join(missing[:5]))
    marker_failures = [rel for rel, item in sorted(audit["traceMarkers"].items())
                       if not item["allPresent"]]
    result.check(not marker_failures, "every trace carries its required markers",
                 ", ".join(marker_failures[:5]))

    print("\n6. Official-driver observations")
    driver_failures = [rel for rel, item in sorted(audit["driverMarkers"].items())
                       if not item["allPresent"]]
    result.check(not driver_failures, "every driver observation carries its required markers",
                 ", ".join(driver_failures[:5]))

    print("\n7. Credential and LOGON redaction")
    result.check(audit["redaction"]["clean"],
                 "no credential or unredacted LOGON payload in %d checked files"
                 % audit["redaction"]["filesChecked"],
                 ", ".join(audit["redaction"]["credentialLeaks"][:5]))

    print("\n8. Recorded audit")
    recorded_path = PACKAGE_DIR + "/q5b05-evidence.json"
    recorded_data = blob(REVISION, recorded_path)
    result.check(recorded_data is not None, "q5b05-evidence.json present")
    if recorded_data is not None:
        recorded = json.loads(recorded_data.decode("utf-8"))
        for section in ("trxCounters", "platformArtifacts", "traceMarkers", "driverMarkers",
                        "redaction", "probeSource"):
            result.check(recorded.get(section) == audit.get(section),
                         "recorded audit matches the recomputed audit: " + section)

    print("\n9. Live repository invariants")
    git_dir = git("rev-parse", "--git-dir").decode().strip()
    common_dir = git("rev-parse", "--git-common-dir").decode().strip()
    primary = os.path.normpath(git_dir) == os.path.normpath(common_dir)
    branch = git("rev-parse", "--abbrev-ref", "HEAD").decode().strip()
    worktrees = len(git("worktree", "list").decode().strip().splitlines())
    stashes = len(git("stash", "list").decode().strip().splitlines())
    behind, ahead = git("rev-list", "--left-right", "--count",
                        "origin/main...HEAD").decode().split()
    if primary:
        result.check(branch == "main", "branch is main", branch)
        status = git("status", "--porcelain").decode().strip()
        result.check(not status, "working tree clean", status)
        result.check(worktrees == 1, "one worktree", str(worktrees))
    else:
        print("   INFO  not the primary working tree; repository invariants are reported, "
              "not enforced")
        print("         branch=%s worktrees=%d" % (branch, worktrees))
    result.check(stashes == 0, "zero stashes", str(stashes))
    result.check(behind == "0", "zero commits behind origin/main", behind)
    print("      %s ahead / %s behind origin/main" % (ahead, behind))

    print("\n" + "=" * 76)
    print("VERIFICATION %s" % ("PASSED" if result.ok else "FAILED"))
    if result.failures:
        print("failed checks: %s" % ", ".join(result.failures))
    print("=" * 76)
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main())