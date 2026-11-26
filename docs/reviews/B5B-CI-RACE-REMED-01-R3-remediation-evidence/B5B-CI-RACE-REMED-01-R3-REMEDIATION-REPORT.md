# B5B-CI-RACE-REMED-01-R3 Remediation Report

**Commission**: B5B-CI-RACE-REMED-01-R3  
**Subject**: Remediate F-01 (TLS authentication cancellation) and F-02 (listener fault accounting) from independent qualification B5B-CI-RACE-REMED-01-R2-Q1  
**Author**: jenol <jenolaszlo-sketch@users.noreply.github.com>  
**Date**: 2026-11-26  
**Baseline commit**: 0ebc38c (R2 subject)  
**Qualification evidence commit**: 632ce34 (preserved on local main)  

---

## Executive Summary

Both MAJOR findings from the independent qualification are **remediated and validated**. The remediation:

1. **F-01** – TLS authentication cancellation now propagates into the underlying handshake via `SslStream.AuthenticateAsServerAsync(SslServerAuthenticationOptions, CancellationToken)` instead of abandoning the handshake task with `.WaitAsync(token)`. Result: **0 unobserved task exceptions** across all probes and regression tests (baseline: 99–100/100).

2. **F-02** – Listener accept faults are now reported through a dedicated `ListenerFault` event and `ListenerFaults` counter that are **accounting-neutral**. Active connection accounting is id-authoritative and defensive; `ConnectionsActive` never goes negative; `accepted == completed + failed` at quiescence. Result: **balanced in every round** (baseline: 0/30 rounds balanced, active reached −70).

All adversarial controls from the qualification probe (B5BQ1Probe) pass on the successor. No regression in existing test suite (1271 tests). Frozen shutdown test passes 30/30 isolated runs. Lifecycle class passes 5/5 runs.

---

## Changes

### Production Code (3 files)

| File | Finding | Change |
|------|---------|--------|
| `src/BoltSharp/Transport/BoltTcpConnection.cs` | F-01 | Replaced `AuthenticateAsServerAsync(...).WaitAsync(tlsCts.Token)` with `AuthenticateAsServerAsync(SslServerAuthenticationOptions, tlsCts.Token)`. Added `using System.Security.Cryptography.X509Certificates;`. Options reproduce legacy semantics exactly: `ServerCertificate`, `ClientCertificateRequired`, `EnabledSslProtocols`, `CertificateRevocationCheckMode = NoCheck`. |
| `src/BoltSharp/Transport/BoltTcpServer.cs` | F-02 | Accept-loop general catch now reports `ListenerFault` (accounting-neutral) instead of `ConnectionFailed` with id `"listener"`. |
| `src/BoltSharp/Transport/BoltTransportDiagnostics.cs` | F-02 | Added `ListenerFault` enum member, `_listenerFaults` field, public `ListenerFaults` property. `Report` switch now uses id-authoritative accounting: `ConnectionAccepted` registers id before counting; `ConnectionCompleted`/`ConnectionFailed` decrement/increment only when `_activeConnectionIds.TryRemove` succeeds; `ListenerFault` only increments `_listenerFaults`. |

### Tests (3 new files)

| File | Purpose |
|------|---------|
| `tests/BoltSharp.Tests/Transport/NonParallelTransportCollection.cs` | xUnit collection with `DisableParallelization = true` so `TaskScheduler.UnobservedTaskException` capture is not polluted by concurrent tests. |
| `tests/BoltSharp.Tests/Transport/TlsShutdownCancellationTests.cs` | F-01 regression tests: (1) shutdown during TLS auth → 0 unobserved; (2) TLS handshake timeout → 0 unobserved + genuine failure still classified as `TlsHandshakeFailures`; (3) genuine plaintext auth failure → not classified as listener shutdown. |
| `tests/BoltSharp.Tests/Transport/ConnectionAccountingTests.cs` | F-02 regression tests: (1) connect-then-reset keeps accounting balanced (compiles/runs on baseline, fails there); (2) `ListenerFault` observable and accounting-neutral; (3) concurrent connections produce exactly one terminal outcome each; (4) invariant `accepted == completed + failed + active` holds during execution. |

---

## Validation Evidence

### 1. F-01: TLS Authentication Cancellation

| Probe / Test | Baseline (0ebc38c) | Successor (HEAD) |
|--------------|-------------------|------------------|
| `B5BTlsAbandonProbe` (3×100 runs) | 99–100/100 unobserved | **0/100 unobserved** (3/3 runs) |
| `B5BQ1Probe tls-abandoned-task` (patched, 1 run) | — | **0 unobserved** |
| `TlsShutdownCancellationTests.ShutdownDuringTlsAuthentication` | 3/3 tests FAIL (20 abandoned each) | **PASS** |
| `TlsShutdownCancellationTests.TlsHandshakeTimeout` | 1/1 test FAIL (10 abandoned) | **PASS** |
| `TlsShutdownCancellationTests.GenuineTlsAuthenticationFailure` | — | **PASS** |

**Control reproduced at baseline**: Yes – independent `B5BTlsAbandonProbe` shows 99–100/100 unobserved on 0ebc38c.

### 2. F-02: Listener Fault Accounting

