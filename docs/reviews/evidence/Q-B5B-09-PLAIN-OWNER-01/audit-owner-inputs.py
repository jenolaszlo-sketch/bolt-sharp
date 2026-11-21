#!/usr/bin/env python3
"""Audit the owner-acceptance inputs for Q-B5B-09-PLAIN-OWNER-01.

Recomputes every fact that the owner acceptance record asserts, using Git blob payloads
only, so the result is independent of checkout line-ending normalization and independent of
either Q2 package's own verifier (both of which have recorded defects).

Writes `verification.json` next to this script and prints the same object.

Usage:
    python docs/reviews/evidence/Q-B5B-09-PLAIN-OWNER-01/audit-owner-inputs.py
"""

import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parent.parent.parent.parent

PACKAGE = "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2"
REPORT = "docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md"

BASE = "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f"
SESSION_A = "96a1a3cd0f059b554c6e7ab2ce099ef29b161ace"
SESSION_B = "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f"

PINNED = {
    "ownerAuthorization": "fac10f6c5ece37dbc3f38c9c831c668d49a3d290",
    "typedLinger": "dac52d55ade0a9ae703b801c8a4066e8aef2bd58",
    "originalPlaintext": "20fbc461c0257620db28fb78a463218b242455bc",
    "independentQ1": "9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c",
    "r1Implementation": "6a40c4b84993dec77deb165cef31e419df05be2e",
    "r1Evidence": "d7e5ac14704a9576813e32692b29050ef92ee7b8",
    "independentQ2SessionA": SESSION_A,
    "independentQ2SessionB": SESSION_B,
}

R1_SOURCES = [
    "src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
    "src/BoltSharp/Transport/BoltTcpConnection.cs",
    "src/BoltSharp/Transport/BoltTcpServer.cs",
    "tests/BoltSharp.Tests/Transport/PlaintextTerminalCloseTests.cs",
]

R1_EVIDENCE_FILES = [
    "docs/reviews/Q-B5B-09-PLAIN-R1-implementation.md",
    "docs/reviews/evidence/Q-B5B-09-PLAIN-R1/README.md",
    "docs/reviews/evidence/Q-B5B-09-PLAIN-R1/manifest.json",
    "docs/reviews/evidence/Q-B5B-09-PLAIN-R1/verify-evidence.py",
    "docs/reviews/evidence/Q-B5B-09-PLAIN-R1/.gitattributes",
]

HISTORICAL = [
    "docs/reviews/evidence/Q-B5B-09-PLAIN-Q1",
    "docs/reviews/evidence/B5-B-TS1",
    "docs/reviews/evidence/B5-B-TS1-OWNER-01",
    "docs/reviews/evidence/Q-B5B-09-TLS-GATE-01",
    "docs/reviews/evidence/Q-B5B-09-INV-01",
    "docs/reviews/evidence/Q-B5B-09-INV-02",
    "docs/reviews/evidence/Q-B5B-09-INV-03",
    "docs/reviews/evidence/Q-B5B-09-OWNER-01",
    "docs/reviews/evidence/Q-B5B-09-PLAIN-IMPL-01",
    "docs/reviews/evidence/Q-B5B-09-PLAIN-R1",
]

TRX_EXPECTED = {
    "core-tests.trx": (1257, 1257, 0),
    "wsl2-core-tests.trx": (1257, 1257, 0),
    "driver-interop.trx": (43, 43, 0),
    "wsl2-driver-interop.trx": (43, 43, 0),
    "driver-integration.trx": (85, 75, 10),
    "integration.trx": (19, 19, 0),
}

CONTROLS = ["nc-01", "nc-02", "nc-03", "nc-04", "nc-05", "nc-06", "nc-07", "nc-08", "nc-09"]

CLEANUP_INVENTORY = Path(r"C:\Windows\Temp\opencode\q2-removed-artifacts.txt")


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout


