"""Verify the committed Q1 report and campaign records over Git blob bytes."""

import hashlib
import io
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

Q1 = "Q-B5B-09-PLAIN-Q1"
ROOT = f"docs/reviews/evidence/{Q1}"
REPORT = f"docs/reviews/{Q1}-qualification.md"
PARENT = "5d27b39c32a2d1e56a3d8ef7ce116a36445427cf"
OWNER = "fac10f6c5ece37dbc3f38c9c831c668d49a3d290"
OWNER_TREE = "fceee363ebcc72c99263d2bfe5e210a9c49cea4d"
MANIFEST = f"{ROOT}/manifest.json"
ARCHIVE = f"{ROOT}/independent-results.zip"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require(condition, what):
    if not condition:
        raise ValueError(what)


def git(*args):
    return subprocess.check_output(["git", *args])


def identity(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def verify(rev):
    def blob(path):
        return git("cat-file", "blob", f"{rev}:{path}")

    require(git("show", "-s", "--format=%P", rev).decode().strip() == PARENT,
            "Unexpected qualification parent")
    require(git("rev-parse", f"{rev}:docs/decisions").decode().strip() == OWNER_TREE,
            "Owner-decision tree changed")
    require(not git("diff", "--diff-filter=DMRT", "--name-only", OWNER, rev, "--", "docs"),
            "Historical documentation modified")
    m = json.loads(blob(MANIFEST))
    files = m["files"]
    delta = {tuple(row.split("\t", 1)) for row in
             git("diff", "--name-status", PARENT, rev).decode().splitlines()}
    require(delta == {("A", p) for p in set(files) | {MANIFEST}}, "Unexpected commit scope")
    require(REPORT in files and ARCHIVE in files, "Missing report or archive")
    require(all(p == REPORT or p.startswith(ROOT + "/") for p in files), "Path outside package")
    for path, expected in files.items():
        require(identity(blob(path)) == expected, f"Blob mismatch: {path}")

    with zipfile.ZipFile(io.BytesIO(blob(ARCHIVE))) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)) and set(names) == set(m["archive"]),
                "Archive inventory mismatch")
        for name, expected in m["archive"].items():
            require(identity(archive.read(name)) == expected, f"Archive member mismatch: {name}")

        def results(name):
            return ET.fromstring(archive.read(f"campaign/{name}.trx"))

        def counts(name, passed, failed=0):
            counter = results(name).find("t:ResultSummary/t:Counters", NS).attrib
            actual = tuple(int(counter[key]) for key in ("total", "passed", "failed", "notExecuted"))
            require(actual == (passed + failed, passed, failed, 0), f"Campaign counts: {name} {actual}")

        def failure(name, test, message):
            for item in results(name).findall("t:Results/t:UnitTestResult", NS):
                if item.get("outcome") == "Failed" and test in item.get("testName", ""):
                    output = " ".join(item.find("t:Output", NS).itertext())
                    if message in output:
                        return
            raise ValueError(f"Missing intended failure: {name}: {test}, {message}")

        for platform in ("win", "wsl"):
            counts(f"{platform}-core", 1250)
            counts(f"{platform}-driver", 43)
            for number in range(1, 4):
                counts(f"{platform}-focused-{number}", 50)
            counts(f"{platform}-q1-linger", 8)
            counts(f"{platform}-q1-candidate", 8)
            counts(f"{platform}-q1-bounds", 5)
            counts(f"{platform}-q1-linger-pristine", 2)
            counts(f"{platform}-q1-write-classification", 0, 1)
            failure(f"{platform}-q1-write-classification", "Q1WriteTimeoutMustEndAtWriteAndFlush",
                    "reason=write-deadline; phase=write;")
        counts("win-q1-original", 8)
        counts("wsl-q1-original", 2)
        counts("win-q1-original-pristine", 2)
        counts("wsl-q1-original-pristine", 0, 2)
        failure("wsl-q1-original-pristine", "Start_BindsEphemeralPort", "Invalid argument")
        failure("wsl-q1-original-pristine", "TlsConversation_CompletesScalarQuery", "Invalid argument")
        counts("nc-00", 12)
        counts("nc-00-restored", 12)
        controls = (
            ("nc-01", 1, 5, 'scenario: "small"', "Actual:   1"),
            ("nc-02", 0, 1, "ListenerStopJoinsActualHandle", "IsClosed = False"),
            ("nc-03", 0, 1, "ListenerStopJoinsActualHandle", '"drain-deadline"'),
            ("nc-04", 0, 1, "OneDeadlineCoversWriteAndDrain", '"drain-deadline"'),
            ("nc-05", 5, 1, 'scenario: "large"', "12288"),
            ("nc-06", 2, 1, "connectionLimit: 4", "Actual:   2"),
            ("nc-07", 0, 1, "TlsRefusalNeverEntersPlaintextHelper", "Name = write-start"),
        )
        for case, passed, failed, test, message in controls:
            counts(case, passed, failed)
            failure(case, test, message)
        require(b"PASS 8 evidence blobs, 59 archive members" in archive.read("campaign/producer-verifier.log"),
                "Missing producer integrity replay")
    print(f"PASS {len(files)} blobs, {len(names)} members, campaign counts, seven control detections and two F01 failures")
    print(git("show", "-s", "--format=%H %T", rev).decode().strip())


if __name__ == "__main__":
    verify(sys.argv[1] if len(sys.argv) > 1 else "HEAD")
