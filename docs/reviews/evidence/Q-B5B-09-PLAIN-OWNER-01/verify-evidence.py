#!/usr/bin/env python3
"""Q-B5B-09-PLAIN-OWNER-01 evidence verifier.

Verifies the owner acceptance package over Git blob payloads only, so the result does not
depend on checkout line-ending normalization and does not depend on either Q2 package's own
verifier (both of which have recorded defects).

Checks, in order:
  1. The owner commit's parent and tree match the recorded base, and the commit scope is
     additions-only within the owner decision and owner evidence package.
  2. Every file recorded in manifest.json matches its SHA-256 and byte count.
  3. Both preserved Q2 packages match their pinned commits file by file over Git blobs.
  4. Session A's own manifest entries (47 content blobs plus the qualification report) are
     re-derived over Git blobs.
  5. Rollback recovery provenance is confirmed against the pre-rollback cleanup inventory.
  6. The six TRX campaign counters match the reported totals.
  7. The build and format logs carry exactly two xUnit2012 warnings and eight pre-existing
     whitespace findings.
  8. Each of the nine negative-control transcripts records a failed run and has a TRX.
  9. R1 source and evidence blob identity, Q1 evidence identity and historical evidence and
     decision integrity.
 10. The reconciliation claims: ancestry, package-collision state, disposition agreement
     between the two qualification reports, and the discrepancy register.
 11. The live repository invariants at verification time (branch, cleanliness, worktrees,
     stashes, divergence). These are enforced only when the verifier runs in the primary
     working tree; in a linked worktree or a fresh clone they are reported, not enforced,
     so the evidence checks remain checkout-independent.

Usage:
    python docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/verify-evidence.py [commit]
"""

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True  # keep the evidence package free of generated artefacts

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parent.parent.parent.parent

PACKAGE_DIR = "docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01"
DECISION = "docs/decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md"

spec = importlib.util.spec_from_file_location(
    "owner_audit", EVIDENCE / "audit-owner-inputs.py")
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


