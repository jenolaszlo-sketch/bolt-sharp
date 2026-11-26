#!/usr/bin/env python3
"""Sanitize and validate qualification evidence artifacts.

This script implements the post-execution security gate:
1. Removes generated private key material from the artifact staging directory
2. Scans the directory for any remaining private key material
3. Fails closed if private key material is detected
4. Preserves legitimate public certificates and non-sensitive evidence

Run AFTER campaign execution and BEFORE artifact upload.
"""
import re
import sys
from pathlib import Path

# Armour headers that unambiguously begin private key material.
ARMOR = (
    re.compile(rb'(?m)^-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED |)PRIVATE KEY-----'),
    re.compile(rb'(?m)^-----BEGIN PRIVATE KEY-----'),
)

# Files that are safe to keep even if they contain key-like patterns
# (e.g., public certificates, CSRs, documentation)
SAFE_SUFFIXES = {'.crt', '.cer', '.pem', '.pub', '.p12', '.pfx', '.csr'}


def has_private_key(data: bytes) -> bool:
    """Check if data contains unambiguous private key armour."""
    return any(p.search(data) for p in ARMOR)


def sanitize_directory(results_dir: Path) -> tuple[int, int]:
    """Remove private key files from the results directory.

    Returns: (removed_count, scanned_count)
    """
    if not results_dir.exists():
        print(f"Results directory does not exist: {results_dir}")
        return 0, 0

    removed = 0
    scanned = 0

    for file_path in results_dir.rglob('*'):
        if not file_path.is_file():
            continue

        scanned += 1

        # Check if this is a private key file by name pattern
        if file_path.suffix == '.key':
            print(f"Removing private key file: {file_path.relative_to(results_dir)}")
            file_path.unlink()
            removed += 1
            continue

        # Also scan content for embedded private key material
        try:
            data = file_path.read_bytes()
        except OSError:
            continue

        if has_private_key(data):
            # If it's a safe suffix (public cert, etc.), don't remove - just warn
            if file_path.suffix.lower() in SAFE_SUFFIXES:
                print(f"WARNING: Private key armour detected in safe-suffix file: {file_path.relative_to(results_dir)}")
                # But we still fail the scan - this shouldn't happen
            else:
                print(f"Removing file with embedded private key material: {file_path.relative_to(results_dir)}")
                file_path.unlink()
                removed += 1

    return removed, scanned


def scan_directory(results_dir: Path) -> list[Path]:
    """Scan the results directory for private key material.

    Returns list of files containing private key armour.
    """
    hits = []
    if not results_dir.exists():
        return hits

    for file_path in results_dir.rglob('*'):
        if not file_path.is_file():
            continue
        try:
            data = file_path.read_bytes()
        except OSError:
            continue
        if has_private_key(data):
            hits.append(file_path)

    return hits


def main(argv):
    if len(argv) < 1:
        print("Usage: sanitize_artifacts.py <results_dir> [--scan-only]")
        return 1

    results_dir = Path(argv[0])
    scan_only = '--scan-only' in argv

    print(f"Artifact security gate: {'scan' if scan_only else 'sanitize + scan'} -> {results_dir}")

    if not scan_only:
        removed, scanned = sanitize_directory(results_dir)
        print(f"Sanitization: {removed} file(s) removed, {scanned} file(s) scanned")

    hits = scan_directory(results_dir)
    if hits:
        print(f"\nSECURITY GATE FAILED: {len(hits)} file(s) contain private key material:")
        for h in hits:
            print(f"  {h.relative_to(results_dir)}")
        print("\nArtifact upload blocked. Remove private key material before publication.")
        return 1

    print("SECURITY GATE PASSED: No private key material detected in artifacts.")
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))