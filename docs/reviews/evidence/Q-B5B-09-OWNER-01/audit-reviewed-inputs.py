"""Read the pinned Q-B5B-09 evidence and replay the prior patches without executing them."""

from collections import Counter
import hashlib
from io import BytesIO
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

PARENT = "608eb3f0e07b059eb3bddd86de9235d422e1540c"
CHAIN = [
    PARENT, "44093f65df2b49de380e4debd64b9606f9e11733",
    "4beafcdd4ce48e0c0a4f8a6e05a52db328d7e017", "23d58da4e0c0e10aa46a31f05ef6a0c31ad3f2fa",
    "29d521cea9e65fe2d240421156454444e0d055ea", "b196db8f0bc612b7f1ef3f78412480239c3f04db",
    "d8c28a9bed0af6305936b2b6e427ca1365bed757", "2e0b9da0b8920a2f6cdd7de9fa968891d779090e",
    "17886a5e7f709ef1e01624e49c9d90b50808566c", "79f7965a0e8e351baf2dd388218290804c31389a",
]
NAMES = (
    "Q-B5B-09-TLS-GATE-01", "Q-B5B-09-INV-03", "Q-B5B-09-INV-02",
    "Q-B5B-09-INV-01", "B5-B-TS1-OWNER-01",
)
COUNTS = (12, 12, 13, 11, 5)
REPORTS = (
    "docs/reviews/Q-B5B-09-TLS-GATE-01-report.md",
    "docs/reviews/Q-B5B-09-INV-03-investigation.md",
    "docs/reviews/Q-B5B-09-INV-02-investigation.md",
    "docs/reviews/Q-B5B-09-INV-01-investigation.md",
    "docs/decisions/B5-B-TS1-OWNER-01-owner-closure.md",
)


def git(*args, cwd=None):
    return subprocess.check_output(["git", *args], cwd=cwd)


def check(condition, message):
    if not condition:
        raise ValueError(message)


def blob(commit, path):
    return git("cat-file", "blob", f"{commit}:{path}")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def scope(parent, commit):
    return git("diff", "--no-renames", "--name-status", parent, commit).decode().splitlines()


def package_evidence():
    packages = {}
    for name, commit, count, report, parent in zip(NAMES, CHAIN, COUNTS, REPORTS, CHAIN[1:]):
        directory = f"docs/reviews/evidence/{name}"
        manifest_path = f"{directory}/manifest.json"
        inventory = set(git("ls-tree", "-r", "--name-only", commit, "--", directory).decode().splitlines())
        expected = inventory | {report}
        raw = blob(commit, manifest_path)
        manifest = json.loads(raw)
        entries = manifest["entries"]
        unique = {entry["path"] for entry in entries}
        check(len(entries) == len(unique) == count and unique == expected - {manifest_path}, name + " inventory")
        for item in entries:
            data = blob(commit, item["path"])
            check(len(data) == item["bytes"] and digest(data) == item["sha256"], item["path"] + " evidence hash")
        changed = set(scope(parent, commit))
        check(changed == {f"A\t{p}" for p in expected}, name + " additions-only scope")
        packages[name] = {"commit":commit, "tree":git("rev-parse",commit+"^{tree}").decode().strip(),
                          "manifest_sha256":digest(raw), "entries":count, "scope_additions":len(changed)}
    return packages


