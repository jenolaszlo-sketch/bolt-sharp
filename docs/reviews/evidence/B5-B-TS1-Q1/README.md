# B5-B-TS1-Q1 — Independent Qualification Evidence

This directory is the evidence package for the independent qualification of `B5-B-TS1`, the
test-only remediation of `B5-B-F01-R1-OWNER-F01` (terminal FAILURE delivery race) and
`B5-B-F01-R1-OWNER-F02` (multi-client counter observation race).

- **Qualified producer HEAD:** `d8c28a9bed0af6305936b2b6e427ca1365bed757` (tree `9e7f07ca2664e44230b10b4b92c9f435fd58fdcd`)
- **Implementation commit:** `2e0b9da0b8920a2f6cdd7de9fa968891d779090e` (tree `3fbdbebf21aacd545fd3384c289c649e8c7ab75b`)
- **Base commit:** `17886a5e7f709ef1e01624e49c9d90b50808566c` (tree `e7d3be41483ab7aa22b51b27c619d7f211d8520b`)
- **Qualification disposition:** `SUPPORTED WITH QUALIFICATIONS`
- **Qualification report:** `docs/reviews/B5-B-TS1-Q1-qualification.md`

The qualification commit is the single docs-only child of `d8c28a9`. This package makes no
production, canonical-test, owner-decision or historical-evidence changes.

## Environment

| Item | Value |
| --- | --- |
| OS | Windows 11, version 10.0.26200, 64-bit |
| .NET SDK | 10.0.401 |
| `Microsoft.NETCore.App` | 8.0.31 |
| Git | 2.55.0.windows.3 |
| PowerShell | 5.1.26100.9711 |
| Configuration | Debug, `--no-build`, `--nodeReuse:false` |
| `core.autocrlf` | `true` |
| Logical CPUs | 18 |

Full toolchain output: `02-environment.txt`.

## Evidence inventory

| File | Content |
| --- | --- |
| `01-preflight.txt` | Starting identity, ancestry, clean state, one worktree, no stashes, 3 ahead, exact TS1 diff, protected-scope check, owner-decision identity. |
| `02-environment.txt` | OS, SDK/runtime, Git, PowerShell, checkout configuration. |
| `03-builds-and-format.txt` | Fresh production build, solution build, scoped formatting, warning inventory. |
| `04-campaign-A-terminal-refusal.tsv` / `-raw.log` | Campaign A: 100 independent processes, 10 terminal-refusal tests each. |
| `05-campaign-B-multi-client.tsv` / `-raw.log` | Campaign B: 100 independent processes, 6 multi-client tests each. |
| `06-campaign-C-full-core.tsv` / `-raw.log` | Campaign C: 30 independent full core-suite processes at 1,227 tests. |
| `07-campaign-D-official-driver.tsv` / `-raw.log` | Campaign D: 3 independent official-driver processes at 41 tests. |
| `08-regressions.tsv` / `-raw.log` | Transport, Backend, Sessions, Messages and B0-B integration filters. |
| `09-baseline-reproduction.tsv` / `-raw.log` | Isolated `17886a5e` archive: old pipelined pair 5/10 failed; old multi-client test 20/20 isolated. |
| `10-mutations-summary.json` / `10-mutations-raw.log` | Bounded negative controls M0–M6 in an isolated HEAD copy. |
| `11-integrity-verification.txt` / `11-integrity-detail.json` | Canonical 619/619, excluded 36/36, TS1 12 entries, owner decisions 6/6, zero historical references. |
| `12-portability-analysis.txt` | Per-entry blob/LF/CRLF/worktree comparison and reproducibility by checkout form. |
| `13-test-inventory.txt` | Exact 10 terminal-refusal and 6 multi-client test names. |
| `Verify-Evidence.ps1` | Self-verifier for this package (Git blob bytes at the qualification commit). |
| `manifest.json` | Inventory of the report and every evidence file except `manifest.json` itself. |

## Exact campaign commands

All campaigns used fresh `dotnet test` processes against the already-built HEAD tree. Failed
iterations were preserved; none occurred.

```text
A: dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false --filter "FullyQualifiedName~UnsupportedCommandRefusalTests"      (100 runs)
B: dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false --filter "FullyQualifiedName~MultiClientTests"                    (100 runs)
C: dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false                                                                    (30 runs)
D: dotnet test tests\BoltSharp.DriverInteropTests\BoltSharp.DriverInteropTests.csproj -c Debug --no-build --nodeReuse:false                                          (3 runs)

Transport: dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false --filter "FullyQualifiedName~BoltSharp.Tests.Transport"
Backend:   dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false --filter "FullyQualifiedName~BoltSharp.Tests.Backend"
Sessions:  dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false --filter "FullyQualifiedName~BoltSharp.Tests.Sessions"
Messages:  dotnet test tests\BoltSharp.Tests\BoltSharp.Tests.csproj -c Debug --no-build --nodeReuse:false --filter "FullyQualifiedName~BoltSharp.Tests.Messages"
B0-B:      dotnet test tests\BoltSharp.IntegrationTests\BoltSharp.IntegrationTests.csproj -c Debug --no-build --nodeReuse:false
```

