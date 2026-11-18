#!/usr/bin/env python3
"""Audit the Q-B5B-05-PROBE-01 evidence and record q5b05-evidence.json.

Recomputes every fact the producer report asserts, over Git blob payloads only, so the result
is independent of checkout line-ending normalization. Executes no test, probe or network
campaign.

Usage:
    python docs/reviews/evidence/Q-B5B-05-PROBE-01/audit-probe-inputs.py [revision]
"""

import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

EVIDENCE = Path(__file__).resolve().parent
REPO = EVIDENCE.parents[3]
REVISION = sys.argv[1] if len(sys.argv) > 1 else "HEAD"

BASE = "e0b762a5ae716544c7d3a5fa3f4e7c08372b7ac1"
BASE_TREE = "42c64bcc93c5d11206430fe611382fd665d5eb30"

TRX_EXPECTED = {
    "q5b05-probe.trx": (13, 13, 0),
    "q5b05-probe-wsl2.trx": (13, 13, 0),
    "q5b05-core-windows.trx": (1257, 1257, 0),
    "q5b05-core-wsl2.trx": (1257, 1257, 0),
    "q5b05-driver-interop-windows.trx": (43, 43, 0),
    "q5b05-driver-interop-wsl2.trx": (43, 43, 0),
}

PLATFORM_ARTIFACTS = [
    "windows/p01-plaintext-reset-recovery.txt",
    "windows/p02-plaintext-repeated-reset.txt",
    "windows/p03-plaintext-pipelined-ignored.txt",
    "windows/p04-tls-reset-recovery.txt",
    "windows/p05-plaintext-concurrent-client.txt",
    "windows/d01-driver-failure-ignored-sequence.txt",
    "windows/d02-driver-recovery-disposition.txt",
    "windows/d03-driver-retriable-provider-failure.txt",
    "windows/d04-driver-pooling.txt",
    "windows/nc01-replacement-connection.txt",
    "windows/nc02-missing-reset.txt",
    "windows/nc03-injected-recovery-failure.txt",
    "windows/nc04-terminal-failure-substituted.txt",
    "wsl2/p01-plaintext-reset-recovery.txt",
    "wsl2/p02-plaintext-repeated-reset.txt",
    "wsl2/p03-plaintext-pipelined-ignored.txt",
    "wsl2/p04-tls-reset-recovery.txt",
    "wsl2/p05-plaintext-concurrent-client.txt",
    "wsl2/d01-driver-failure-ignored-sequence.txt",
    "wsl2/d02-driver-recovery-disposition.txt",
    "wsl2/d03-driver-retriable-provider-failure.txt",
    "wsl2/d04-driver-pooling.txt",
    "wsl2/nc01-replacement-connection.txt",
    "wsl2/nc02-missing-reset.txt",
    "wsl2/nc03-injected-recovery-failure.txt",
    "wsl2/nc04-terminal-failure-substituted.txt",
]

CREDENTIAL_SENTINEL = "probe-owner-secret"
PRINCIPAL_SENTINEL = "probe-owner-user"

REQUIRED_TRACE_MARKERS = {
    "p01-plaintext-reset-recovery.txt": ["RESET messages observed server-side: 1"],
    "p02-plaintext-repeated-reset.txt": ["RESET messages observed server-side: 3"],
    "p03-plaintext-pipelined-ignored.txt": ["RESET messages observed server-side: 1"],
    "p05-plaintext-concurrent-client.txt": ["accepted=2"],
    "nc01-replacement-connection.txt": ["accepted=2"],
    "nc03-injected-recovery-failure.txt": ["disposal failures: 1"],
    "nc04-terminal-failure-substituted.txt": ["RESET messages observed: 0"],
}

DRIVER_MARKERS = {
    "d01-driver-failure-ignored-sequence.txt": [
        "driver exception: ProtocolException",
        "gqlStatus=50N42",
        "retriable=False",
    ],
    "d02-driver-recovery-disposition.txt": [
        "recovery on the SAME physical connection: False",
    ],
    "d03-driver-retriable-provider-failure.txt": [
        "DatabaseException",
        "gqlStatus=50N00",
    ],
    "d04-driver-pooling.txt": [
        "physical connections observed by the proxy: 1",
    ],
}

