#!/usr/bin/env python3
"""Detect private key material committed to the repository.

Detection is CONTENT-based and independent of file extension: a file is flagged
when its bytes contain an unambiguous PEM private-key armour header. This is
deliberately narrow - it never flags a file merely for being named *.key, *.pem
or *.crt, so legitimate source files (for example a public certificate, a
key-reference in documentation, or a test fixture that only *mentions* a header
inside prose) are not swept up silently.

Exit status: 0 when no private key armour is found, 1 when at least one is.

Run with --selftest to execute the built-in positive and negative regression
controls for this prevention mechanism.
"""
import re
import sys
import tempfile
from pathlib import Path

# Armour headers that unambiguously begin private key material. Matched at the
# start of a line so prose that merely quotes a header is distinguishable by the
# --include-prose flag (default off).
ARMOR = (
    re.compile(rb'(?m)^-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED |)PRIVATE KEY-----'),
    re.compile(rb'(?m)^-----BEGIN PRIVATE KEY-----'),
)

# Extensions that can never contain private key material but are often confused
# with it; used only to explain negative results in --selftest, not to filter.
PUBLIC_SUFFIXES = {'.crt', '.cer', '.pem', '.pub', '.p12', '.pfx'}


def has_private_key(data: bytes) -> bool:
    return any(p.search(data) for p in ARMOR)


def scan(root: Path):
    """Return the list of tracked files under root containing private key armour."""
    if not (root / '.git').exists():
        raise SystemExit(f'not a git worktree: {root}')
    import subprocess
    tracked = subprocess.check_output(
        ['git', 'ls-files', '-z'], cwd=str(root)).decode('utf-8', 'replace').split('\0')
    hits = []
    for rel in tracked:
        if not rel:
            continue
        p = root / rel
        if not p.is_file():
            continue
        try:
            data = p.read_bytes()
        except OSError:
            continue
        if has_private_key(data):
            hits.append(rel)
    return hits


def selftest() -> int:
    ok = True

    def check(name, cond):
        nonlocal ok
        print(f'  {"PASS" if cond else "FAIL"}: {name}')
        ok = ok and cond

    print('SECRET-SCANNER REGRESSION CONTROLS')

    # --- positive controls: real armour must be detected ---
    good = [
        b'-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSk\n-----END PRIVATE KEY-----',
        b'-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA\n-----END RSA PRIVATE KEY-----',
        b'-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEI\n-----END EC PRIVATE KEY-----',
        b'-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAA\n-----END OPENSSH PRIVATE KEY-----',
        b'-----BEGIN ENCRYPTED PRIVATE KEY-----\nMIIFHDBOBgkqhkiG9w0BBQ0w\n-----END ENCRYPTED PRIVATE KEY-----',
    ]
    for i, data in enumerate(good, 1):
        check(f'positive #{i}: armour detected in isolation', has_private_key(data))

    # embedded armour inside a larger file must also be detected
    embedded = b'# evidence capture\nlog = "..."\n' + good[0] + b'\nmore text\n'
    check('positive #6: armour detected embedded in a larger file',
          has_private_key(embedded))

    # --- negative controls: legitimate material must NOT be flagged ---
    cert = (b'-----BEGIN CERTIFICATE-----\n'
            b'MIIDCTCCAfGgAwIBAgIUJsOtDmT0Ch4EIlJp\n'
            b'-----END CERTIFICATE-----\n')
    check('negative #1: public certificate not flagged', not has_private_key(cert))
    pub = b'ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQ user@host\n'
    check('negative #2: public SSH key not flagged', not has_private_key(pub))
    prose = (b'# Incident note\n# The credential armour begins with "BEGIN PRIVATE KEY"\n'
             b'# and must never be committed. See the redaction record.\n')
    check('negative #3: prose that merely mentions the header not flagged',
          not has_private_key(prose))
    empty = b''
    check('negative #4: empty file not flagged', not has_private_key(empty))
    # a filename alone is never evidence
    check('negative #5: has_private_key is content-based, never name-based',
          not has_private_key(b'cert.key\n'))

    # --- end-to-end: build a throwaway worktree fixture and scan it ---
    print('  end-to-end scan fixture:')
    tmp = Path(tempfile.mkdtemp(prefix='secret-scan-'))
    (tmp / 'cert').mkdir()
    (tmp / 'cert' / 'server.key').write_bytes(good[0])          # must be found
    (tmp / 'cert' / 'server.crt').write_bytes(cert)             # must be ignored
    (tmp / 'notes.md').write_bytes(prose)                       # must be ignored
    found = []
    for p in sorted(tmp.rglob('*')):
        if p.is_file() and has_private_key(p.read_bytes()):
            found.append(p.relative_to(tmp).as_posix())
    check('e2e: only the real key is reported', found == ['cert/server.key'])
    import shutil
    shutil.rmtree(tmp, ignore_errors=True)

    print('ALL SECRET-SCANNER CONTROLS PASSED' if ok else 'SOME CONTROLS FAILED')
    return 0 if ok else 1


def main(argv):
    if '--selftest' in argv:
        return selftest()
    hits = scan(Path.cwd())
    for h in hits:
        print(f'PRIVATE KEY MATERIAL: {h}')
    if hits:
        print(f'\n{len(hits)} file(s) contain private key armour.')
        print('Remove them from history and from the working tree before publication.')
        return 1
    print('No private key material detected.')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))