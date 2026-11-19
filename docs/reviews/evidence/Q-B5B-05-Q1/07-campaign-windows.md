# Windows Campaign Record — Q-B5B-05-Q1

Environment: Microsoft Windows NT 10.0.26200.0; .NET SDK 10.0.401; runtime
`Microsoft.NETCore.App 8.0.31`; `wsl --status` reports WSL2 (used for the second platform).
All reviewer executions run in an independent clone of the canonical repository at
`C:\Windows\Temp\opencode\q5b05q1\reviewer` (HEAD `8d762dd…`, tree `50f214a…` — byte-identical
to the canonical tree). The canonical tree itself is used read-only for evidence inspection.

## 1. Builds

```text
dotnet build BoltSharp.slnx -c Release        -> 0 errors, 2 pre-existing xUnit2012 advisories
                                               (BoltRequestDecodeTests.cs lines 71-72; known
                                               from the B5-B-TS1 owner closure)
dotnet build <reviewer harness> -c Release    -> 0 errors, 0 warnings
dotnet build docs/reviews/probes/Q-B5B-05-PROBE-01 (producer probe, clone) -> 0 errors
```

## 2. Reviewer reproduction harness (Q5B05Q1.Reviewer — reviewer-owned)

Raw Bolt 5.8 client, byte-exact expectations, per-connection provider identity, diagnostics sink,
self-signed TLS, recording proxy — none of it derived from the producer probe code.
Command:

```text
dotnet run --project <harness> -c Release -- <evidence-dir>
```

Results — **15/15 scenarios, 364/364 assertions, 0 failures** (per-scenario assertion counts:
R-01 28, R-02 44, R-03 28, R-06 100, R-04 24, R-05 26, NC-01 25, NC-02 15, NC-03 13, NC-04 10,
D-01 10, D-02 10, D-03 9, D-04 13, D-05 6). Evidence: `windows/` in this package; machine-readable
summary `windows/q5b05q1-reviewer-results.json`.

## 3. Regression campaigns

| Suite | Command | Result |
|---|---|---|
| Core solution (`BoltSharp.slnx`: 1 project, 1,257 tests) | `dotnet test BoltSharp.slnx -c Release --no-build --logger trx --results-directory <…>\win-core` | **1,257 passed / 0 failed / 0 skipped** (`q5b05q1-core-windows.trx`, 4 s) |
| Official-driver interoperability | `dotnet test tests/BoltSharp.DriverInteropTests -c Release --logger trx --results-directory <…>\win-interop` | **43 passed / 0 failed / 0 skipped** (`q5b05q1-interop-windows.trx`, 2 min 8 s) |
| Producer probe suite | `dotnet test docs/reviews/probes/Q-B5B-05-PROBE-01 -c Release --logger trx --results-directory <…>\win-probe` | **13 passed / 0 failed / 0 skipped** (`q5b05q1-probe-windows.trx`, 619 ms) |

Note: the three driver-dependent test projects are not members of `BoltSharp.slnx` (repository
convention), so the core run covers exactly the producer's stated 1,257 and the interop suite runs
separately at its stated 43. The reviewer also confirms the solution's project membership
(`src/BoltSharp`, `tests/BoltSharp.Tests`) matches the producer's evidence.

## 4. Focused suites and bounded repeated runs

| Focus | Filter | Result |
|---|---|---|
| RESET state machine / failed session / disposal | `TcpSessionIntegrationTests \| FailureAndRecoveryTests \| TransportFailureTests` | 36/0/0 |
| Multi-client accounting | `MultiClientTests` | 6/0/0 |
| Plaintext transport | `TcpHandshakeTests \| TcpListenerTests \| TcpFramingTests` | 36/0/0 |
| TLS integration | `TlsIntegrationTests` | 9/0/0 |
| Unsupported-command refusal | `UnsupportedCommandRefusalTests \| ResidualLifecycle` | 10/0/0 |
| Interop RESET→GOODBYE sequence | `B5B2_06` (DriverInteropTests) | 1/0/0 |
| Bounded repeats (lifecycle races) | 5 × reset/failure/multi-client filter | 5 × 34/0/0 |

Full transcript: `windows-focused-campaign.txt`. No failures were retained — none occurred.

## 5. Evidence integrity of the producer package (reviewer-owned tooling)

Reviewer-owned independent tooling (`verify-evidence.py` logic re-derived, not reused):

- producer self-verification: **PASSED** (51/51 files, six TRX envelopes, 26 traces, redaction);
- independent manifest check over Git blobs: 51/51 SHA-256 + byte counts + blob IDs match;
  TRX counters independently re-parsed (13/1257/43 × sets);
- checkout portability: 24 files CRLF-explained, 3 Windows TRX files are the producer's original
  mixed-line-ending artifacts — all 51 working-tree files re-hash to their committed blob IDs
  under the repository filters (`git hash-object --path`), i.e. inherited
  `core.autocrlf` checkout behavior, not evidence tampering.

Full transcripts: `09-integrity-verification.txt`.