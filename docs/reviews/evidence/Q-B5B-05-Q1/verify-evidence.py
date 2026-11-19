#!/usr/bin/env python3
"""Q-B5B-05-Q1 evidence verifier (reviewer-owned, independent of the producer tooling).

Verifies, over Git blob payloads only (checkout line-ending independent):

  1. The commit's parent and tree state, and that the commission commit is additions-only
     within the qualification report and this evidence package (no production, test, project,
     package, or historical-evidence change).
  2. Every manifest entry's SHA-256, byte count and Git blob ID, recomputed from the blob.
  3. The six reviewer TRX campaign envelopes against their expected counters.
  4. Both platform results summaries: 15 scenarios, 0 failures, expected assertion totals.
  5. Credential/LON redaction across all evidence text.

Usage: python docs/reviews/evidence/Q-B5B-05-Q1/verify-evidence.py [revision]
"""
import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

sys.dont_write_bytecode = True

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parents[3]
REVISION = sys.argv[1] if len(sys.argv) > 1 else "HEAD"

PACKAGE = "docs/reviews/evidence/Q-B5B-05-Q1"
REPORT = "docs/reviews/Q-B5B-05-Q1-independent-qualification.md"
PROBE_REPORT = "docs/reviews/Q-B5B-05-PROBE-01-producer-report.md"
PROBE_PACKAGE = "docs/reviews/evidence/Q-B5B-05-PROBE-01"


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


def trx_counters(data):
    root = ET.fromstring(data)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = root.find(".//t:Counters", ns)
    return (int(counters.get("total")), int(counters.get("passed")),
            int(counters.get("failed")), int(counters.get("notRunnable")))


EXPECT_TRX = {
    "q5b05q1-probe-windows.trx": (13, 13, 0, 0),
    "q5b05q1-probe-wsl2.trx": (13, 13, 0, 0),
    "q5b05q1-core-windows.trx": (1257, 1257, 0, 0),
    "q5b05q1-core-wsl2.trx": (1257, 1257, 0, 0),
    "q5b05q1-interop-windows.trx": (43, 43, 0, 0),
    "q5b05q1-interop-wsl2.trx": (43, 43, 0, 0),
}
EXPECT_SCENARIOS = {
    "windows": {"R-01": 28, "R-02": 44, "R-03": 28, "R-06": 100, "R-04": 24, "R-05": 26,
                "NC-01": 25, "NC-02": 15, "NC-03": 13, "NC-04": 10, "D-01": 10, "D-02": 10,
                "D-03": 9, "D-04": 13, "D-05": 6},
    "wsl2": {"R-01": 28, "R-02": 44, "R-03": 28, "R-06": 100, "R-04": 24, "R-05": 26,
             "NC-01": 25, "NC-02": 15, "NC-03": 13, "NC-04": 10, "D-01": 10, "D-02": 10,
             "D-03": 9, "D-04": 13, "D-05": 6},
}


def main():
    result = Result()
    print("=" * 76)
    print("Q-B5B-05-Q1 evidence verification at %s" % REVISION)
    print("disposition: SUPPORTED WITH QUALIFICATIONS; Q-B5B-05 remains OPEN")
    print("=" * 76)

    manifest_data = blob(REVISION, PACKAGE + "/manifest.json")
    if not result.check(manifest_data is not None, "manifest present at %s" % REVISION):
        return 1
    manifest = json.loads(manifest_data.decode("utf-8"))

    print("\n1. Commit structure and scope")
    parent = git("show", "-s", "--format=%P", REVISION, binary=False).strip()
    result.check(parent == manifest["baseCommit"], "sole parent is the recorded base commit",
                 "parent=%s expected=%s" % (parent, manifest["baseCommit"]))
    changed = [line.split("\t", 1) for line in
               git("diff", "--name-status", parent, REVISION, binary=False).splitlines() if line]
    result.check(all(s == "A" for s, _ in changed), "commit scope is additions only",
                 ", ".join(p for s, p in changed if s != "A"))
    outside = [p for _, p in changed if p != REPORT and not p.startswith(PACKAGE + "/")]
    result.check(not outside, "commit scope limited to the report and this package", ", ".join(outside))
    protected = [p for _, p in changed
                 if p.startswith("src/") or p.startswith("tests/")
                 or (p.startswith("docs/reviews/probes/Q-B5B-05-PROBE-01"))]
    result.check(not protected, "no production/test/producer-package change", ", ".join(protected[:5]))

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
    result.check(sum(e["bytes"] for e in manifest["files"]) == manifest["totalBytes"],
                 "manifest byte total is consistent")

    print("\n3. Campaign envelopes")
    for name, expected in sorted(EXPECT_TRX.items()):
        data = blob(REVISION, PACKAGE + "/" + name)
        if data is None:
            result.check(False, "%s present" % name)
            continue
        got = trx_counters(data)
        result.check(got == expected, "%s counters %s" % (name, expected), str(got))

    print("\n4. Reviewer reproduction results (both platforms)")
    for platform, scenario_expected in EXPECT_SCENARIOS.items():
        data = blob(REVISION, PACKAGE + "/%s/q5b05q1-reviewer-results.json" % platform)
        if data is None:
            result.check(False, "%s results present" % platform)
            continue
        rows = {r["Id"]: r for r in json.loads(data.decode("utf-8"))}
        result.check(len(rows) == 15 and all(r["Passed"] for r in rows.values()),
                     "%s: 15/15 scenarios passed" % platform,
                     str({k: v["Passed"] for k, v in rows.items() if not v["Passed"]}))
        counts_ok = all(rows[k]["Assertions"] == scenario_expected[k] for k in scenario_expected)
        result.check(counts_ok, "%s: assertion counts match the campaign record" % platform,
                     str({k: (rows[k]["Assertions"], scenario_expected[k])
                          for k in scenario_expected if rows[k]["Assertions"] != scenario_expected[k]}))

    print("\n5. Credential and LOGON redaction")
    leaks = []
    for e in manifest["files"]:
        if not e["path"].endswith((".txt", ".json", ".md")):
            continue
        text = blob(REVISION, e["path"]).decode("utf-8", "replace")
        for needle in ("reviewer-secret", "secret-do-not-log", "B16AA3"):
            if needle in text:
                leaks.append(e["path"] + "::" + needle)
    result.check(not leaks, "no credential or unredacted LOGON payload in evidence",
                 ", ".join(leaks[:5]))

    print("\n" + "=" * 76)
    print("VERIFICATION %s" % ("PASSED" if result.ok else "FAILED: " + "; ".join(result.failures)))
    print("=" * 76)
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main())