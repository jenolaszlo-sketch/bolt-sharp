"""Verify the R1 producer package, source boundaries and campaign outcomes."""

import hashlib
import io
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

R1 = "Q-B5B-09-PLAIN-R1"
PACKAGE = f"docs/reviews/evidence/{R1}"
MANIFEST = f"{PACKAGE}/manifest.json"
ARCHIVE = f"{PACKAGE}/r1-results.zip"
REPORT = f"docs/reviews/{R1}-implementation.md"
ENTRY = "9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c"
ENTRY_TREE = "f0f1d38dbe648ee36c07a490f6217b47a179dd16"
SOURCE = {
    "src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs",
    "src/BoltSharp/Transport/BoltTcpConnection.cs",
    "src/BoltSharp/Transport/BoltTcpServer.cs",
    "tests/BoltSharp.Tests/Transport/PlaintextTerminalCloseTests.cs",
}
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def require(condition, detail):
    if not condition:
        raise ValueError(detail)


def git(*args):
    return subprocess.check_output(["git", *args])


def ref(refname):
    return git("rev-parse", refname).decode().strip()


def blob(rev, path):
    return git("cat-file", "blob", f"{rev}:{path}")


def verify(rev):
    def digest(data):
        return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}

    def delta(before, after):
        return {tuple(line.split("\t", 1)) for line in
                git("diff", "--name-status", before, after).decode().splitlines()}

    impl = ref(f"{rev}^")
    require(ref(f"{ENTRY}^{{tree}}") == ENTRY_TREE, "Entry tree changed")
    require(git("show", "-s", "--format=%P", rev).decode().strip() == impl, "Evidence parent count")
    require(git("show", "-s", "--format=%P", impl).decode().strip() == ENTRY, "Implementation parent")
    require(delta(ENTRY, impl) == {("M", p) for p in SOURCE}, "Unexpected implementation scope")
    require(not git("diff", "--diff-filter=DMRT", "--name-only", ENTRY, rev, "--", "docs"),
            "Historical documentation changed")

    server = "src/BoltSharp/Transport/BoltTcpServer.cs"
    previous_linger_line = next(line for line in blob(ENTRY, server).splitlines()
                                if b"LingerState =" in line)
    require(previous_linger_line in blob(impl, server).splitlines(), "Typed linger setter changed")
    m = json.loads(blob(rev, MANIFEST))
    require(m["implementation"] == {"commit": impl, "tree": ref(f"{impl}^{{tree}}")}, "Wrong implementation")
    require(set(m["source"]) == SOURCE, "Missing source digests")
    for path, expected in m["source"].items():
        require(digest(blob(impl, path)) == expected, f"Source hash: {path}")

    files = m["files"]
    require(delta(impl, rev) == {("A", p) for p in set(files) | {MANIFEST}}, "Unexpected evidence scope")
    require(REPORT in files and ARCHIVE in files and
            all(p == REPORT or p.startswith(PACKAGE + "/") for p in files), "Evidence inventory")
    for path, expected in files.items():
        require(digest(blob(rev, path)) == expected, f"Evidence hash: {path}")

    with zipfile.ZipFile(io.BytesIO(blob(rev, ARCHIVE))) as z:
        names = z.namelist()
        require(len(names) == len(set(names)) and set(names) == set(m["archive"]),
                "Unexpected archive inventory")
        for name, expected in m["archive"].items():
            require(digest(z.read(name)) == expected, f"Archive hash: {name}")

        def tests(name):
            return ET.fromstring(z.read(f"campaign/{name}.trx"))

        def counts(name, passed, failed=0):
            c = tests(name).find("t:ResultSummary/t:Counters", NS).attrib
            got = tuple(int(c[k]) for k in ("total", "passed", "failed", "notExecuted"))
            require(got == (passed + failed, passed, failed, 0), f"Unexpected test counts: {name}: {got}")

        def case(name, needle, message, outcome):
            for result in tests(name).findall("t:Results/t:UnitTestResult", NS):
                if needle in result.get("testName", "") and result.get("outcome") == outcome:
                    out = " ".join(result.find("t:Output", NS).itertext())
                    if message in out:
                        return
            raise ValueError(f"Missing {outcome} oracle for {name}: {needle} / {message}")

        for platform in ("win", "wsl"):
            counts(f"{platform}-baseline", 0, 1)
            case(f"{platform}-baseline", "Q1WriteTimeoutMustEndAtWriteAndFlush",
                 "reason=write-deadline; phase=write;", "Failed")
            case(f"{platform}-baseline", "Q1WriteTimeoutMustEndAtWriteAndFlush",
                 "failed=1; admitted=False; drain=0", "Failed")
            counts(f"{platform}-core", 1257)
            counts(f"{platform}-driver", 43)
            for i in range(1, 4):
                counts(f"{platform}-focused-{i}", 57)
            case(f"{platform}-core", 'scenario: "delay"', "drain, peer-eof, admitted=True", "Passed")
            case(f"{platform}-core", 'scenario: "absolute"', "post-write, total-deadline", "Passed")
            case(f"{platform}-core", 'stimulus: "write-timeout"', "write-deadline,", "Passed")
            counts(f"{platform}-nc-08", 1, 5)
            case(f"{platform}-nc-08", 'scenario: "delay"', "delay: write, write-deadline, admitted=False", "Failed")

        controls = (
            ("01", 'scenario: "small"', "Actual:   1"),
            ("02", "ListenerStopJoinsActualHandle", "IsClosed = False"),
            ("03", "ListenerStopJoinsActualHandle", '"drain-deadline"'),
            ("04", "OneDeadlineCoversWriteAndDrain", '"drain-deadline"'),
            ("05", 'scenario: "large"', "12288"),
            ("06", "connectionLimit: 4", "Actual:   2"),
            ("07", "TlsRefusalNeverEntersPlaintextHelper", "Name = write-start"),
        )
        for number, test, message in controls:
            case(f"win-nc-{number}", test, message, "Failed")
        counts("win-nc-00", 18)
        counts("win-nc-00-restored", 18)
        counts("wsl-nc-00-restored", 6)
        require(b"format-exit=0" in z.read("campaign/win-format-status.log"), "Scoped format incomplete")
        require(b"PASS 8 blobs, 137 members" in z.read("campaign/verify-q1.log"), "Q1 integrity replay missing")
    print(f"PASS four source blobs, {len(files)} evidence blobs, {len(names)} archive members, both platforms and eight controls")
    print(f"Implementation {impl} {ref(f'{impl}^{{tree}}')}")
    print(f"Evidence       {ref(rev)} {ref(f'{rev}^{{tree}}')}")


if __name__ == "__main__":
    verify(sys.argv[1] if len(sys.argv) > 1 else "HEAD")
