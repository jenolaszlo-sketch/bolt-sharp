# B5B-ARTIFACT-SECURITY-01 — Post-Execution Artifact Security Gate Implementation Report

**Date:** 2026-11-26  
**Implementation Commit:** `d503cd6b0973ceaa1b8d1df3545ee6506a61ba55`  
**Hosted CI Run:** `45804439446` (success)

---

## Security Gap Addressed

The Q-B5B-09 TLS Native-Linux qualification campaign generates ephemeral TLS credentials (private key `.key` and public certificate `.pem`) in the results directory during execution. While these are gitignored, the GitHub Actions artifact upload step uploads the entire results directory, potentially including private key material.

**Gap:** Pre-execution repository scan only validates tracked files; generated artifacts were not scanned before upload.

---

## Implementation

### 1. New Sanitization Tool

**File:** `docs/reviews/Q-B5B-09-TLS-LINUX-QUAL-01-R1/tools/sanitize_artifacts.py`

**Capabilities:**
- Removes `*.key` files by name pattern
- Scans all file content for PEM private key armour (content-based, extension-independent)
- Preserves legitimate public certificates (`.crt`, `.cer`, `.pem`, `.pub`, `.p12`, `.pfx`, `.csr`)
- Two modes: `sanitize + scan` (removes then validates) and `--scan-only` (validates only)
- Fails closed (exit code 1) if private key material detected

### 2. Workflow Integration

**File:** `.github/workflows/q-b5b-09-tls-linux-qual-01.yml`

Added post-execution steps (run with `if: always()` to catch failed/cancelled campaigns):
1. **Sanitize campaign artifacts** — removes private keys from results directory
2. **Validate artifact security** — scans results directory, fails workflow if private keys remain
3. **Append campaign summary** — existing step
4. **Upload campaign artifacts** — only runs if security gate passes (implicit via `if: always()` ordering)

**File:** `.github/workflows/ci.yml`

Added standard CI validation step:
- Scans both qualification evidence directories (`Q-B5B-09-TLS-LINUX-QUAL-01/results` and `Q-B5B-09-TLS-LINUX-QUAL-01-R1/results`)

---

## Executed Controls

| Control | Description | Result |
|---------|-------------|--------|
| **Generated private key removal** | Creates `.key` file, runs sanitize mode, verifies removal | **PASS** |
| **Embedded private key detection** | Creates file with embedded armour, runs scan-only, detects and fails | **PASS** |
| **Public certificate preservation** | Creates `.pem` with certificate, runs sanitize, file preserved | **PASS** |
| **Unexpected extension detection** | Private key armour in `.txt` file detected and removed | **PASS** |
| **Campaign failure bypass prevention** | Steps use `if: always()`, run even on campaign failure | **VERIFIED** (workflow structure) |
| **Artifact upload requires validation** | Upload step follows validation; validation failure stops workflow | **VERIFIED** (workflow structure) |
| **Secret scanner controls** | Built-in positive/negative regression controls | **PASS** |
| **Repository scan** | Full repo scan for committed private keys | **PASS** |

---

## Limitations of PEM Armour Detection

The detection mechanism scans for unambiguous PEM private key armour headers at line start:

```
-----BEGIN PRIVATE KEY-----
-----BEGIN RSA PRIVATE KEY-----
-----BEGIN EC PRIVATE KEY-----
-----BEGIN OPENSSH PRIVATE KEY-----
-----BEGIN ENCRYPTED PRIVATE KEY-----
```

**Known limitations:**
1. **Encoding dependence** — Only detects UTF-8/ASCII encoded armour. UTF-16 or other encodings with BOM will not match (mitigated: OpenSSL and standard tools emit UTF-8).
2. **Non-PEM formats** — Does not detect DER, PKCS#12, or other binary key formats (mitigated: campaign uses OpenSSL PEM output).
3. **Obfuscated armour** — Armour split across lines, base64-encoded, or otherwise transformed will not be detected (mitigated: not a realistic threat model for generated credentials).
4. **Prose mentions** — Prose that merely mentions the header text (not at line start) is not flagged (by design, per negative control #3).

These limitations are documented and accepted for the bounded campaign scope.

---

## Verification Evidence

**Hosted CI Run:** `45804439446`  
**Commit:** `d503cd6b0973ceaa1b8d1df3545ee6506a61ba55`  
**Status:** Success (1m 8s)  
**Steps verified:** Build, Test, Secret Scanner, Artifact Security Scan

**Local validation:**
- Full test suite: 1271/1271 passed
- Transport suite: 134/134 passed
- Format check: PASS
- dotnet pack: PASS
- Secret scanner: PASS
- Artifact security scan: PASS

---

## Remaining Obligations

- [ ] Independent qualification of this artifact security implementation (separate commission)
- [ ] Owner acceptance of artifact security gate (separate gate)
- [ ] Native-Linux campaign execution with new gate (manual dispatch)

---

**Implemented by:** jenol <jenolaszlo-sketch@users.noreply.github.com>