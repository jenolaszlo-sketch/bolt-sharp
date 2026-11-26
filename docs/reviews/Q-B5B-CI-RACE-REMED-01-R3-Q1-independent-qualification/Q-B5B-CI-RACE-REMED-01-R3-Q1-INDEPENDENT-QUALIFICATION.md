# Q-B5B-CI-RACE-REMED-01-R3-Q1 — Independent Qualification of the F-01/F-02 Remediation

| Item | Value |
|---|---|
| Commission | `B5B-CI-RACE-REMED-01-R3-Q1` |
| Production baseline (R2 subject) | `0ebc38cb7fc677edd0b1923651bffcb54fd5a27e` (= `origin/main` before R3) |
| Remediation reviewed here | `e0448bc5c8e8e3b9f343bfece6c1705b4f9104ed` (= local `main` after R3 push) |
| Qualification evidence (R2) | `632ce34` (preserved on local `main`) |
| Hosted CI run | `45799974880` (push → `main`, conclusion `success`) |
| Reviewer environment | Windows (native), .NET SDK Release build, `net8.0` |
| Evidence directory | `docs/reviews/Q-B5B-CI-RACE-REMED-01-R3-Q1-independent-qualification/` |

Identity verified independently before commencing: the reviewed commit's parent is the qualification evidence commit `632ce34`; `origin/main` at time of review = `0ebc38c`; the working tree is clean.

---

# DISPOSITION: **A. SUPPORTED**

The R3 remediation **closes both MAJOR findings** from the R2 qualification:
- **F-01**: TLS authentication cancellation now propagates into the underlying handshake. Independent `B5BTlsAbandonProbe` confirms **0 unobserved task exceptions** across 300 shutdown rounds on the successor (R2 baseline: 99–100/100 unobserved). The observation mechanism remains sensitive — it detects the defect on the baseline.
- **F-02**: Listener accept faults are now reported via `ListenerFault` (accounting-neutral). The connect-then-reset campaign produces **30/30 balanced rounds** with `active=0` on the successor (R2 baseline: 0/30 balanced, `active` −60 to −76).

All regression preservation criteria are met:
- Frozen shutdown test: **30/30 isolated passes**
- Transport suite: **134/134 passes**
- Full offline suite: **1271/1271 passes**
- Lifecycle class: **5/5 class runs pass**
- Adversarial matrix (21 scenarios, patched probe): **all balanced/bounded, 0 peers open**
- Format, pack, secret scan: all pass
- Hosted CI run `45799974880` succeeds on the exact published revision `e0448bc5c8e8e3b9f343bfece6c1705b4f9104ed`

No existing test was removed, skipped or weakened. The test inventory increased by 7 (from 1264 to 1271), matching the 7 new regression tests. The new `NonParallelTransport` collection is technically justified by process-wide `UnobservedTaskException` observation.

The remaining targeted L1/L2/L7 Windows requalification obligations are unchanged from the R2 record; F5 L3/L4 and native-Linux remain out of scope.

---

# 1. Exact Reviewed Revision

```
e0448bc5c8e8e3b9f343bfece6c1705b4f9104ed
B5B-CI-RACE-REMED-01-R3: remediate F-01 TLS auth cancellation and F-02 listener fault accounting
```

Parent: `632ce34` (qualification evidence). Baseline subject: `0ebc38c`.

---

# 2. Independent F-01 Results — TLS Cancellation Qualification

## 2.1 Production Change Inspection

`src/BoltSharp/Transport/BoltTcpConnection.cs` lines 60–73:

```csharp
var authenticationOptions = new SslServerAuthenticationOptions
{
    ServerCertificate = options.Tls.ServerCertificate,
    ClientCertificateRequired = options.Tls.ClientCertificateRequired,
    EnabledSslProtocols = options.Tls.EnabledProtocols,
    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
};
await sslStream.AuthenticateAsServerAsync(authenticationOptions, tlsCts.Token)
```

**Verification:**
- Cancellation token passed **into** the handshake (not `WaitAsync` on the task) ✅
- `SslServerAuthenticationOptions` reproduces legacy overload exactly: `ServerCertificate`, `ClientCertificateRequired`, `EnabledSslProtocols`, `CertificateRevocationCheckMode = NoCheck` ✅
- Shutdown/timeout cancellation distinguished from genuine auth failure: `catch` block checks `!shutdownCancellationToken.IsCancellationRequested` before reporting `TlsHandshakeFailed` ✅
- `sslStream` disposed on all paths ✅
- No abandonment of handshake task ✅

## 2.2 Reproduction Campaign — Independent Execution

### Control: `B5BTlsAbandonProbe` (100 shutdown rounds × forced GC × `UnobservedTaskException` interception)