PROBE_SOURCE = [
    "docs/reviews/probes/Q-B5B-05-PROBE-01/BoltSharp.Q5B05.Probe.csproj",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/ResetRecovery.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/TlsRecovery.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/DriverResetBehavior.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/NegativeControls.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/Infra/ProbeHost.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/Infra/ProbeClient.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/Infra/ProbeMessages.cs",
    "docs/reviews/probes/Q-B5B-05-PROBE-01/Infra/ConnectionIdentity.cs",
]

EVIDENCE_DOCS = [
    "docs/reviews/evidence/Q-B5B-05-PROBE-01/commissioning-record.md",
    "docs/reviews/evidence/Q-B5B-05-PROBE-01/requirements-matrix.md",
    "docs/reviews/evidence/Q-B5B-05-PROBE-01/driver-behavior-assessment.md",
    "docs/reviews/evidence/Q-B5B-05-PROBE-01/negative-controls-results.md",
    "docs/reviews/evidence/Q-B5B-05-PROBE-01/README.md",
    "docs/reviews/Q-B5B-05-PROBE-01-producer-report.md",
]


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError("git %s failed: %s" % (" ".join(args), proc.stderr.decode()))
    return proc.stdout


def blob(rev, path):
    proc = subprocess.run(["git", "cat-file", "blob", "%s:%s" % (rev, path)],
                          cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


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
    result = {
        "commission": "Q-B5B-05-PROBE-01",
        "disposition": "PRODUCTION LIMITATION IDENTIFIED - OWNER DECISION REQUIRED",
        "parentQualificationStatus": "Q-B5B-05 remains OPEN",
        "base": {"commit": BASE, "tree": BASE_TREE},
        "baseTreeMatches": git("rev-parse", "%s^{tree}" % BASE).decode().strip() == BASE_TREE,
        "trxCounters": {},
        "platformArtifacts": {},
        "traceMarkers": {},
        "driverMarkers": {},
        "redaction": {},
        "probeSource": {},
    }
    # The commit identity is deliberately not recorded here: this file is committed with the
    # evidence it describes, so recording its own commit hash would be self-referential and
    # could never converge. The verifier checks the commit structure separately (check 1).

    for name, (total, passed, skipped) in sorted(TRX_EXPECTED.items()):
        data = blob(REVISION, "docs/reviews/evidence/Q-B5B-05-PROBE-01/" + name)
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

    for rel in PLATFORM_ARTIFACTS:
        data = blob(REVISION, "docs/reviews/evidence/Q-B5B-05-PROBE-01/" + rel)
        result["platformArtifacts"][rel] = {
            "present": data is not None,
            "bytes": len(data) if data else 0,
            "sha256": hashlib.sha256(data).hexdigest() if data else None,
        }

    def markers_for(table):
        out = {}
        for platform in ("windows", "wsl2"):
            for name, markers in table.items():
                rel = "%s/%s" % (platform, name)
                data = blob(REVISION, "docs/reviews/evidence/Q-B5B-05-PROBE-01/" + rel)
                text = decode_any(data) if data else ""
                out[rel] = {
                    "required": markers,
                    "allPresent": bool(text) and all(m in text for m in markers),
                }
        return out

    result["traceMarkers"] = markers_for(REQUIRED_TRACE_MARKERS)
    result["driverMarkers"] = markers_for(DRIVER_MARKERS)

    leaks = []
    checked = 0
    package = "docs/reviews/evidence/Q-B5B-05-PROBE-01/"
    for rel in PLATFORM_ARTIFACTS + EVIDENCE_DOCS:
        path = package + rel if rel.startswith(("windows/", "wsl2/", "commissioning-record.md",
                                               "requirements-matrix.md",
                                               "driver-behavior-assessment.md",
                                               "negative-controls-results.md", "README.md")) else rel
        data = blob(REVISION, path)
        if data is None:
            continue
        checked += 1
        text = decode_any(data)
        if CREDENTIAL_SENTINEL in text or PRINCIPAL_SENTINEL in text:
            leaks.append(path)
        if "REDACTED(LOGON)" not in text and "C2S LOGON" in text:
            # LOGON payloads must never be stored; the proxy redacts them by name.
            leaks.append(path + " (LOGON not redacted)")
    result["redaction"] = {"filesChecked": checked, "credentialLeaks": leaks, "clean": not leaks}

    for path in PROBE_SOURCE:
        data = blob(REVISION, path)
        result["probeSource"][path] = {
            "present": data is not None,
            "bytes": len(data) if data else 0,
            "sha256": hashlib.sha256(data).hexdigest() if data else None,
        }

    return result


def main():
    result = audit()
    (EVIDENCE / "q5b05-evidence.json").write_text(
        json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())