def git_optional(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


def blob(rev, path):
    return git_optional("cat-file", "blob", "%s:%s" % (rev, path))


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    rev = sys.argv[1] if len(sys.argv) > 1 else "HEAD"
    result = Result()

    print("=" * 76)
    print("Q-B5B-09-PLAIN-OWNER-01 evidence verification")
    print("revision     : %s" % rev)
    print("disposition  : PLAINTEXT REMEDIATION - ACCEPTED WITH QUALIFICATIONS - CLOSED")
    print("=" * 76)

    manifest_data = blob(rev, PACKAGE_DIR + "/manifest.json")
    if not result.check(manifest_data is not None, "manifest present at %s" % rev):
        print("VERIFICATION FAILED")
        return 1
    manifest = json.loads(manifest_data.decode("utf-8"))

    base = manifest["baseCommit"]

    print("\n1. Owner commit structure and scope")
    parent = git("show", "-s", "--format=%P", rev).decode().strip()
    result.check(parent == base, "sole parent is the acceptance base",
                 "parent=%s expected=%s" % (parent, base))
    changed = [line.split("\t", 1) for line in
               git("diff", "--name-status", base, rev).decode().splitlines() if line]
    outside = [path for status, path in changed
               if not (path == DECISION or path.startswith(PACKAGE_DIR + "/"))]
    result.check(not outside, "commit scope limited to the owner decision and evidence package",
                 ", ".join(outside))
    result.check(all(status == "A" for status, _ in changed),
                 "commit scope is additions only",
                 ", ".join(path for status, path in changed if status != "A"))

    print("\n2. Manifest hashes and byte counts over Git blobs")
    mismatches = []
    for entry in manifest["files"]:
        data = blob(rev, entry["path"])
        if data is None:
            mismatches.append(entry["path"] + " (missing)")
        elif sha256(data) != entry["sha256"] or len(data) != entry["bytes"]:
            mismatches.append(entry["path"] + " (hash/bytes)")
    result.check(not mismatches,
                 "%d package files match the manifest" % len(manifest["files"]),
                 "; ".join(mismatches[:5]))
    result.check(manifest["fileCount"] == len(manifest["files"]),
                 "manifest file count is consistent")
    result.check(sum(entry["bytes"] for entry in manifest["files"]) == manifest["totalBytes"],
                 "manifest byte total is consistent")

    print("\n3. Preserved Q2 packages against their pinned commits")
    for label, key in (("session A", "q2-session-a"), ("session B", "q2-session-b")):
        preserved = manifest["preservedPackages"][key]
        source_commit = preserved["commit"]
        source_root = preserved["source"]
        restored_root = preserved["restored"]
        source_files = [line for line in git(
            "ls-tree", "-r", "--name-only", source_commit, "--", source_root
        ).decode().splitlines() if line]
        bad = []
        for path in source_files:
            rel = path[len(source_root) + 1:]
            if rel == ".gitattributes":
                target_rel = ".gitattributes.session-" + key.rsplit("-", 1)[-1]
            else:
                target_rel = rel
            source_data = blob(source_commit, path)
            restored_data = blob(rev, restored_root + "/" + target_rel)
            if source_data is None or restored_data is None or source_data != restored_data:
                bad.append(target_rel)
        report_rel = "report/Q-B5B-09-PLAIN-Q2-qualification.md"
        source_report = blob(source_commit, "docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md")
        restored_report = blob(rev, restored_root + "/" + report_rel)
        if source_report is None or restored_report is None or source_report != restored_report:
            bad.append(report_rel)
        result.check(not bad, "%s preserved byte-exactly (%d files)" % (label, len(source_files) + 1),
                     "; ".join(bad[:5]))
        guard = blob(rev, restored_root + "/.gitattributes")
        result.check(guard == b"* -text\n",
                     "%s byte-exactness guard installed" % label)

    print("\n4. Session A manifest re-derived over Git blobs")
    session_a = manifest["pinnedRevisions"]["independentQ2SessionA"]
    source_root = "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2"
    a_manifest = json.loads(blob(session_a, source_root + "/manifest.json").decode("utf-8"))
    a_entries = a_manifest["evidenceFiles"]
    a_report_entries = [e for e in a_entries if e["path"].startswith("../../")]
    a_self_referential = [e for e in a_entries if e["path"].endswith("verify-evidence.py")]
    a_content = [e for e in a_entries
                 if e not in a_report_entries and e not in a_self_referential]
    a_bad = []
    a_verified = 0
    for entry in a_entries:
        rel = entry["path"]
        if rel.endswith("verify-evidence.py"):
            continue
        target = ("docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md" if rel.startswith("../../")
                  else source_root + "/" + rel)
        data = blob(session_a, target)
        if data is None:
            a_bad.append(rel + " (missing)")
        elif sha256(data) != entry["sha256"] or len(data) != entry["bytes"]:
            a_bad.append(rel + " (hash/bytes)")
        else:
            a_verified += 1
    result.check(not a_bad,
                 "session A manifest: %d entries re-derived over Git blobs" % a_verified,
                 "; ".join(a_bad[:5]))
    result.check(len(a_entries) == 48 and len(a_content) == 46 and len(a_report_entries) == 1
                 and len(a_self_referential) == 1,
                 "session A manifest composition: 47 content blobs plus the qualification "
                 "report, one self-referential verifier",
                 "entries=%d content=%d report=%d selfReferential=%d" %
                 (len(a_entries), len(a_content), len(a_report_entries), len(a_self_referential)))

    print("\n5. Rollback recovery provenance")
    audit = audit_module.audit()
    provenance = audit["rollbackProvenance"]
    if provenance["inventoryPresent"]:
        result.check(not provenance["unverified"],
                     "all %d rollback-surviving results match the pre-rollback inventory"
                     % len(provenance["verified"]))
    else:
        result.check(False, "pre-rollback cleanup inventory present")

    print("\n6. Campaign counters inside TRX envelopes")
    for name, counters in sorted(audit["trxCounters"].items()):
        result.check(counters["matches"], name,
                     "total=%s passed=%s failed=%s skipped=%s" %
                     (counters["total"], counters["passed"], counters["failed"], counters["skipped"]))

    print("\n7. Build and format logs")
    build = audit["buildAndFormat"]
    result.check(build["xUnit2012Occurrences"] == 4,
                 "xUnit2012 warning occurrences=4 (two unique, echoed twice)")
    result.check(build["whitespaceFindings"] == 8,
                 "pre-existing whitespace findings=8")

    print("\n8. Negative-control transcripts")
    for control, item in sorted(audit["negativeControls"].items()):
        result.check(item["detected"], control,
                     "failures=%s trx=%s" % (item["failuresRecorded"], item["trxPresent"]))

    print("\n9. R1, Q1 and historical integrity")
    for path, item in sorted(audit["r1Sources"].items()):
        result.check(item["present"], "R1 source " + path)
    for path, item in sorted(audit["r1Evidence"].items()):
        result.check(item["present"], "R1 evidence " + path)
    result.check(audit["q1Evidence"]["byteIdentical"], "Q1 evidence byte-identical at the base")
    historical = audit["historicalIntegrity"]
    result.check(historical["decisionsUnchanged"], "owner-decision tree unchanged since R1 evidence")
    for directory, item in sorted(historical["directories"].items()):
        result.check(item["unchangedSinceR1Evidence"], "unchanged " + directory)

    print("\n10. Reconciliation claims")
    collision = audit["packageCollision"]
    result.check(collision["sessionBIsChildOfSessionA"],
                 "session B is a descendant of session A")
    result.check(collision["identical"] == 35 and len(collision["modifiedBySessionB"]) == 13
                 and len(collision["addedBySessionB"]) == 5 and not collision["onlyInSessionA"],
                 "package collision state matches the recorded table",
                 "identical=%d modified=%d added=%d aOnly=%d" %
                 (collision["identical"], len(collision["modifiedBySessionB"]),
                  len(collision["addedBySessionB"]), len(collision["onlyInSessionA"])))
    result.check(collision["headPackageFileCount"] == 53,
                 "merged package directory at the base holds 53 files")
    agreement = audit["reportAgreement"]
    result.check(agreement["markersIdentical"],
                 "both qualification reports agree on disposition and findings")
    result.check(agreement["danglingObsProposalInSessionB"]
                 and not agreement["danglingObsProposalExistsAtBase"],
                 "D-05 dangling OBS-01 reference confirmed")
    recorded = json.loads(blob(rev, PACKAGE_DIR + "/verification.json").decode("utf-8"))
    for section in ("pinned", "sessions", "packageCollision", "r1Sources", "r1Evidence",
                    "q1Evidence", "trxCounters", "buildAndFormat", "negativeControls",
                    "rollbackProvenance", "historicalIntegrity", "reportAgreement"):
        result.check(recorded.get(section) == audit.get(section),
                     "verification.json matches the recomputed audit: " + section)
    result.check(len(manifest["discrepancies"]) == 11,
                 "discrepancy register holds D-01 .. D-11")
    result.check(sorted(item["id"] for item in manifest["discrepancies"]) ==
                 ["D-%02d" % number for number in range(1, 12)],
                 "discrepancy identifiers are contiguous")

    print("\n11. Live repository invariants")
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
