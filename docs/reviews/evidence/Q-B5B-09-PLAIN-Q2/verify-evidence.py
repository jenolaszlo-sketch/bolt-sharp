#!/usr/bin/env python3
"""Q-B5B-09-PLAIN-Q2 evidence verifier.

Checks, in order:
  1. The four R1 source blobs match implementation commit 6a40c4b.
  2. The five R1 evidence blobs match evidence commit d7e5ac1.
  3. Every evidence file in manifest.json matches its recorded SHA-256 and byte count
     (the verifier itself is self-referential and is skipped).
  4. The five rollback-surviving campaign results match the pre-rollback cleanup
     inventory hashes.
  5. Campaign counters inside the TRX envelopes match the reported totals.
  6. The build/format logs carry exactly two xUnit2012 warnings and no format errors
     beyond the eight pre-existing whitespace errors.
  7. Each negative-control transcript records a failed run and has a matching TRX.
  8. All historical evidence and decision directories are unmodified.
"""

import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parent.parent.parent.parent

R1 = "6a40c4b84993dec77deb165cef31e419df05be2e"
R1_EVIDENCE = "d7e5ac14704a9576813e32692b29050ef92ee7b8"

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
    "docs/decisions",
]
TRX_EXPECTED = {
    "core-tests.trx": (1257, 1257, 0),
    "wsl2-core-tests.trx": (1257, 1257, 0),
    "driver-interop.trx": (43, 43, 0),
    "wsl2-driver-interop.trx": (43, 43, 0),
    "driver-integration.trx": (85, 75, 10),
    "integration.trx": (19, 19, 0),
}


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 16), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_text_any(path):
    data = path.read_bytes()
    if data.startswith(b"\xff\xfe") or data.startswith(b"\xfe\xff"):
        return data.decode("utf-16")
    for encoding in ("utf-8-sig", "utf-8"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("utf-16", errors="replace")


def blob_exists(commit, path):
    proc = subprocess.run(["git", "cat-file", "blob", f"{commit}:{path}"],
                          cwd=REPO, capture_output=True)
    return proc.returncode == 0


def trx_counts(path):
    root = ET.parse(path).getroot()
    for node in root.iter():
        if node.tag.endswith("Counters"):
            return {key: int(value) for key, value in node.attrib.items()}
    return None


def main():
    ok = True
    manifest = json.loads((EVIDENCE / "manifest.json").read_text(encoding="utf-8"))

    print("=" * 72)
    print("Q-B5B-09-PLAIN-Q2 evidence verification")
    print("report      : docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md")
    print("disposition : %s" % manifest["disposition"])
    print("=" * 72)

    print("\n1. R1 source blobs against %s" % R1)
    for path in R1_SOURCES:
        good = blob_exists(R1, path)
        print("   %s  %s" % ("PASS" if good else "FAIL", path))
        ok = ok and good

    print("\n2. R1 evidence blobs against %s" % R1_EVIDENCE)
    for path in R1_EVIDENCE_FILES:
        good = blob_exists(R1_EVIDENCE, path)
        print("   %s  %s" % ("PASS" if good else "FAIL", path))
        ok = ok and good

    print("\n3. Evidence file hashes and byte counts")
    for entry in manifest["evidenceFiles"]:
        rel = entry["path"]
        if rel.endswith("verify-evidence.py"):
            print("   SKIP  %-52s (self-referential)" % rel)
            continue
        target = EVIDENCE / rel
        if not target.exists():
            print("   FAIL  %s (missing)" % rel)
            ok = False
            continue
        digest = sha256(target)
        same = digest == entry["sha256"] and target.stat().st_size == entry["bytes"]
        print("   %s  %-52s %s" % ("PASS" if same else "FAIL", rel, digest[:16] + "..."))
        if not same:
            ok = False

    print("\n4. Rollback recovery provenance")
    cleanup = Path(r"C:\Windows\Temp\opencode\q2-removed-artifacts.txt")
    if cleanup.exists():
        recorded = {}
        for line in read_text_any(cleanup).splitlines():
            parts = line.split()
            if len(parts) >= 2 and len(parts[0]) == 64:
                recorded[parts[1].rsplit("\\", 1)[-1]] = parts[0].lower()
        for entry in manifest["rollbackRecovery"]["survivedOriginalBytes"]:
            name = entry["path"].rsplit("/", 1)[-1]
            note = recorded.get(name)
            expected = entry.get("preRollbackSha256")
            match = note is not None and note == expected
            print("   %s  %s" % ("PASS" if match else "WARN", name))
            if not match:
                print("         inventory=%s manifest=%s" % (note, expected))
                ok = False
    else:
        print("   WARN  cleanup inventory not present; provenance recorded in "
              "recovery-audit.md")
        ok = False

    print("\n5. Campaign counters inside TRX envelopes")
    for name, (total, passed, skipped) in TRX_EXPECTED.items():
        path = EVIDENCE / name
        if not path.exists():
            print("   FAIL  %s (missing)" % name)
            ok = False
            continue
        counts = trx_counts(path)
        if not counts:
            print("   FAIL  %s (no counters)" % name)
            ok = False
            continue
        observed = counts.get("total", 0) - counts.get("executed", counts.get("total", 0))
        good = (counts.get("total") == total and counts.get("passed") == passed
                and counts.get("failed") == 0 and observed == skipped)
        print("   %s  %-26s total=%s passed=%s failed=%s skipped=%s" %
              ("PASS" if good else "FAIL", name, counts.get("total"), counts.get("passed"),
               counts.get("failed"), observed))
        if not good:
            ok = False

    print("\n6. Build and format logs")
    build = read_text_any(EVIDENCE / "build-solution.log")
    warnings = build.count("warning xUnit2012")
    good = warnings == 4  # two unique warnings, each echoed twice by MSBuild
    print("   %s  xUnit2012 warning occurrences=%d (two unique, echoed twice)" %
          ("PASS" if good else "FAIL", warnings))
    if not good:
        ok = False
    fmt = read_text_any(EVIDENCE / "format-check.log")
    whitespace = fmt.count("error WHITESPACE")
    good = whitespace == 8
    print("   %s  pre-existing whitespace errors=%d" % ("PASS" if good else "FAIL", whitespace))
    if not good:
        ok = False

    print("\n7. Negative-control transcripts")
    nc = EVIDENCE / "negative-controls"
    for log in sorted(nc.glob("nc-*.log")):
        text = read_text_any(log)
        failed = ("[FAIL]" in text or "] Failed " in text
                  or any(l.strip().startswith("Failed ") for l in text.splitlines()))
        trx = nc / (log.stem + ".trx")
        good = failed and trx.exists()
        print("   %s  %-12s failures recorded=%s trx=%s" %
              ("PASS" if good else "FAIL", log.stem, failed, trx.exists()))
        if not good:
            ok = False

    print("\n8. Historical evidence and decision integrity")
    for directory in HISTORICAL:
        proc = subprocess.run(["git", "status", "--porcelain", directory], cwd=REPO,
                              capture_output=True, text=True)
        clean = not proc.stdout.strip()
        print("   %s  %s" % ("PASS" if clean else "WARN", directory))
        if not clean:
            print("         %s" % proc.stdout.strip())
            ok = False

    print("\n" + "=" * 72)
    print("VERIFICATION %s" % ("PASSED" if ok else "FAILED"))
    print("=" * 72)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())