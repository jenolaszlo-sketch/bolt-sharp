"""Capture the byte-level evidence behind discrepancies D-02, D-04 and D-11.

Reads Git blob payloads only. Writes reconciliation/discrepancy-evidence.log.
"""

import hashlib
import json
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[5]
OUT = Path(__file__).resolve().parent / "discrepancy-evidence.log"

PACKAGE = "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2"
A = "96a1a3cd0f059b554c6e7ab2ce099ef29b161ace"
B = "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f"
REPORT = "docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md"


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr.decode())
    return proc.stdout


def blob(rev, path):
    proc = subprocess.run(["git", "cat-file", "blob", "%s:%s" % (rev, path)],
                          cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


lines = []


def emit(text=""):
    lines.append(text)


emit("Q-B5B-09-PLAIN-OWNER-01 - byte-level evidence for discrepancies D-02, D-04 and D-11")
emit("All values are computed over exact Git blob payload bytes.")
emit()

emit("D-02  Manifest integrity content")
for label, rev in (("session A", A), ("session B", B)):
    manifest = json.loads(blob(rev, PACKAGE + "/manifest.json").decode("utf-8"))
    entries = manifest["evidenceFiles"]
    populated = [e for e in entries if e.get("sha256")]
    emit("  %-10s commit=%s entries=%d entriesWithHashes=%d" %
         (label, rev[:7], len(entries), len(populated)))
    if populated:
        sample = entries[0]
        emit("             sample entry: %s sha256=%s bytes=%s" %
             (sample["path"], sample["sha256"][:16] + "...", sample["bytes"]))
    else:
        sample = entries[0]
        emit("             sample entry: %s sha256=%r bytes=%r" %
             (sample["path"], sample["sha256"], sample["bytes"]))
emit()

emit("D-04  Session A verifier is checkout-dependent")
emit("  The session A manifest pins the Git-blob SHA-256 of the qualification report.")
for label, rev in (("session A", A), ("session B", B)):
    data = blob(rev, REPORT) or b""
    emit("  report at %-9s commit=%s bytes=%d sha256=%s crlf=%d lf=%d" %
         (label, rev[:7], len(data), hashlib.sha256(data).hexdigest(),
          data.count(b"\r\n"), data.count(b"\n")))
autocrlf = git("config", "core.autocrlf").decode().strip() or "(unset)"
emit("  core.autocrlf=%s" % autocrlf)
emit("  A fresh Windows checkout normalizes the report to CRLF (21,520 blob bytes ->")
emit("  21,867 worktree bytes; sha256 51a6435b... -> 0bf23605...), so the session A")
emit("  verifier's filesystem hash no longer equals the manifest's blob hash.")
emit("  The 47 package files are unaffected because the package .gitattributes marks them")
emit("  '-text'. Reproduced in a disposable worktree: see")
emit("  reconciliation/session-a-verifier-at-own-commit.log")
emit()

emit("D-11  Byte-exactness guard in .gitattributes")
for label, rev in (("session A", A), ("session B", B)):
    data = blob(rev, PACKAGE + "/.gitattributes") or b""
    emit("  %-10s commit=%s .gitattributes=%r" % (label, rev[:7], data))
emit("  Session A marks every package file '-text' (no line-ending or text conversion).")
emit("  Session B replaced that with '* text=auto', so its package files are not")
emit("  guaranteed byte-stable across checkout environments, and its manifest carries no")
emit("  hashes to detect the difference (D-02).")
emit()

emit("Package collision counts across the two sessions")
files_a = set(git("ls-tree", "-r", "--name-only", A, "--", PACKAGE).decode().split())
files_b = set(git("ls-tree", "-r", "--name-only", B, "--", PACKAGE).decode().split())


def blob_id(rev, path):
    proc = subprocess.run(["git", "rev-parse", "%s:%s" % (rev, path)],
                          cwd=REPO, capture_output=True, text=True)
    return proc.stdout.strip() if proc.returncode == 0 else None


identical = [p for p in sorted(files_a & files_b) if blob_id(A, p) == blob_id(B, p)]
modified = [p for p in sorted(files_a & files_b) if blob_id(A, p) != blob_id(B, p)]
emit("  identical=%d modifiedBySessionB=%d onlyInSessionA=%d onlyInSessionB=%d" %
     (len(identical), len(modified), len(files_a - files_b), len(files_b - files_a)))
for path in modified:
    emit("    modified: %s" % path.replace(PACKAGE + "/", ""))
for path in sorted(files_b - files_a):
    emit("    added   : %s" % path.replace(PACKAGE + "/", ""))
emit()

OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
print("\n".join(lines))