| Probe / Test | Baseline (0ebc38c) | Successor (HEAD) |
|--------------|-------------------|------------------|
| `B5BQ1Probe accept-reset-accounting` (3×10 rounds) | **0/30 rounds balanced**, active −63 to −72 | **30/30 rounds balanced**, active 0 |
| `ConnectionAccountingTests.ConnectThenReset_KeepsAccountingBalanced` | 3/3 tests FAIL (active = −100) | **PASS** |
| `ConnectionAccountingTests.ListenerFault_IsObservableAndAccountingNeutral` | N/A (requires R3 API) | **PASS** |
| `ConnectionAccountingTests.ConcurrentConnections_ProduceExactlyOneTerminalOutcomeEach` | — | **PASS** |
| `ConnectionAccountingTests.DuringActiveExecution_AccountingInvariantHolds` | — | **PASS** |

**Control reproduced at baseline**: Yes – `B5BQ1Probe` shows 50/50 unbalanced rounds in 5 isolated runs at R2.

### 3. Full Regression & Adversarial Matrix

| Suite | Runs | Result |
|-------|------|--------|
| Full offline suite (`dotnet test BoltSharp.slnx -c Release`) | 2 | **1271/1271 PASS** |
| Transport suite (`FullyQualifiedName~BoltSharp.Tests.Transport`) | 1 | **134/134 PASS** |
| Frozen shutdown test `ListenerShutdownDuringNegotiation_CompletesBounded` | 30 isolated | **30/30 PASS** |
| Lifecycle class `ListenerShutdownLifecycleTests` | 5 | **5/5 PASS** |
| B5BQ1Probe patched full matrix (21 scenarios) | 1 | **All scenarios balanced/bounded** |
| Format on changed files | 1 | **PASS** |
| `dotnet pack` (local NuGet) | 1 | **PASS** |
| Private-key scanner + selftest | 1 | **PASS** |

### 4. Qualification Commit Preserved

The qualification evidence commit `632ce34` ("Q-B5B-CI-RACE-REMED-01-R2-Q1: independent qualification of the listener-shutdown remediation") remains on local `main` and will be included in the fast-forward push.

---

## Scope Compliance

| Requirement | Status |
|-------------|--------|
| Preserve R2 mechanism (sentinel, ownership registry, registration-before-execution, bounded shutdown, concurrent/repeated shutdown, caller cancellation, observable outcome, public API) | ✅ |
| Add focused regression tests for F-01 and F-02 | ✅ (4 + 4 tests in `DisableParallelization` collection) |
| Demonstrate controls fail at 0ebc38c, pass at successor | ✅ (documented above) |
| Reuse qualification controls (`B5BQ1Probe`, `B5BTlsAbandonProbe`) | ✅ |
| Preserve existing tests unchanged unless justified | ✅ (no existing test modified) |
| Release build + full offline + Transport suite | ✅ |
| F-01 and F-02 reproduction campaigns | ✅ (probes + new tests) |
| Frozen shutdown test ×30 isolated | ✅ |
| Relevant R2 sentinel/ownership controls | ✅ (all 21 B5BQ1Probe scenarios pass) |
| Format changed files | ✅ |
| Local NuGet pack | ✅ |
| Private-key scanner + outgoing-history checks | ✅ |
| No force-push, history rewrite, release tag, or NuGet publish | ✅ |
| Commit as `jenol <jenolaszlo-sketch@users.noreply.github.com>` (no AI attribution) | ✅ |
| Fast-forward `main` | ✅ (planned) |

---

## Owner Acceptance Gate

**Selection: A – Remediation complete and validated. Ready for fast-forward push to `main` and hosted CI observation.**

Evidence summary:
- F-01: unobserved = 0 (acceptance criterion met)
- F-02: balanced = True, active ≥ 0 (acceptance criterion met)
- All adversarial controls pass
- Full regression suite passes
- No scope exclusions violated
- Qualification evidence preserved

---

## Artifacts

Evidence logs: `docs/reviews/B5B-CI-RACE-REMED-01-R3-remediation-evidence/evidence-logs/`

Key files:
- `full-suite.txt`, `full-suite-repeat.txt` – 1271 tests pass
- `transport-suite.txt` – 134 transport tests pass
- `frozen-isolated-30.txt` – 30/30 isolated frozen shutdown
- `lifecycle-5x.txt` – 5/5 lifecycle class
- `tls-abandon-0ebc38c-3x.txt` / `tls-abandon-successor-3x.txt` – F-01 baseline vs successor
- `accept-reset-0ebc38c-3x.txt` / `accept-reset-successor-3x.txt` – F-02 baseline vs successor
- `b5bq1-probe-patched-full-successor.txt` – all 21 adversarial scenarios
- `new-regression-tests-baseline-0ebc38c.txt` / `new-regression-tests-successor.txt` – new tests fail at baseline, pass at successor
- `format-changed-files.txt` – changed files format clean
- `dotnet-pack.txt` – NuGet package created
- `secret-scan.txt` – no private keys, selftest passes

---

## Next Steps (Post-Report)

1. `git add -A`
2. `git commit -m "B5B-CI-RACE-REMED-01-R3: remediate F-01 TLS auth cancellation and F-02 listener fault accounting" --author="jenol <jenolaszlo-sketch@users.noreply.github.com>"`
3. `git push origin main` (fast-forward; origin/main at 0ebc38c, local main at 632ce34 + remediation)
4. Observe hosted CI on exact published revision
5. Record headSha / conclusion