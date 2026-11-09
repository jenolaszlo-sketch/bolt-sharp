# B5-B-IMPL-01 Evidence Directory

Producer evidence for **B5-B-IMPL-01 — official Neo4j .NET driver interoperability**
(`docs/reviews/B5-B-IMPL-01.md` is the narrative report).

## Contents

| Path | Purpose |
| --- | --- |
| `Run-B5BImpl.ps1` | Reproduction driver: builds, runs the new 40-test interoperability suite with JSON evidence generation, every regression suite, historical manifest verification and the evidence manifest. |
| `Verify-HistoricalManifests.ps1` | Raw-byte SHA-256 verification of all 11 historical evidence manifests (352 entries) plus the B5-A producer (18) and B5-A-Q1 (10) packages, and byte-identity of the four accepted owner decisions. |
| `New-EvidenceManifest.ps1` | SHA-256 inventory of every file in this directory (raw working-tree bytes). |
| `b5b2-*.json` … `b5b10-*.json` | One sanitized JSON document per interoperability scenario: driver package/assembly identity, URI scheme, handshake observations, per-connection wire transcripts (LOGON redacted at message and chunk level), driver-visible results, exception classifications and server-side counters/events. |
| `00-environment.json` | OS, frameworks, driver package/assembly identity, BoltSharp assembly version, git HEAD/tree at capture time. |
| `compatibility-matrix.json` | Producer capability matrix with per-row status and evidence references. |
| `b5b-interop-suite.trx` | TRX result of the complete new interoperability suite (40 tests, all passing). |
| `full-regression.trx` | TRX result of the complete core test assembly (1,217 tests, all passing). |
| `historical-integrity.json` | Structured result of the historical manifest and owner-decision verification. |
| `manifest.json` | SHA-256 inventory of every file in this evidence directory. |
| `logs/01-preflight.txt` | Starting HEAD/tree, branch, worktree count, cleanliness, push status, toolchain. |
| `logs/02-build-production.txt` | Production assembly build: 0 errors, 0 warnings. |
| `logs/03-build-solution.txt` | Solution build: 0 errors, 2 warnings (the two pre-existing `xUnit2012` advisories). |
| `logs/04-b5b-interop-suite.txt` | New official-driver interoperability suite: 40/40 passing. |
| `logs/05-transport-suite.txt` | B5-A transport suite: 80/80 passing. |
| `logs/06-b3c-backend.txt` | B3-C reference backend: 94/94 passing. |
| `logs/07-b3b-sessions.txt` | B3-B session engine: 119/119 passing. |
| `logs/08-b3a-messages.txt` | B3-A message codec: 98/98 passing. |
| `logs/09-full-regression.txt` | Full core regression: 1,217/1,217 passing. |
| `logs/10-driver-integration-b0b.txt` | Historical B0-B scripted-server integration suite: 19/19 passing (unchanged). |
| `logs/11-historical-manifests.txt` | Historical/B5-A manifest verification and owner-decision byte identity. |
| `logs/12-format-check.txt` | CI-equivalent scoped `dotnet format --verify-no-changes` for the new test project (exit 0). |

## Reproduction

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/reviews/evidence/B5-B-IMPL-01/Run-B5BImpl.ps1
```

The run is deterministic apart from ephemeral ports and in-memory certificate material. The
scenario documents are written by the test suite itself when `BOLTSHARP_B5B_EVIDENCE_DIR` points
at this directory (the reproduction driver sets it).

## Confidentiality

All credentials are deterministic synthetic test values. The evidence writer refuses to write any
document containing the credential sentinel in plain or hex-encoded form; client-to-server chunks
overlapping a LOGON payload are fully redacted. TLS certificates (including the test CA and the
CA-signed leaf) are generated in memory per test and never written to disk; no machine trust
store is modified. No production secret, private key or PFX appears anywhere in this directory.

## Hash convention

`manifest.json` records the SHA-256 of each evidence file's exact working-tree bytes (the
repository's canonical checkout form). The historical manifest verifier compares raw bytes the
same way, which is why all historical manifests remain valid without rewriting.
