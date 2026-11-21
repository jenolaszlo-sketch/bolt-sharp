"""Verify the committed producer evidence using exact Git blob bytes."""

import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ID = "Q-B5B-09-PLAIN-IMPL-01"
PACKAGE = f"docs/reviews/evidence/{ID}"
REPORT = f"docs/reviews/{ID}-implementation.md"
NAMES = {".gitattributes", "README.md", "identity.json", "manifest.json",
         "negative-controls.json", "producer-results.zip", "summary.json", "verify-evidence.py"}
BASELINE = "fac10f6c5ece37dbc3f38c9c831c668d49a3d290"
LINGER_FILES = {
    "src/BoltSharp/Transport/BoltTcpServer.cs",
    "tests/BoltSharp.Tests/Transport/TcpListenerTests.cs",
    "tests/BoltSharp.Tests/Transport/TlsIntegrationTests.cs",
}
FEATURE_FILES = {
    "src/BoltSharp/Sessions/BoltConnectionSession.cs",
    "src/BoltSharp/Sessions/BoltSessionResult.cs",
    "src/BoltSharp/Transport/BoltTcpConnection.cs",
    "src/BoltSharp/Transport/BoltTcpServer.cs",
    "src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
    "src/BoltSharp/Transport/BoltPlaintextTerminalCloseDiagnostics.cs",
    "tests/BoltSharp.Tests/Transport/PlaintextTerminalCloseTests.cs",
    "tests/BoltSharp.DriverInteropTests/Scenarios/PlaintextTerminalCloseInteropTests.cs",
}
FINAL_COUNTS = {
    "win-core-final.trx": 1250, "wsl-core-final.trx": 1250,
    "win-driver-final.trx": 43, "wsl-driver-final.trx": 43,
    "win-baseline.trx": 4, "wsl-baseline.trx": 4,
    "win-linger-accepted.trx": 2, "wsl-linger-accepted.trx": 2,
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def git(*args):
    return subprocess.check_output(["git", *args])


def ref(expression):
    return git("rev-parse", "--verify", "--end-of-options", expression).decode().strip()


def blob(revision, path):
    return git("cat-file", "blob", f"{revision}:{path}")


def digest(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def changes(before, after):
    return set(git("diff", "--no-renames", "--name-status", before, after).decode().splitlines())


def results(data):
    doc = ET.fromstring(data)
    return doc.findall(".//{*}UnitTestResult")


def verify(revision):
    evidence = ref(revision + "^{commit}")
    implementation = ref(evidence + "^")
    linger = ref(implementation + "^")
    require(ref(linger + "^") == BASELINE, "Unexpected baseline")
    for child, parent in [(evidence, implementation), (implementation, linger), (linger, BASELINE)]:
        require(git("show", "-s", "--format=%P", child).decode().split() == [parent], "Unexpected commit parents")

    def package(name):
        return blob(evidence, f"{PACKAGE}/{name}")

    manifest = json.loads(package("manifest.json"))
    require(manifest["hash_convention"] == "SHA-256 over exact Git blob payload bytes", "Invalid hash convention")
    expected = {f"{PACKAGE}/{name}" for name in NAMES} | {REPORT}
    require(changes(implementation, evidence) == {f"A\t{path}" for path in expected}, "Unexpected evidence diff")
    require(set(manifest["entries"]) == expected - {f"{PACKAGE}/manifest.json"}, "Unexpected manifest paths")
    for path, item in manifest["entries"].items():
        require(digest(blob(evidence, path)) == item, f"Invalid evidence blob {path}")

    identity = json.loads(package("identity.json"))
    for label, commit in [("baseline", BASELINE), ("linger", linger), ("implementation", implementation)]:
        require(identity[label] == {"commit": commit, "tree": ref(commit + "^{tree}")}, f"Incorrect {label} identity")

    require(changes(BASELINE, linger) == {f"M\t{path}" for path in LINGER_FILES}, "Unexpected linger diff")
    added = {path for path in FEATURE_FILES if Path(path).name in {
        "BoltPlaintextTerminalClose.cs", "BoltPlaintextTerminalCloseDiagnostics.cs",
        "PlaintextTerminalCloseTests.cs", "PlaintextTerminalCloseInteropTests.cs"}}
    expected_changes = {f"{'A' if path in added else 'M'}\t{path}" for path in FEATURE_FILES}
    require(changes(linger, implementation) == expected_changes, "Unexpected implementation diff")
    tested = identity["implementation_tested_blobs"]
    require(set(tested) == FEATURE_FILES, "Unexpected tested source inventory")
    for path, item in tested.items():
        require(digest(blob(implementation, path)) == item, f"Tested input differs from commit at {path}")

    summary = json.loads(package("summary.json"))
    controls = json.loads(package("negative-controls.json"))
    require(set(controls["restored_sha256"]) <= FEATURE_FILES, "Control changed an out-of-scope path")
    for path, sha256 in controls["restored_sha256"].items():
        require(sha256 == tested[path]["sha256"], f"Control source preimage mismatch for {path}")
    with zipfile.ZipFile(io.BytesIO(package("producer-results.zip"))) as archive:
        require(archive.testzip() is None, "Damaged archive")
        require(sorted(archive.namelist()) == sorted(summary["archive_members"]), "Unexpected archived members")
        for name, item in summary["archive_members"].items():
            require(digest(archive.read(name)) == item, f"Invalid archive member {name}")
        for name, count in FINAL_COUNTS.items():
            sample = results(archive.read(name))
            require(len(sample) == count and all(row.get("outcome") == "Passed" for row in sample), f"Unexpected cases in {name}")
        cases = controls["cases"]
        require([case["control"] for case in cases] == [f"NC-{n:02}" for n in range(8)], "Unexpected controls")
        for n, case in enumerate(cases):
            sample = results(archive.read(case["control"] + ".trx"))
            failed = sum(row.get("outcome") == "Failed" for row in sample)
            passed = sum(row.get("outcome") == "Passed" for row in sample)
            require((len(sample), failed, passed) == (case["total"], case["failed"], case["passed"]),
                    f"Unexpected control case counts for {case['control']}")
            require(case["source_restored"] and bool(failed) == bool(n) and case["exit"] == bool(n),
                    f"Unexpected mutation result for {case['control']}")

    owner = blob(BASELINE, "docs/reviews/evidence/Q-B5B-09-OWNER-01/verify-evidence.py")
    subprocess.run([sys.executable, "-", BASELINE], input=owner, check=True)
    print(f"PASS {len(manifest['entries'])} evidence blobs, {len(summary['archive_members'])} archive members, "
          "tested source digests, 8 passing campaigns, and 7 detected controls")
    print(f"Commit {evidence}")
    print(f"Tree   {ref(evidence + '^{tree}')}")


if __name__ == "__main__":
    if len(sys.argv) > 2:
        sys.exit("Usage: python verify-evidence.py [evidence-commit]")
    try:
        verify(sys.argv[1] if len(sys.argv) == 2 else "HEAD")
    except (OSError, KeyError, ValueError, subprocess.CalledProcessError) as error:
        sys.exit(f"FAIL {error}")
