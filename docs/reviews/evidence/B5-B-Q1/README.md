# B5-B-Q1 — independent reviewer evidence

Produced by the B5-B-Q1 independent reviewer (separate OpenCode session, model
`nvidia/z-ai/glm-5.3-flash`), starting from HEAD `646c572912de9a2ca415ec3548a99159cb2f4905`
(tree `7670702e59933d84a685d09a0e44744a2dce34ec`), without modifying any production or producer
artifact.

## Contents

- `Verify-AllManifests-Independent.ps1` — reviewer-written SHA-256 re-verification of the producer
  B5-B manifest (62/62), all thirteen historical manifests (380/380) and the byte-identity of the
  four accepted owner decisions. Outputs `01-manifest-verification.txt` and
  `01-manifest-verification-detail.json`.
- `Run-Q1Regression.ps1` — reviewer regression reproduction: builds plus every accepted suite.
  Outputs `02-…txt` through `13-regression-complete.txt` and the two TRX files
  (`b5bq1-producer-suite.trx`, `b5bq1-full-regression.trx`).
- `q1*.json` — per-probe evidence documents written by the reviewer probe project
  (`docs/reviews/probes/B5-B-Q1/`) during its runs. Each document is sanitized: LOGON payloads are
  redacted at the raw-chunk level by the reviewer tap before storage, and the writer refuses any
  document containing a credential sentinel in plain, hex or UTF-16-hex form. A final sentinel scan
  over all files (reviewer and producer credential values) found zero leaks.
- `b5bq1-probes.trx` — TRX result of the reviewer probe suite (53/53 passing).
- `manifest.json` — SHA-256 manifest over the exact working-tree bytes of every file in this
  directory except this manifest itself.
- `.gitattributes` — `* -text` (raw byte preservation, repository convention for evidence).

## Reproduction

```powershell
powershell -ExecutionPolicy Bypass -File docs/reviews/evidence/B5-B-Q1/Verify-AllManifests-Independent.ps1
powershell -ExecutionPolicy Bypass -File docs/reviews/evidence/B5-B-Q1/Run-Q1Regression.ps1
$env:BOLTSHARP_B5BQ1_EVIDENCE_DIR = "<this directory>"
dotnet test docs/reviews/probes/B5-B-Q1/BoltSharp.B5BQ1.Probes.csproj -c Debug
```

The probes start the real accepted `BoltTcpServer` on loopback ephemeral ports, drive the pinned
official `Neo4j.Driver 6.4.0` through its documented public API, and record driver-visible results,
server diagnostics, per-connection provider counters and sanitized wire transcripts.