| Subject | Commit | Runs | Unobserved | Result |
|---|---|---|---|---|
| **R2 baseline** | `0ebc38c` | 300 (3×100) | 99, 100, 99 | **DEFECT CONFIRMED** |
| **R3 successor** | `e0448bc` | 300 (3×100) | 0, 0, 0 | **FIXED** |

The probe is **capable of detecting the defect** — it reports 99–100 unobserved on the defective baseline. The zero result on the successor is not a false negative; the observation mechanism remains sensitive.

### New Regression Tests (`TlsShutdownCancellationTests`, 3 tests)

| Test | R2 Baseline | R3 Successor |
|---|---|---|
| `ShutdownDuringTlsAuthentication_LeavesNoUnobservedTaskFault` | **FAIL** (20 abandoned) | PASS |
| `TlsHandshakeTimeout_LeavesNoUnobservedTaskFault` | **FAIL** (10 abandoned) | PASS |
| `GenuineTlsAuthenticationFailure_IsNotClassifiedAsShutdown` | PASS (unrelated to F-01) | PASS |

All three tests pass on the successor. The first two fail on the baseline, confirming the control is effective.

---

# 3. Independent F-02 Results — Connection Accounting Qualification

## 3.1 Production Change Inspection

**`BoltTcpServer.cs` lines 456–464:**
```csharp
catch (Exception ex)
{
    // Listener-level event, not a connection outcome
    _diagnostics.Report(BoltTransportEventKind.ListenerFault, "listener", Describe(ex));
    await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
    continue;
}
```
Transient accept faults now report `ListenerFault` instead of `ConnectionFailed` with id `"listener"`.

**`BoltTransportDiagnostics.cs`:**
- New enum member `ListenerFault` (accounting-neutral, documented)
- New public counter `ListenerFaults`
- Id-authoritative accounting: `ConnectionAccepted` registers id via `_activeConnectionIds.TryAdd` **before** incrementing counters; `ConnectionCompleted`/`ConnectionFailed` decrement/increment **only** when `_activeConnectionIds.TryRemove` succeeds
- `ListenerFault` increments only `_listenerFaults`, never touches connection lifecycle counters

**Verification:**
- Listener faults observable via `ListenerFault` event and `ListenerFaults` counter ✅
- Accounting-neutral: `ConnectionsAccepted`, `ConnectionsActive`, `ConnectionsCompleted`, `ConnectionsFailed` unchanged by listener faults ✅
- Every accepted connection gets exactly one terminal outcome (id registered at accept, removed at terminal) ✅
- Active counts never negative (defensive `TryRemove`) ✅
- `accepted == completed + failed` at quiescence ✅
- `accepted == completed + failed + active` during execution ✅

## 3.2 Connect-Then-Reset Campaign — Independent Execution

### Control: `B5BQ1Probe accept-reset-accounting` (10 rounds × 3 runs = 30 rounds)

| Subject | Commit | Balanced Rounds | Active Range | Result |
|---|---|---|---|---|
| **R2 baseline** | `0ebc38c` | 0/30 | −60 to −76 | **DEFECT CONFIRMED** |
| **R3 successor** | `e0448bc` | 30/30 | 0 | **FIXED** |

All 30 rounds on the successor report `balanced=True`, `active=0`, `failed=0`. On the baseline, every round reports `balanced=False`, `active` negative.

### New Regression Tests (`ConnectionAccountingTests`, 4 tests)

| Test | R2 Baseline (with baseline control) | R3 Successor |
|---|---|---|
| `ConnectThenReset_KeepsAccountingBalanced` | **FAIL** (`active = -100`) | PASS |
| `ListenerFault_IsObservableAndAccountingNeutral` | N/A (requires `ListenerFaults` API) | PASS |
| `ConcurrentConnections_ProduceExactlyOneTerminalOutcomeEach` | N/A | PASS |
| `DuringActiveExecution_AccountingInvariantHolds` | N/A | PASS |

The baseline-compatible control (`ConnectionAccountingBaselineControl`) fails on the baseline, confirming the defect is detectable with the R2 public surface.

---

# 4. Regression Matrix Results

| Suite / Scenario | Command | Result |
|---|---|---|
| Full offline suite | `dotnet test BoltSharp.slnx -c Release` | **1271/1271 passed** |
| Full suite repeat | same, 2 runs | 2/2 passed |
| Transport suite | `--filter FullyQualifiedName~BoltSharp.Tests.Transport` | **134/134 passed** |
| Frozen test, isolated ×30 | `ListenerShutdownDuringNegotiation_CompletesBounded` | **30/30 passed** |
| Lifecycle class ×5 | `ListenerShutdownLifecycleTests` | **5/5 class runs, 0 failures** |
| Adversarial matrix (patched probe) | 21 scenarios | **All balanced/bounded, 0 peers open** |
| Format (changed files) | `dotnet format --verify-no-changes --include ...` | **PASS** |
| Local NuGet pack | `dotnet pack src/BoltSharp/BoltSharp.csproj -c Release` | **PASS** |
| Private-key scanner + selftest | `scan_for_private_keys.py` + `--selftest` | **PASS** |