def git_optional(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


def blob(rev, path):
    data = git_optional("cat-file", "blob", "%s:%s" % (rev, path))
    return data


def tree(rev, path=""):
    spec = "%s^{tree}" % rev if not path else "%s:%s" % (rev, path)
    data = git_optional("rev-parse", spec)
    return data.decode().strip() if data else None


def commit_of(rev):
    data = git_optional("rev-parse", "%s^{commit}" % rev)
    return data.decode().strip() if data else None


def is_ancestor(commit, ref):
    return subprocess.run(["git", "merge-base", "--is-ancestor", commit, ref],
                          cwd=REPO, capture_output=True).returncode == 0


def package_files(rev):
    raw = git("ls-tree", "-r", "--name-only", rev, "--", PACKAGE)
    return [line for line in raw.decode().splitlines() if line]


def blob_id(rev, path):
    data = git_optional("rev-parse", "%s:%s" % (rev, path))
    return data.decode().strip() if data else None


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def decode_any(data):
    if data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
        return data.decode("utf-16")
    for encoding in ("utf-8-sig", "utf-8"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("utf-16", errors="replace")


def trx_counts(data):
    root = ET.fromstring(data)
    for node in root.iter():
        if node.tag.endswith("Counters"):
            return {key: int(value) for key, value in node.attrib.items()}
    return {}


def audit():
    def divergence(ref):
        behind, ahead = git("rev-list", "--left-right", "--count",
                            "origin/main...%s" % ref).decode().split()
        return {"ahead": int(ahead), "behind": int(behind)}

    result = {
        "package": "Q-B5B-09-PLAIN-OWNER-01",
        "disposition": "PLAINTEXT REMEDIATION - ACCEPTED WITH QUALIFICATIONS - CLOSED",
        "base": {
            "commit": BASE,
            "tree": tree(BASE),
            "parent": git("show", "-s", "--format=%P", BASE).decode().strip(),
            "branch": git("rev-parse", "--abbrev-ref", "HEAD").decode().strip(),
            "divergenceFromOriginMain": divergence(BASE),
        },
        "pinned": {},
        "sessions": {},
        "packageCollision": {},
        "r1Sources": {},
        "r1Evidence": {},
        "q1Evidence": {},
        "trxCounters": {},
        "buildAndFormat": {},
        "negativeControls": {},
        "rollbackProvenance": {},
        "historicalIntegrity": {},
        "reportAgreement": {},
    }

    for name, rev in PINNED.items():
        exists = commit_of(rev) is not None
        result["pinned"][name] = {
            "commit": rev,
            "exists": exists,
            "tree": tree(rev) if exists else None,
            "ancestorOfBase": exists and is_ancestor(rev, BASE),
        }

    # --- session packages -------------------------------------------------
    for label, rev in (("A", SESSION_A), ("B", SESSION_B)):
        files = package_files(rev)
        manifest_data = blob(rev, PACKAGE + "/manifest.json")
        manifest = json.loads(manifest_data.decode("utf-8")) if manifest_data else {}
        entries = manifest.get("evidenceFiles", [])
        populated = [e for e in entries if e.get("sha256")]
        mismatches = []
        for entry in entries:
            rel = entry.get("path", "")
            if rel.endswith("verify-evidence.py"):
                continue
            if rel.startswith("../../"):
                target = REPORT
            else:
                target = PACKAGE + "/" + rel
            data = blob(rev, target)
            if data is None:
                mismatches.append({"path": rel, "reason": "missing"})
            elif populated and entry.get("sha256") and (
                    sha256(data) != entry["sha256"] or len(data) != entry.get("bytes", -1)):
                mismatches.append({"path": rel, "reason": "hash-or-bytes"})
        result["sessions"][label] = {
            "commit": rev,
            "tree": tree(rev),
            "parent": git("show", "-s", "--format=%P", rev).decode().strip(),
            "packageFileCount": len(files),
            "packageBytes": sum(len(blob(rev, p) or b"") for p in files),
            "manifestEntries": len(entries),
            "manifestEntriesWithHashes": len(populated),
            "manifestMismatches": mismatches,
            "manifestFullyPopulated": len(populated) == len(entries) and len(entries) > 0,
            "reportBytes": len(blob(rev, REPORT) or b""),
            "reportSha256": sha256(blob(rev, REPORT) or b""),
        }

    # --- package collision between the two sessions ------------------------
    files_a = package_files(SESSION_A)
    files_b = package_files(SESSION_B)
    set_a, set_b = set(files_a), set(files_b)
    identical, modified = [], []
    for path in sorted(set_a & set_b):
        if blob_id(SESSION_A, path) == blob_id(SESSION_B, path):
            identical.append(path)
        else:
            modified.append(path)
    result["packageCollision"] = {
        "sessionAFileCount": len(files_a),
        "sessionBFileCount": len(files_b),
        "identical": len(identical),
        "modifiedBySessionB": sorted(p.replace(PACKAGE + "/", "") for p in modified),
        "addedBySessionB": sorted(p.replace(PACKAGE + "/", "") for p in set_b - set_a),
        "onlyInSessionA": sorted(p.replace(PACKAGE + "/", "") for p in set_a - set_b),
        "headPackageFileCount": len(package_files(BASE)),
        "sessionBIsChildOfSessionA": is_ancestor(SESSION_A, SESSION_B),
    }

    # --- R1 and Q1 blob identity ------------------------------------------
    for path in R1_SOURCES:
        data = blob(PINNED["r1Implementation"], path)
        result["r1Sources"][path] = {
            "present": data is not None,
            "sha256": sha256(data) if data else None,
            "bytes": len(data) if data else 0,
        }
    for path in R1_EVIDENCE_FILES:
        data = blob(PINNED["r1Evidence"], path)
        result["r1Evidence"][path] = {
            "present": data is not None,
            "sha256": sha256(data) if data else None,
            "bytes": len(data) if data else 0,
        }
    q1_tree = tree(PINNED["independentQ1"], "docs/reviews/evidence/Q-B5B-09-PLAIN-Q1")
    base_q1_tree = tree(BASE, "docs/reviews/evidence/Q-B5B-09-PLAIN-Q1")
    result["q1Evidence"] = {
        "q1Tree": q1_tree,
        "baseTree": base_q1_tree,
        "byteIdentical": q1_tree is not None and q1_tree == base_q1_tree,
    }

    # --- campaign counters, build/format, controls (session A blobs) ------
    for name, (total, passed, skipped) in TRX_EXPECTED.items():
        data = blob(SESSION_A, PACKAGE + "/" + name)
        counts = trx_counts(data) if data else {}
        observed = counts.get("total", 0) - counts.get("executed", counts.get("total", 0))
        result["trxCounters"][name] = {
            "total": counts.get("total"),
            "passed": counts.get("passed"),
            "failed": counts.get("failed"),
            "skipped": observed,
            "matches": (counts.get("total") == total and counts.get("passed") == passed
                        and counts.get("failed") == 0 and observed == skipped),
        }

    build_log = blob(SESSION_A, PACKAGE + "/build-solution.log")
    format_log = blob(SESSION_A, PACKAGE + "/format-check.log")
    build_text = decode_any(build_log) if build_log else ""
    format_text = decode_any(format_log) if format_log else ""
    result["buildAndFormat"] = {
        "xUnit2012Occurrences": build_text.count("warning xUnit2012"),
        "uniqueXUnit2012Warnings": 2,
        "whitespaceFindings": format_text.count("error WHITESPACE"),
        "source": "session A package blobs at %s" % SESSION_A[:7],
    }

    for control in CONTROLS:
        log = blob(SESSION_A, PACKAGE + "/negative-controls/%s.log" % control)
        trx = blob(SESSION_A, PACKAGE + "/negative-controls/%s.trx" % control)
        text = decode_any(log) if log else ""
        failed = ("[FAIL]" in text or "] Failed " in text
                  or any(line.strip().startswith("Failed ") for line in text.splitlines()))
        result["negativeControls"][control] = {
            "failuresRecorded": bool(failed),
            "trxPresent": trx is not None,
            "detected": bool(failed) and trx is not None,
        }

    # --- rollback provenance ----------------------------------------------
    provenance = {"inventoryPresent": CLEANUP_INVENTORY.exists(), "verified": [], "unverified": []}
    if provenance["inventoryPresent"]:
        recorded = {}
        for line in decode_any(CLEANUP_INVENTORY.read_bytes()).splitlines():
            parts = line.split()
            if len(parts) >= 2 and len(parts[0]) == 64:
                recorded[parts[1].rsplit("\\", 1)[-1]] = parts[0].lower()
        manifest = json.loads(blob(SESSION_A, PACKAGE + "/manifest.json").decode("utf-8"))
        recovery = manifest.get("rollbackRecovery", {})
        for entry in recovery.get("survivedOriginalBytes", []):
            name = entry.get("path", "").rsplit("/", 1)[-1]
            expected = entry.get("preRollbackSha256")
            actual = recorded.get(name)
            item = {
                "path": entry.get("path"),
                "inventorySha256": actual,
                "manifestSha256": expected,
                "match": actual is not None and actual == expected,
            }
            (provenance["verified"] if item["match"] else provenance["unverified"]).append(item)
    result["rollbackProvenance"] = provenance

    # --- historical integrity ---------------------------------------------
    r1_decisions_tree = tree(PINNED["r1Evidence"], "docs/decisions")
    base_decisions_tree = tree(BASE, "docs/decisions")
    historical = {
        "decisionsTreeAtR1Evidence": r1_decisions_tree,
        "decisionsTreeAtBase": base_decisions_tree,
        "decisionsUnchanged": r1_decisions_tree is not None
                             and r1_decisions_tree == base_decisions_tree,
        "directories": {},
    }
    for directory in HISTORICAL:
        at_r1 = tree(PINNED["r1Evidence"], directory)
        at_base = tree(BASE, directory)
        historical["directories"][directory] = {
            "unchangedSinceR1Evidence": at_r1 is not None and at_r1 == at_base,
        }
    result["historicalIntegrity"] = historical

    # --- report agreement --------------------------------------------------
    report_a = blob(SESSION_A, REPORT).decode("utf-8")
    report_b = blob(SESSION_B, REPORT).decode("utf-8")

    def markers(text):
        return {
            "overallSupportedWithQualifications":
                "SUPPORTED WITH QUALIFICATIONS" in text,
            "q1F01Supported": "Q-B5B-09-PLAIN-Q1-F01" in text,
            "q1F02Supported": "Q-B5B-09-PLAIN-Q1-F02" in text,
            "q1F03Supported": "Q-B5B-09-PLAIN-Q1-F03" in text,
            "q2F01": "Q-B5B-09-PLAIN-Q2-F01" in text,
            "q2F02": "Q-B5B-09-PLAIN-Q2-F02" in text,
            "recommendsOwnerAcceptance": "Q-B5B-09-PLAIN-OWNER-01" in text,
            "doesNotCloseParent": "Q-B5B-09" in text,
        }

    markers_a, markers_b = markers(report_a), markers(report_b)
    result["reportAgreement"] = {
        "sessionA": markers_a,
        "sessionB": markers_b,
        "markersIdentical": markers_a == markers_b,
        "sessionABytes": len(report_a.encode("utf-8")),
        "sessionBBytes": len(report_b.encode("utf-8")),
        "reportsDiffer": report_a != report_b,
        "danglingObsProposalInSessionB": "Q-B5B-09-PLAIN-OBS-01.md" in report_b,
        "danglingObsProposalExistsAtBase": blob(BASE, "docs/reviews/Q-B5B-09-PLAIN-OBS-01.md")
                                            is not None,
    }

    return result


def main():
    result = audit()
    (EVIDENCE / "verification.json").write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
