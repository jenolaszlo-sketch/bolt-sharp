"""Run from repository root; read-only checks of all historical tracked files."""
import hashlib
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ENTRY = "6f685487707ab86598415723ad4175ce1893d2b3"
IMPLEMENTATION = "94aebcaa80a387608b710f2de7001e143fb9c096"
Q1 = "4dbd75a86df8a0f691665bc05d72391dc218ccd1"
BASELINE = "500f9fc874e6afada2fdc632cd5e17350f877557"

def git(*args):
    return subprocess.check_output(["git", *args]).decode("utf-8").strip()

def blob(ref, path):
    return subprocess.check_output(["git", "show", f"{ref}:{path}"])

def without_xml(data):
    return b"\n".join(line for line in data.splitlines() if not line.lstrip().startswith(b"///"))

def trx_summary(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = root.find("t:ResultSummary/t:Counters", ns).attrib
    groups = {"B1": 0, "B2-A producer": 0, "B2-A-Q1": 0, "B2-A-Q2": 0}
    definitions = {t.attrib["id"]: t.find("t:TestMethod", ns).attrib["className"]
                   for t in root.findall("t:TestDefinitions/t:UnitTest", ns)}
    for result in root.findall("t:Results/t:UnitTestResult", ns):
        assert result.attrib["outcome"] == "Passed", result.attrib
        name = definitions[result.attrib["testId"]]
        group = ("B2-A-Q2" if name.startswith("B2AQ2.") else
                 "B2-A-Q1" if "B2Q1FramingProbes" in name else
                 "B2-A producer" if "Tests.Framing." in name else "B1")
        groups[group] += 1
    return {"counters": counters, "groups": groups}

assert git("branch", "--show-current") == "main"
assert git("rev-parse", "HEAD") == ENTRY or git("rev-parse", "HEAD^") == ENTRY
identities = {}
for label, commit in [("B1/B2 scope baseline", BASELINE), ("B2-A implementation", IMPLEMENTATION),
                      ("Q1 qualification", Q1), ("R1 / reviewed entry", ENTRY)]:
    identities[label] = {"commit": commit, "tree": git("rev-parse", f"{commit}^{{tree}}"),
                         "parents": git("show", "-s", "--format=%P", commit)}
assert git("rev-parse", f"{ENTRY}^") == Q1
assert git("rev-parse", f"{Q1}^") == IMPLEMENTATION
assert git("rev-parse", f"{IMPLEMENTATION}^") == BASELINE
r1_files = git("diff", "--name-only", Q1, ENTRY).splitlines()
expected_sources = ["src/BoltSharp/Framing/" + name for name in
                    ["BoltFramingLimits.cs", "BoltMessageReader.cs", "BoltMessageWriter.cs"]]
assert sorted(r1_files) == sorted(expected_sources + ["docs/reviews/B2-A-R1-qualification-closure.md"])
for path in expected_sources:
    assert without_xml(blob(Q1, path)) == without_xml(blob(ENTRY, path)), path
assert not git("diff", "--name-only", Q1, ENTRY, "--", "tests")

# Git's clean filter normalizes CRLF when comparing the working tree with committed blobs.
preserved = []
for path in git("ls-tree", "-r", "--name-only", ENTRY).splitlines():
    expected = git("rev-parse", f"{ENTRY}:{path}")
    actual = git("hash-object", "--path=" + path, path)
    assert actual == expected, f"Historical file changed: {path}"
    preserved.append({"path": path, "git_blob": actual})

root = Path("docs/reviews/evidence/B2-A-Q2")
baseline = trx_summary(root / "baseline-results/baseline.trx")
reviewer = trx_summary(root / "reviewer-results/reviewer.trx")
assert baseline["groups"] == {"B1": 509, "B2-A producer": 54, "B2-A-Q1": 41, "B2-A-Q2": 0}
assert reviewer["groups"] == {"B1": 0, "B2-A producer": 0, "B2-A-Q1": 0, "B2-A-Q2": 18}
evidence_hashes = {str(p.relative_to(root)).replace("\\", "/"): hashlib.sha256(p.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
                   for p in sorted(root.rglob("*")) if p.is_file() and p.name != "provenance.json"}
result = {"branch": "main", "identities": identities,
          "r1_changed_files": r1_files, "r1_non_xml_source_identical": True,
          "r1_tests_identical": True, "entry_src_tree": git("rev-parse", f"{ENTRY}:src"),
          "entry_tests_tree": git("rev-parse", f"{ENTRY}:tests"),
          "baseline": baseline, "reviewer": reviewer,
          "historical_files_preserved": preserved,
          "sha256_normalization": "Text bytes with CRLF normalized to LF, stable across Git checkouts",
          "evidence_sha256": evidence_hashes,
          "reviewer_source_sha256": {
              p.name: hashlib.sha256(p.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
              for p in sorted(Path("docs/reviews/probes/B2-A-Q2").glob("*")) if p.is_file()}}
(root / "provenance.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"identities": identities, "historical_files_preserved": len(preserved),
                  "R1": "XML-only source; tests identical", "baseline": baseline,
                  "reviewer": reviewer}, indent=2))