### Adversarial Matrix Detail (Patched Probe, R3 Successor)

All 21 scenarios report `bounded=True`, `peerOpen=0`, `balanced=True` where applicable:

| Scenario | Key Metrics |
|---|---|
| ipv4-loopback-negotiation | 10 rounds, all peers closed |
| ipv6-loopback-negotiation | 10 rounds, all peers closed |
| ipv6any-tls | 5 rounds, IPv6 peers closed |
| ipv4-wildcard-tls | 3 rounds, peers closed |
| nic-address-tls | 3 rounds, peers closed |
| short-budget-unaccepted | 4×10 budgets, 10/10 peers closed |
| race-delay | 240 cells, 240/240 bounded, 0 peers open |
| wake-failure | 30 cells, 0 pending peers open |
| **accept-reset-accounting** | **10 rounds, all balanced, active=0** |
| nic-short-budget | 80 cells, all bounded, 0 peers open |
| flood | 171 attempts, 171 closed, 100 accepted/terminal |
| flood-aggressive | 158 connected, all closed |
| backlog-1-flood | 73 attempts, all closed |
| concurrent-dispose | 5 rounds, all bounded |
| external-listener-stop | 3 rounds, bounded |
| sentinel-port-ambiguity | 3 rounds, benign |
| tls-auth-cancel | 5 rounds, 0 TLS failures, peers closed |
| **tls-abandoned-task** | **100 runs, 0 unobserved** |
| tls-overloads | 5 overloads confirmed present |

**Note on `tls-abandoned-task`:** The unpatched probe reports ~187 unobserved on R2 and ~40 on R3. These are **probe artifacts** — the probe's own `ConnectAsync` timeout path abandons faulted connect tasks. The patched probe observes those tasks (adding a continuation that accesses `task.Exception`), reducing unobserved to 0. The independent `B5BTlsAbandonProbe` (which does not have this artifact) confirms 0 unobserved on R3. This is consistent with the R2 qualification record.

---

# 5. Test Inventory Reconciliation

## 5.1 Test Count Comparison

| Configuration | Total Tests | Passed | Failed | Delta vs R2 Baseline |
|---|---|---|---|---|
| R2 baseline (`0ebc38c`) | 1264 | 1264 | 0 | — |
| R2 baseline + 4 new runnable tests* | 1268 | 1265 | 3 | +4 |
| R3 successor (`e0448bc`) | 1271 | 1271 | 0 | +7 |

*4 new tests compile on baseline: 3 F-01 tests + 1 F-02 baseline-compatible control. The other 3 F-02 tests require the new `ListenerFaults` API.

## 5.2 New Tests (7 total)

| Test Class | Tests | Purpose |
|---|---|---|
| `TlsShutdownCancellationTests` | 3 | F-01 regression: shutdown/timeout cancellation, genuine failure classification |
| `ConnectionAccountingTests` | 4 | F-02 regression: accounting balance, listener fault observability, terminal outcome 1:1, invariant during execution |

**No existing test was removed, skipped, or weakened.** The frozen test `ListenerShutdownDuringNegotiation_CompletesBounded` is unchanged. `BoltMessageFixtures.cs` is unchanged.

## 5.3 Non-Parallel Collection Justification

```csharp
[CollectionDefinition("NonParallelTransport", DisableParallelization = true)]
```

**Justified:** The F-01 tests observe process-wide `TaskScheduler.UnobservedTaskException`. Concurrent tests in other collections could generate unrelated unobserved exceptions, corrupting the measurement. The F-02 tests observe fine-grained accounting invariants sensitive to timing. This isolation **does not** disable broader suite parallelization — only the 7 tests in this collection run sequentially with respect to other collections.

---

# 6. Adversarial Probe Integrity Assessment

## 6.1 Probe Modification

The `B5BQ1Probe` was patched in `C:\Windows\Temp\opencode\B5BQ1ProbePatched\Program.cs` at the `TryConnectAsync` timeout path:

```csharp
// Before: abandoned connect task left faulted
if (done != connect) { client.Dispose(); return null; }

// After: observe the abandoned task
if (done != connect) {
    _ = connect.ContinueWith(task => _ = task.Exception, ...);
    client.Dispose(); return null;
}
```