def gate_evidence():
    prefix = f"docs/reviews/evidence/{NAMES[0]}"
    config_bytes = blob(PARENT, f"{prefix}/configuration.json")
    config = json.loads(config_bytes)
    identity = json.loads(blob(PARENT, f"{prefix}/source-identity.json"))
    matrix = json.loads(blob(PARENT, f"{prefix}/matrix.json"))
    check(config["maximum_attempts_per_cell_per_platform"] == 3, "Three-attempt rule")
    check(digest(config_bytes) == identity["predeclared_configuration_sha256"], "Configuration digest")
    result = {}
    for platform in ("windows", "wsl2"):
        rows = [json.loads(line) for line in blob(PARENT, f"{prefix}/{platform}.jsonl").splitlines()]
        check(rows[-1]["kind"] == "campaign-end" and not rows[-1]["defect"], platform + " disposition")
        starts = []
        facts = {}
        for row in rows[:-1]:
            key = (row["cell"]["Name"], row["profile"]["Ordinal"])
            check(key[0] in config["cells"] and key[1] in (1,2,3), platform + " valid cell and ordinal")
            if row["kind"] == "started":
                check(key not in starts, platform + " duplicate start")
                starts.append(key)
            elif row["kind"] == "completed":
                check(key in starts and key not in facts, platform + " orphan or duplicate completion")
                facts[key] = row["result"]
            else:
                raise ValueError("Unexpected journal event")
        check(set(starts) == set(facts), platform + " complete journal")
        outcomes = {cell:[facts[key]["Outcome"] for key in starts if key[0] == cell] for cell in config["cells"]}
        for cell, history in outcomes.items():
            check(0 < len(history) <= 3, platform + " completed budget")
            check([key[1] for key in starts if key[0]==cell] == list(range(1,len(history)+1)), "Order")
            check(history == [entry["outcome"] for entry in matrix[platform]["cells"][cell]], "Matrix reconciliation")
            check("induced-and-interrupted" not in history[:-1], "Stopped cell at successful induction")
        check(len(starts) == matrix[platform]["attempt_count"], "Attempt total")
        check(all(row["Closed"] and row["Permits"] == row["ReleasedPermits"]
                  and row["Accounting"]["Active"] == 0 for row in facts.values()), platform + " cleanup")
        result[platform] = {"outcomes_by_cell":outcomes,"attempts":len(starts),
                            "unobserved_task_exceptions":rows[-1]["unobservedTaskExceptions"]}
        if platform == "wsl2":
            for cell, label in (("T1","total-deadline"),("T2","server-cancelled")):
                observed = facts[cell,2]
                check(observed["Confirmed"] and observed["PendingMs"] >= 20
                      and observed["SenderWritableAtSample"] is False, cell + " pending network evidence")
                check(observed["Finish"]["Phase"] == "write" and observed["Finish"]["Outcome"] == label
                      and observed["TerminalResult"] == "Defunct:1" and observed["ParserEntries"] == 5
                      and observed["ProviderPreparations"] == 1, cell + " outcome/terminal scope")
                write = observed["InnerWrites"][0]
                check(write["Stage"] == "terminal-write" and write["Status"] == "Faulted", cell + " joined write")
                if cell == "T2":
                    check(observed["PendingAtListener"] and
                          0 <= write["ElapsedMs"] - observed["ListenerMs"] < 60, "Listener causal bound")
    check(result["windows"]["attempts"] == 12 and result["wsl2"]["attempts"] == 10, "Expected fixed campaign size")
    check(Counter(outcome for history in result["windows"]["outcomes_by_cell"].values()
                  for outcome in history) == {"preceding-write-did-not-complete":12}, "Windows scope")
    check(Counter(outcome for history in result["wsl2"]["outcomes_by_cell"].values()
                  for outcome in history) == {"target-reached-no-persistent-pending-write":8,
                                              "induced-and-interrupted":2}, "WSL2 scope")
    return {"configuration_sha256":digest(config_bytes),"maximum_attempts_per_cell_per_platform":3,**result}