Campaign D's original TSV header was missing a tab between `exit` and `elapsed_ms`. The header in
`07-campaign-D-official-driver.tsv` is corrected; the three run rows and the raw log are
unmodified. No run was discarded or restarted.

## Baseline reproduction

The baseline was extracted from `git archive 17886a5e` into an isolated temporary directory and
built there. The canonical repository was never modified.

- Old pipelined pair (`PipelinedUnsupportedCommand` + `PipelinedMalformedMessage`): 10 runs,
  5 failed, 5 passed. Every failure was `PipelinedUnsupportedCommand`, before any response, with
  `IOException` + inner `SocketException` ("forcibly closed by the remote host" — the
  characterized RST). The producer recorded 7/10; the difference is environment timing, not class.
- Old `FailureInOneClient_IsolatedFromOthers`: 20 runs, 20 passed in isolation. The owner's
  full-suite observation was 1 failure in 33 runs, consistent with a load-sensitive test race.

## Negative controls

Six source mutations were applied one at a time in an isolated `git archive` copy of the qualified
HEAD, never to the canonical tree. Each mutation was built and exercised with the standalone byte
oracle and the two pipelined tests.

| Mutation | Standalone detected | Pipelined detected | Interpretation |
| --- | --- | --- | --- |
| M0 unmodified control | 0/1 (pass) | 0/2 (pass) | Control is green. |
| M1 missing terminal write, event still reported | 1/1 | 0/10 | Pipelined oracle is blind under RST; standalone catches. |
| M2 wrong FAILURE content | 1/1 | 8/10 | Two RST-suppressed runs accepted; standalone catches. |
| M3 extra fabricated response | 1/1 | 5/5 | Both catch. |
| M4 wrong terminal state (`Failed`, still closes) | 0/1 (pass) | 5/5 | Pipelined state assertion catches; standalone does not assert state. |
| M5 unrelated transport fault after write | 0/1 (pass) | 1/1 | Pipelined classifier rejects the fault; standalone does not exercise post-write faults. |
| M6 no response / no terminal event | 1/1 | 1/1 | Both catch; pipelined times out with diagnostic counters. |

The combined suite detects every injected defect. The pipelined-only blind spots for M1/M2 and the
standalone-only blind spots for M4/M5 are complementary, not additive weaknesses. The M1/M2
blindness is the owner-authorized abortive-close boundary (`Q-B5B-09`), covered deterministically
by the standalone byte oracle.

Method limitations: the mutations are source-level, the copies are isolated archives, and RST
timing is environment-dependent. M2's 8/10 and M1's 0/10 are timing observations, not guarantees;
the deterministic result is the standalone oracle's 1/1 detection. No canonical production or test
file was modified.

## Hashing semantics

**The Q1 manifest uses SHA-256 over Git blob bytes, not platform-dependent checkout bytes.**

- Resolve an entry with `git cat-file blob <qualification-commit>:<path>`; hash the returned bytes.
- `docs/reviews/evidence/B5-B-TS1-Q1/.gitattributes` contains `* -text`, so every evidence file is
  stored and checked out byte-exactly; no CRLF conversion applies to this directory.
- The report is written as LF and is also hashed from its Git blob, never from a worktree form.
- `manifest.json` is excluded from its own inventory (a file cannot contain its own hash).
- `Verify-Evidence.ps1` implements this convention and also checks package completeness and the
  qualified-parent invariant.

The producer's B5-B-TS1 manifest uses a different convention (exact working-tree bytes). Its
`TransportTestSupport.cs` entry is CRLF (20,066 bytes) while the Git blob is LF (19,563 bytes), so
the producer manifest is reproducible only from the producer's mixed worktree. See
`12-portability-analysis.txt` and finding `B5-B-TS1-Q1-F01`. The producer's canonical 619/619
historical verification is likewise a raw-worktree check in this Windows `core.autocrlf=true`
checkout; 306 of the 619 entries are CRLF-only worktree forms. That is pre-existing historical
evidence behavior, not changed by TS1.

## Scope and preserved qualifications

This is qualification evidence only. It changes no production source, canonical test, helper,
project, package, owner decision or historical evidence. It does not issue owner acceptance and
does not discharge any production qualification.

- `Q-B5B-03` remains **closed** by the F01-R1 owner decision; TS1 does not reopen it.
- `Q-B5B-04` (generic `50N42` classification) remains **open**.
- `Q-B5B-05` (same-connection RESET recovery after IGNORED) remains **open**.
- `Q-B5B-09` (release-grade abortive-close / client-visible delivery hardening) remains **open**.
- All other inherited qualifications remain unchanged.

## Verification

From the repository root at the qualification commit:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File docs/reviews/evidence/B5-B-TS1-Q1/Verify-Evidence.ps1
```

The verifier reads `manifest.json`, fetches each entry with `git cat-file blob HEAD:<path>`,
computes SHA-256, reports every mismatch, and checks that every evidence file except
`manifest.json` is listed. It exits `0` only if all entries match and the inventory is complete.
