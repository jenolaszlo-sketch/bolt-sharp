"""Diagnose the line-ending state of the two preserved Q2 packages."""

import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[5]
PACKAGE = "docs/reviews/evidence/Q-B5B-09-PLAIN-Q2"
A = "96a1a3cd0f059b554c6e7ab2ce099ef29b161ace"
B = "3c2144a0d655f1e0b7187ca0bf8ac6de1bef8e8f"


def git(*args):
    proc = subprocess.run(["git", *args], cwd=REPO, capture_output=True)
    if proc.returncode != 0:
        raise RuntimeError(proc.stderr.decode())
    return proc.stdout


def blob(rev, path):
    proc = subprocess.run(["git", "cat-file", "blob", "%s:%s" % (rev, path)],
                          cwd=REPO, capture_output=True)
    return proc.stdout if proc.returncode == 0 else None


for label, rev in (("session A", A), ("session B", B)):
    files = [line for line in git("ls-tree", "-r", "--name-only", rev, "--", PACKAGE)
             .decode().splitlines() if line]
    with_crlf = []
    for path in files:
        data = blob(rev, path) or b""
        if b"\r\n" in data:
            with_crlf.append(path.replace(PACKAGE + "/", ""))
    print("%s (%s): %d files, %d contain CRLF" % (label, rev[:7], len(files), len(with_crlf)))
    for path in with_crlf:
        print("   CRLF: %s" % path)
    print()