def patch_replay():
    earlier = f"docs/reviews/evidence/{NAMES[2]}"
    refined = f"docs/reviews/evidence/{NAMES[1]}"
    gate = f"docs/reviews/evidence/{NAMES[0]}"
    data = json.loads(blob(PARENT, gate+"/source-identity.json"))
    pristine = {row["path"]:row["sha256"] for row in data["inv03_replayed_source_bytes"]}
    original = {row["path"]:row["sha256"] for row in data["replayed_prototype_source_bytes"]}
    final = data["gate_isolated_source_sha256"]
    patches = [(CHAIN[2],earlier+"/prototype.patch",original),
               (CHAIN[1],refined+"/refinement.patch",None),
               (CHAIN[1],refined+"/real-write-observation.patch",pristine),
               (PARENT,gate+"/link-observation.patch",final),
               (CHAIN[2],earlier+"/wsl-listener-adapter.patch",None)]
    def matches(root, records):
        return all(digest((root / path).read_bytes()) == sha for path, sha in records.items())
    hashes = {}
    with tempfile.TemporaryDirectory(prefix="boltsharp-owner-evidence-") as temp:
        root = Path(temp)
        snapshot = set(git("ls-tree","-r","--name-only",CHAIN[3],"--","src/BoltSharp").decode().splitlines())
        for name in sorted(pristine.keys() & snapshot):
            path = root/name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(blob(CHAIN[3],name).replace(b"\r\n",b"\n"))
        for commit, path, expected in patches:
            raw = blob(commit,path)
            hashes[path] = digest(raw)
            patch = root/"replay.patch"
            patch.write_bytes(raw)
            git("-c","core.autocrlf=false","apply","--check",str(patch),cwd=root)
            git("-c","core.autocrlf=false","apply",str(patch),cwd=root)
            if expected:
                check(matches(root,expected), path + " byte-exact replay")
        adapted = final | {"src/BoltSharp/Transport/BoltTcpServer.cs":data["wsL2_listener_source_sha256"]}
        check(matches(root, adapted), "Adapted WSL2 matches recorded gate files")
        check(list((root/"src/BoltSharp").rglob("*.cs")) and
              {p.relative_to(root).as_posix() for p in (root/"src/BoltSharp").rglob("*.cs")}
              == set(pristine), "Replay left exactly the intended source inventory")
    return {"source_commit":CHAIN[3],"source_tree":git("rev-parse",CHAIN[3]+"^{tree}").decode().strip(),
            "patch_blob_hashes":hashes,"prototype_files":len(original),"refined_files":len(pristine),
            "windows_variant_matches":True,"wsl2_variant_matches":True}


def run_counts():
    location=f"docs/reviews/evidence/{NAMES[2]}/execution-records.zip"
    tests={"win-baseline-core-authorized.trx":1227,"win-candidate-core-final.trx":1227,
           "win-baseline-driver-authorized.trx":41,"win-candidate-driver-final.trx":43,
           "win-baseline-matrix-final.trx":34,"win-candidate-matrix-final.trx":34}
    results={}
    with zipfile.ZipFile(BytesIO(blob(CHAIN[2],location))) as z:
        check(z.testzip() is None, "INV-02 execution CRC")
        for name, expected in tests.items():
            node=ET.fromstring(z.read(name)).find(".//{*}Counters")
            check(node is not None and int(node.attrib["passed"])==int(node.attrib["total"])==expected
                  and int(node.attrib["failed"])==0,name+" campaign counters")
            results[name]=expected
    return results


def main():
    for commit, parent in zip(CHAIN,CHAIN[1:]):
        check(git("show","-s","--format=%P",commit).decode().split()==[parent],"Exact sole-parent ancestry")
    check(not git("diff","--name-only",CHAIN[4],PARENT,"--","src","tests","docs/decisions"),
          "Protected source/tests/decisions unchanged since TS1 owner closure")
    decisions = {}
    for path in git("ls-tree","-r","--name-only",CHAIN[4],"--","docs/decisions").decode().splitlines():
        oid = git("rev-parse",f"{PARENT}:{path}").decode().strip()
        check(oid==git("rev-parse",f"{CHAIN[4]}:{path}").decode().strip(),path+" accepted decision")
        decisions[path]=oid
    output = {
        "entry_commit":PARENT, "entry_tree":git("rev-parse",PARENT+"^{tree}").decode().strip(),
        "ancestry_newest_first":CHAIN, "prior_packages":package_evidence(),
        "prior_decision_blobs_preserved":decisions, "tls_campaign":gate_evidence(),
        "isolated_patch_replay":patch_replay(), "inv02_windows_result_counts":run_counts(),
    }
    print(json.dumps(output,indent=2))


if __name__ == "__main__":
    main()