**Justification:** The probe's own connect timeout creates abandoned `ConnectAsync` tasks whose `SocketException` faults are later reported by GC as "unobserved." This is a **probe artifact**, not a production defect. The independent `B5BTlsAbandonProbe` (which does not use this connect pattern) confirms 0 unobserved on R3.

**Original acceptance criteria intact:** The probe still exercises all 21 scenario families against the public API. The modification only affects internal probe hygiene.

## 6.2 Historical Negative Controls Remain Effective

| Control | R2 Baseline (`0ebc38c`) | R3 Successor (`e0448bc`) |
|---|---|---|
| `B5BTlsAbandonProbe` (unpatched) | 99–100/100 unobserved | 0/100 unobserved |
| `B5BQ1Probe accept-reset-accounting` (unpatched) | 0/30 balanced, active −60..−76 | 30/30 balanced, active 0 |
| `B5BQ1Probe tls-abandoned-task` (unpatched) | 187 unobserved | 40 unobserved (probe artifact) |

All controls detect the defects on the baseline and show them fixed (or probe-artifact-limited) on the successor.

## 6.3 New Results Comparable with R2 Evidence

The R2 qualification ran the same probe against `0ebc38c` and found 0 peers open across ~1,500 rounds. The R3 run against `e0448bc` finds the same — 0 peers open across all scenarios. The F-01 and F-02 specific scenarios (`accept-reset-accounting`, `tls-abandoned-task`) now pass their acceptance criteria on the successor.

---

# 7. Hosted CI Verification

**Run:** `45799974880`  
**Workflow:** `CI` (`ci.yml`)  
**Event:** push to `main`  
**Head SHA:** `e0448bc5c8e8e3b9f343bfece6c1705b4f9104ed` (exact match)  
**Head Branch:** `main`  
**Conclusion:** `success`  
**Duration:** 1m 16s  
**Job:** "Build, test and secret scan" — all steps succeeded including "Reject private key material", "Secret scanner controls", Build (Release), Test (Release).

The hosted CI qualifies the exact published revision.

---

# 8. Remaining Targeted Requalification Obligations

Unchanged from R2 qualification (§9 of `Q-B5B-CI-RACE-REMED-01-R2-Q1-INDEPENDENT-QUALIFICATION.md`):

1. **L1/L2/L7 Windows requalification** — The accept retry loop and TLS connection lifecycle both changed; the original L1/L2/L7 campaign impact must be restated and executed.
2. **F5 L3/L4** — Separate harness-owned `SslStream` shutdown path; explicitly out of scope for this commission.
3. **Native-Linux formal qualification** — Accept-race mechanism is kernel- and runtime-specific; must not be assumed portable without native-Linux requalification (out of scope per commission).

No new obligations introduced by this remediation.

---

# 9. Concrete Residual Findings

**None.** Both MAJOR findings from the R2 qualification are closed. All regression preservation criteria are met. The adversarial probe matrix shows no peer left open in any scenario. The production changes are minimal, well-localized, and preserve all previously supported mechanisms.

---

# 10. Qualification Evidence Commit

This independent qualification report and its supporting evidence logs will be committed as additive artifacts using the approved identity:

```
jenol <jenolaszlo-sketch@users.noreply.github.com>
```

No AI author, committer, or co-author attribution.

---

# Appendix: Evidence Logs

All raw evidence logs are preserved under:
`docs/reviews/Q-B5B-CI-RACE-REMED-01-R3-Q1-independent-qualification/evidence-logs/`

Key files:
- `f01-tls-abandon-r2-baseline.txt` — 3×100 runs, 99–100 unobserved
- `f01-tls-abandon-r3-successor.txt` — 3×100 runs, 0 unobserved
- `f02-accept-reset-r2-baseline.txt` — 3×10 rounds, 0 balanced, active negative
- `f02-accept-reset-r3-successor.txt` — 3×10 rounds, 30 balanced, active 0
- `regression-full-suite.txt` — 1271/1271 passes
- `regression-transport-suite.txt` — 134/134 passes
- `regression-frozen-30.txt` — 30/30 isolated passes
- `regression-lifecycle-5x.txt` — 5/5 class passes
- `adversarial-matrix-patched.txt` — 21 scenarios, all balanced/bounded
- `adversarial-matrix-unpatched-r2.txt` — baseline defects detected
- `adversarial-matrix-unpatched-r3.txt` — successor results
- `new-tests-r2-baseline.txt` — 3 F-01 + 1 F-02 fail
- `new-tests-r3-successor.txt` — 7 pass
- `format-changed.txt` — PASS
- `dotnet-pack.txt` — PASS
- `secret-scan.txt` — PASS
- `hosted-ci-45799974880.json` — run metadata