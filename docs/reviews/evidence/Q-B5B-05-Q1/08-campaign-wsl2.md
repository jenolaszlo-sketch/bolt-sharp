# Ubuntu WSL2 Campaign Record — Q-B5B-05-Q1

Environment: `6.6.114.1-microsoft-standard-WSL2`, Ubuntu 26.04.1 LTS; .NET SDK 10.0.114 with
`DOTNET_ROLL_FORWARD=Major` (only .NET 10 is installed on this image — same inherited arrangement
the producer used). Runs execute in an independent clone at
`/mnt/c/Windows/Temp/opencode/q5b05q1/reviewer-wsl2` (HEAD `8d762dd…`, tree `50f214a…`).
Native Linux remains an outstanding inherited release gate and is **not** claimed.

## 1. Builds

```text
dotnet build BoltSharp.slnx -c Release        -> 0 errors (roll-forward Major)
dotnet build <reviewer harness> -c Release    -> 0 errors (2 environmental advisories:
                                               SourceLink/git-location warnings because the
                                               scratch harness project sits outside a git
                                               working tree when resolved through /mnt/c)
```

## 2. Reviewer reproduction harness

Command: `dotnet run -c Release --no-build -- <evidence-dir>` (script `wsl-focused.sh` follows the
same pattern for the focused suites).

Results — **15/15 scenarios, 364/364 assertions, 0 failures**; per-scenario counts identical to
Windows (R-01 28, R-02 44, R-03 28, R-06 100, R-04 24, R-05 26, NC-01 25, NC-02 15, NC-03 13,
NC-04 10, D-01 10, D-02 10, D-03 9, D-04 13, D-05 6). Evidence: `wsl2/`.

## 3. Regression campaigns

| Suite | Command | Result |
|---|---|---|
| Core solution (`BoltSharp.slnx`, 1,257 tests) | `dotnet test BoltSharp.slnx -c Release --no-build --logger trx --results-directory …/wsl2-core` | **1,257 passed / 0 failed / 0 skipped** (`q5b05q1-core-wsl2.trx`, 4 s) |
| Official-driver interoperability | `dotnet test tests/BoltSharp.DriverInteropTests -c Release --logger trx --results-directory …/wsl2-interop` | **43 passed / 0 failed / 0 skipped** (`q5b05q1-interop-wsl2.trx`, 14 s) |
| Producer probe suite | `dotnet test docs/reviews/probes/Q-B5B-05-PROBE-01 -c Release --logger trx --results-directory …/wsl2-probe` | **13 passed / 0 failed / 0 skipped** (`q5b05q1-probe-wsl2.trx`, 1 s) |

## 4. Focused suites and bounded repeated runs

| Focus | Filter | Result |
|---|---|---|
| RESET state machine / failed session / disposal | `TcpSessionIntegrationTests \| FailureAndRecoveryTests \| TransportFailureTests` | 36/0/0 |
| Multi-client accounting | `MultiClientTests` | 6/0/0 |
| Plaintext transport | `TcpHandshakeTests \| TcpListenerTests \| TcpFramingTests` | 36/0/0 |
| TLS integration | `TlsIntegrationTests` | 9/0/0 |
| Interop RESET→GOODBYE sequence | `B5B2_06` | 1/0/0 |
| Bounded repeats (lifecycle races) | 3 × reset/failure/multi-client filter | 3 × 34/0/0 |

Full transcript: `wsl2-focused-campaign.txt`.

## 5. Platform consistency

The protocol sequence, byte-exact markers, session transitions, connection identities, provider
lifecycle, counters and negative-control verdicts are identical on Windows and WSL2 for every
scenario in this commission. No platform-conditional behavior was observed. Connection accounting
and resource cleanup verified per scenario (GOODBYE completion `completed=1 failed=0`, TLS
handshake failures 0, no leaked accepted connections at host stop).