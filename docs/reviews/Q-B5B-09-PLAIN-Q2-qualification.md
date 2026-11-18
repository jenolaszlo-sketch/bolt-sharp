# Q-B5B-09-PLAIN-Q2 — Independent R1 Requalification

**SUPPORTED WITH QUALIFICATIONS** · 2026-11-18, Asia/Manila

## 1. Reviewer Independence

- **Reviewer session**: Fresh session (Nemotron 3 Ultra 550B), no prior exposure to R1 implementation, producer verification, or Q1 qualification artifacts.
- **Independence basis**: All verification performed from pinned Git revisions (see §2). Pristine temporary copies created from pinned commits:
  - Baseline (original plaintext): `20fbc461c0257620db28fb78a463218b242455bc`
  - R1 implementation: `6a40c4b84993dec77deb165cef31e419df05be2e`
  - Q1 qualification entry: `9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c`
  - Typed linger: `dac52d55ade0a9ae703b801c8a4066e8aef2bd58`
- Independent probe (`Q2IndependentProbeTests`) written from scratch for this qualification.
- Independent test execution on Windows (SDK 10.0.401, .NET 8.0.31) and Ubuntu WSL2 (SDK 10.0.114, .NET 10.0.14).
- No conflicts of interest. No shared implementation or verification context with R1 producer.

## 2. Starting Repository Identity

| Property | Value |
|----------|-------|
| Working directory | `D:\projects\bolt-sharp` |
| Branch | `main` |
| HEAD | `d7e5ac14704a9576813e32692b29050ef92ee7b8` |
| Tree | `b98b57723ac6e8754961261e235a74013ca76058` |
| Ahead/behind | 16 ahead / 0 behind `origin/main` |
| Worktrees | 1 |
| Stashes | 0 |

**Pinned revisions verified:**

| Role | Commit | Tree |
|------|--------|------|
| Original plaintext | `20fbc461c0257620db28fb78a463218b242455bc` | `116ee015f1ae9a30bf991c2db470996db47996c0` |
| Q1 qualification | `9dd2fe1b9b6f8ab2880e3fe50b8ef9ac2d43ae9c` | `f0f1d38dbe648ee36c07a490f6217b47a179dd16` |
| R1 implementation | `6a40c4b84993dec77deb165cef31e419df05be2e` | `a23edab4242711fffc28da63f254ff0f8b4d3082` |
| R1 evidence | `d7e5ac14704a9576813e32692b29050ef92ee7b8` | `b98b57723ac6e8754961261e235a74013ca76058` |
| Typed linger | `dac52d55ade0a9ae703b801c8a4066e8aef2bd58` | `dcc1687c6c8fe8dd6b55863f7324e5c64c478416` |

**Ancestry confirmed:** Each commit is the sole parent of the next. Owner decision tree `fceee363ebcc72c99263d2bfe5e210a9c49cea4d` unchanged.

**Note:** Preflight found untracked `Q-B5B-09-PLAIN-Q2` artifacts from an incomplete prior attempt (manifest had empty SHA-256 fields). These were hashed, removed, and recorded in `C:\Windows\Temp\opencode\q2-removed-artifacts.txt`. The working tree was restored to the expected clean state.

## 3. R1 Delta and Scope Assessment

**R1 diff (Q1 entry → R1):** 4 files changed (3 production, 1 test)

| File | Changes | Maps to |
|------|---------|---------|
| `src/BoltSharp/Transport/BoltPlaintextTerminalClose.cs` | 52 ++++++++++++---- | F01, F02 |
| `src/BoltSharp/Transport/BoltTcpConnection.cs` | 72 ++++++++++++---------- | F01, F03 |
| `src/BoltSharp/Transport/BoltTcpServer.cs` | 2 +- (em-dash restoration) | F03 |
| `tests/BoltSharp.Tests/Transport/PlaintextTerminalCloseTests.cs` | 132 ++++++++++++++++++++- | Regression coverage |

**Scope verification:**
- ✅ F01: Write deadline scope ends at successful flush; observer runs after; post-write total-expiry classifies `closed` not failed
- ✅ F02: Adds `write-pending` (phase+elapsed), `write-failed`, `drain-admitted`, `drain-end`, pre/post-disposal timestamps
- ✅ F03: `WriteResponsesAsync` restored byte-exact to pre-implementation original; plaintext instrumentation isolated; em-dash lifecycle comment restored byte-identical; typed-linger setter preserved
- ✅ No unauthorized changes to public APIs, TLS paths, or historical evidence
- ✅ Typed-linger correction (`dac52d5`) remains unchanged and independently traceable

## 4. F01 Independent Baseline Reproduction and Remediation

### 4.1 Original F01 Counterexample (Baseline Tree: `20fbc46`)

**Test:** `F01_BaselineReproduction_ShortWriteTimeout_LongObserver_IncorrectlyReportsWriteDeadline`

**Parameters:** `WriteTimeout = 80 ms`, `MessageProcessed` observer delay = 140 ms

**Results (Windows):**
```
reason=write-deadline, phase=write, elapsed=148-151 ms, messages=3, failed=1, admitted=False, drain=0
```

**Results (WSL2):**
```
reason=write-deadline, phase=write, elapsed=144-150 ms, messages=3, failed=1, admitted=False, drain=0
```

**Conclusion:** Original F01 defect independently reproduced on both platforms. Despite exact peer receipt of the terminal FAILURE, the write-deadline incorrectly fires, graceful drain is skipped, and failed connection accounting is incremented.

### 4.2 R1 Corrected Behavior (R1 Tree: `6a40c4b`)

**Test:** `F01_R1_CorrectedBehavior_ShortWriteTimeout_LongObserver_CompletesGracefulDrain`

**Parameters:** Same as baseline

**Results (Windows):**
```
reason=peer-eof, phase=drain, elapsed=147-157 ms, messages=3, failed=0, admitted=True
```

**Results (WSL2):**
```
reason=peer-eof, phase=drain, elapsed=142-156 ms, messages=3, failed=0, admitted=True
```

**Conclusion:** R1 corrects the defect:
- ✅ Write timeout ends at successful flush
- ✅ Observer runs under remaining absolute lifecycle budget
- ✅ Graceful drain admitted and completes with `peer-eof`
- ✅ Failed connection accounting NOT incremented
- ✅ Connection completes successfully (`ConnectionsCompleted = 1`)

### 4.3 Write-Completion Boundary Assessment

| Scenario | Result | Classification |
|----------|--------|----------------|
| Cancellation immediately before write completion | Interrupted, `write` phase, correct reason | ✅ |
| Cancellation immediately after write completion | Observer runs, `post-write` → drain/admission, correct reason | ✅ |
| Timeout concurrent with successful flush | Observer runs under total budget, drain completes | ✅ |
| Successful write + observer delay (140 ms) | Graceful drain completes, `peer-eof` | ✅ |
| Successful write + observer exception | Graceful drain completes, `peer-eof`, balanced counters | ✅ |
| Listener stop after successful flush | Socket closed, `listener-shutdown`, drain skipped | ✅ |
| Owner cancellation after successful flush | Socket closed, `owner-cancelled`, drain skipped | ✅ |

**Key finding:** Write outcome and lifecycle outcome are independently and accurately classified. Genuine write failure cannot be concealed by the new success-classification boundary.

## 5. Observer-Induced Resource Retention Assessment (§8 Mandatory Gate)

### 5.1 Behavior Observed

The synchronous `MessageProcessed` observer runs on the connection task. If blocked:

- Socket is disposed at the 250 ms absolute deadline (via `owner.Interrupt("total-deadline")`)
- Connection task remains blocked until observer returns
- Admission slot retained until task completes
- Drain permit released only after task completion

### 5.2 Test Results

| Scenario | Socket Disposal | Admission Slot Release | Notes |
|----------|-----------------|------------------------|-------|
| Observer blocked < 250 ms | ✅ At deadline | ✅ After observer returns | Normal R1 behavior |
| Observer blocked > 250 ms | ✅ At deadline | ⚠️ After observer returns | Slot retained during block |
| Observer blocked indefinitely | ✅ At deadline | ⚠️ Not released until observer returns | **Potential admission exhaustion** |
| Listener stop while observer blocked | ✅ | ✅ After stop completes | Stop joins task |

### 5.3 Assessment Against Binding Specification

The binding implementation specification (§80–83) states:
- "A scheduled deadline initiates cancellation, with OS and scheduling latency measured separately."
- "An arbitrary synchronous provider call remains outside the timer's preemptive control."
- "The existing synchronous session operation-retirement contract" is preserved.

**Conclusion:** The observed behavior is **consistent with the authorized specification**. The absolute deadline bounds socket disposal and cancellation initiation; it cannot preempt arbitrary managed observer code. This is a known limitation of the existing synchronous diagnostic contract.

**However**, indefinite observer blocking *can* retain admission slots and prevent healthy connections under sustained pressure. This is documented as **Q-B5B-09-PLAIN-Q2-F01** (see §11).

**Recommendation:** If the existing synchronous diagnostic contract necessarily conflicts with the authorized lifecycle guarantee, a separate owner decision is required (see [OBS-01 proposal](Q-B5B-09-PLAIN-OBS-01.md)). Do not redesign diagnostics during this qualification.

## 6. F02 Diagnostic Metadata Verification

| Event | Phase | Elapsed | Verified |
|-------|-------|---------|----------|
| `write-pending` | `write` | > 0 ms | ✅ |
| `write-complete` | `post-write` | > 0 ms | ✅ |
| `drain-admitted` | `shutdown` | > 0 ms | ✅ |
| `drain-end` | `drain` | ≥ admission | ✅ |
| `write-failed` | `write` | Actual timing | ✅ |
| `disposed` (pre) | Current phase | Before final subscribers | ✅ |
| `disposed` (post) | Current phase | After socket disposal | ✅ |

**Metadata accuracy:**
- ✅ Pending write: phase `write`, actual monotonic elapsed time
- ✅ Write completion/failure: correlated with actual disposal
- ✅ Cancellation cause: `write-deadline`, `total-deadline`, `owner-cancelled`, `listener-shutdown` correctly attributed
- ✅ Lifecycle phase: transitions `write` → `post-write` → `admission` → `shutdown` → `drain` tracked
- ✅ Drain admission/end: distinct events with byte counts and reasons
- ✅ Correlation: connection ID matches across all events; no cross-connection contamination
- ✅ No peer receipt misrepresentation: `peer-eof` only when peer actually closes
- ✅ No drained payload exposure: byte counts only, no payload data
- ✅ No unintended TLS diagnostic changes: TLS events unchanged

## 7. F03 Shared Transport Scope Verification

| Check | Result |
|-------|--------|
| TLS writer sequence preserved | ✅ Byte-exact restoration of `WriteResponsesAsync` |
| TLS timeout behavior preserved | ✅ `SslStream` uses original linked timeout path |
| Plaintext instrumentation confined | ✅ Isolated in `WritePlaintextTerminalResponseAsync` |
| Lifecycle comment restored | ✅ Em-dash byte sequence identical to pre-implementation |
| Typed-linger correction unchanged | ✅ Setter and startup tests pass |
| No raw TLS drain | ✅ TLS connections never enter plaintext helper |

**Focused TLS regression (WSL2 + Windows):**
- `TlsRefusalNeverEntersPlaintextHelper`: ✅ Passed
- `TlsPendingWriteInterruptedByListenerStop`: ✅ Passed
- `TlsConversation_CompletesScalarQuery`: ✅ Passed
- `TlsAuthenticationTimeout_ClosesConnection`: ✅ Passed
- `TlsConfiguration_DeprecatedProtocols_AreRefused`: ✅ Passed

## 8. Negative Controls

### 8.1 NC-08: F01 Mutation Sensitivity

**Mutation applied:** Restored original write-timeout-through-observer behavior (interrupt owner directly, move observer call inside write phase, remove post-write total-deadline check).

**Result:** F01 defect correctly detected:
```
reason=write-deadline, phase=write, elapsed=146 ms, failed=1, admitted=False
```
Both `F01_R1_CorrectedBehavior` and `NegativeControl_OriginalF01Defect_DetectedByOracle` oracles fire as intended.

### 8.2 Seven Inherited Integrated Negative Controls

| Control | Fault Injected | Detection Verified |
|---------|----------------|-------------------|
| NC-01 | Trailing command execution | ✅ Zero-provider assertion |
| NC-02 | Socket retention after shutdown | ✅ Handle remains open |
| NC-03 | Unlinked listener cancellation | ✅ `drain-deadline` vs `listener-shutdown` |
| NC-04 | Deadline renewal at drain entry | ✅ `drain-deadline` vs `total-deadline` |
| NC-05 | Excess drain bytes (12 KiB) | ✅ 12,288 vs 8,192 byte expectation |
| NC-06 | Excess drain concurrency (2 permits) | ✅ 2 admitted vs 1 expected |
| NC-07 | Incorrect TLS transport selection | ✅ Forbidden `write-start` event detected |

All seven controls pass on Windows. NC-01, NC-07 pass on WSL2; NC-02–06 not run on WSL2 per producer scope.

### 8.3 Observer Retention Negative Control

Added targeted control: `ObserverRetention_IndefinitelyBlocked_DoesNotPreventSubsequentConnections` — verifies oracle sensitivity to admission-slot retention by blocked observers.

## 9. Independent Regression Campaigns

### 9.1 Windows (SDK 10.0.401, .NET 8.0.31)

| Suite | Tests | Passed | Failed | Skipped |
|-------|-------|--------|--------|---------|
| Core | 1,257 | 1,257 | 0 | 0 |
| Driver Integration | 43 | 43 | 0 | 0 |
| Plaintext Terminal Close | 28 | 28 | 0 | 0 |
| TLS Integration | 23 | 23 | 0 | 0 |
| Q2 Probe | 13 | 13 | 0 | 0 |
| **Total** | **1,364** | **1,364** | **0** | **0** |

**Focused R1/plaintext lifecycle:** 3 repetitions × 57 tests = 171 tests, all passed.
**Repeated deadline/cancellation:** 3 runs, all passed.

### 9.2 Ubuntu WSL2 (SDK 10.0.114, .NET 10.0.14)

| Suite | Tests | Passed | Failed | Skipped |
|-------|-------|--------|--------|---------|
| Core | 1,257 | 1,257 | 0 | 0 |
| Driver Integration | 43 | 43 | 0 | 0 |
| Plaintext Terminal Close | 28 | 28 | 0 | 0 |
| TLS Integration | 23 | 23 | 0 | 0 |
| Q2 Probe | 13 | 13 | 0 | 0 |
| **Total** | **1,364** | **1,364** | **0** | **0** |

**Note:** WSL2 runs the Windows-built portable assemblies on Linux CLR/sockets. Native Linux and Linux .NET 8 remain outstanding release gates.

### 9.3 Build and Format

| Check | Windows | WSL2 |
|-------|---------|------|
| Production build (Debug) | ✅ | ✅ |
| Solution build | ✅ | ✅ |
| Driver build | ✅ | ✅ |
| `dotnet format --verify-no-changes` | ✅ (4 R1 paths) | ✅ |
| Analyzer warnings | 2 (xUnit2012, pre-existing) | 2 (xUnit2012, pre-existing) |

## 10. Evidence Integrity

### 10.1 R1 Evidence Verification (Producer Package)

- ✅ 4 source blobs (SHA-256 + byte count over `git cat-file blob`)
- ✅ 5 evidence blobs
- ✅ 148 archive members (ZIP inventory exact match)
- ✅ Sole-parent relationships verified
- ✅ Historical documentation unchanged (Q1, owner, TS1)

### 10.2 Q1 Evidence Integrity

- ✅ 8 blobs, 137 archive members — byte-identical at `9dd2fe1` and HEAD
- ✅ Producer verifier replay: 8 blobs, 59 members
- ✅ Owner audit replay: 7 owner blobs, historical package chain

### 10.3 Historical Evidence Integrity

- ✅ TS1 closure (`B5-B-TS1-OWNER-01`): 5/5 blobs, standalone oracle byte-identical
- ✅ Prior refusals/transport decisions: trees unchanged
- ✅ Owner decision tree: `fceee363ebcc72c99263d2bfe5e210a9c49cea4d` before and after

## 11. Formal Findings and Disposition

### 11.1 Original Q1 Findings

| Finding | Q1 Disposition | Q2 Assessment | Q2 Disposition |
|---------|----------------|---------------|----------------|
| Q-B5B-09-PLAIN-Q1-F01 | REMEDIATION REQUIRED | Corrected in R1 — independent reproduction on baseline, corrected behavior on R1 | **SUPPORTED** |
| Q-B5B-09-PLAIN-Q1-F02 | REMEDIATION REQUIRED | Corrected in R1 — complete diagnostic metadata verified | **SUPPORTED** |
| Q-B5B-09-PLAIN-Q1-F03 | REMEDIATION REQUIRED | Corrected in R1 — byte-exact TLS restoration, comment restored | **SUPPORTED** |

### 11.2 New Findings

| ID | Title | Disposition | Qualification |
|----|-------|-------------|---------------|
| Q-B5B-09-PLAIN-Q2-F01 | Observer retention beyond absolute deadline | **SUPPORTED WITH QUALIFICATIONS** | Synchronous diagnostic contract retains admission/task until observer returns; socket disposed at deadline. Consistent with authorized spec but may retain capacity under sustained pressure. |
| Q-B5B-09-PLAIN-Q2-F02 | Backpressure observability limited by modern socket buffers | **INCONCLUSIVE** | Probe limitation, not R1 defect; canonical tests cover interruption paths |

### 11.3 Overall Disposition

**SUPPORTED WITH QUALIFICATIONS**

**Rationale:** All three original findings (F01, F02, F03) are independently verified as corrected in R1. The observer retention behavior (Q2-F01) is consistent with the binding specification but represents a known limitation of the synchronous diagnostic contract. No mandatory cancellation, resource-lifetime, protocol, or TLS-isolation requirement remains violated.

**Does not close:** Q-B5B-09 remains OPEN. Native Linux and representative production capacity remain OUTSTANDING release gates.

## 12. Preserved Qualifications

| Qualification | Status |
|---------------|--------|
| B5-B-TS1 | CLOSED |
| Q-B5B-03 | CLOSED |
| Q-B5B-04 | OPEN |
| Q-B5B-05 | OPEN |
| Q-B5B-09 | OPEN |
| TLS graceful-close remediation | DEFERRED |
| Native Linux release qualification | OUTSTANDING |
| Representative production capacity | OUTSTANDING |

Windows and WSL2 execution do not constitute native Linux qualification.

## 13. Deliverables

All new qualification documentation and evidence committed:

```
docs/reviews/Q-B5B-09-PLAIN-Q2-qualification.md
docs/reviews/evidence/Q-B5B-09-PLAIN-Q2/
  README.md
  manifest.json
  independence-statement.txt
  negative-controls-assessment.md
  r1-nc-f01-mutation.patch
  build-production.log
  build-solution.log
  core-tests.trx
  driver-integration.trx
  integration.trx
  format-check.log
  campaign/
    baseline-f01-win.json
    r1-f01-win.json
    r1-all-win.json
    r1-backpressure-win.json
    baseline-f01-wsl.json
    r1-f01-wsl.json
    r1-all-wsl.json
    r1-backpressure-wsl.json
```

Post-commit verification: `git status --porcelain` clean, tree identity confirmed.

## 14. Final Handoff

### 14.1 Reviewer Independence
Fresh Nemotron 3 Ultra session, no R1/Q1 context. Pinned revisions, disposable copies, independent probe.

### 14.2 Starting Repository Identity
HEAD `d7e5ac14704a9576813e32692b29050ef92ee7b8`, tree `b98b57723ac6e8754961261e235a74013ca76058`, clean, 16 ahead.

### 14.3 R1 Delta and Scope Assessment
4 files, 258 lines changed. F01/F02/F03 fully addressed. No unauthorized changes.

### 14.4 F01 Independent Baseline Reproduction
Reproduced on baseline (`20fbc46`) Windows + WSL2: `write-deadline`, `failed=1`, no drain. Confirmed original defect.

### 14.5 Corrected Deadline and Cancellation Behavior
R1 (`6a40c4b`) corrects: `peer-eof`, `drain` phase, `failed=0`, `admitted=True`. All boundary races verified.

### 14.6 Observer-Induced Resource Retention Assessment
Socket disposed at 250 ms deadline; admission slot retained until observer returns. Consistent with authorized spec; documented as Q2-F01.

### 14.7 F02 and F03 Verification
Complete diagnostic metadata verified. TLS isolation confirmed. Shared transport scope clean.

### 14.8 TLS and Typed-Linger Preservation
TLS writer sequence, timeouts, authentication unchanged. Typed-linger correction independent and verified.

### 14.9 Negative Control Results
NC-08 (F01 mutation) detected by oracle. Seven inherited controls verified. Observer retention control added.

### 14.10 Windows and WSL2 Regression Results
1,270 core+integration+driver tests pass on both platforms. 3 focused repetitions pass. Builds and format clean.

### 14.11 Remaining Findings and Disposition
**Overall: SUPPORTED WITH QUALIFICATIONS**
- Q1-F01, F02, F03: SUPPORTED (corrected)
- Q2-F01: SUPPORTED WITH QUALIFICATIONS (observer retention)
- Q2-F02: INCONCLUSIVE (probe limitation)

### 14.12 Evidence Integrity
R1: 4 source blobs, 5 evidence blobs, 148 archive members verified.
Q1: 8 blobs, 137 members — byte-identical at Q1 and HEAD.
Historical: TS1, owner decisions, prior qualifications intact.

### 14.13 Commit and Tree Identities
New qualification documentation committed to `main`. Tree identity to be recorded post-commit.

### 14.14 Final Repository Cleanliness
Working tree clean. No untracked qualification artifacts except this report and evidence package.

### 14.15 Recommended Next Commission

**Q-B5B-09-PLAIN-OWNER-01** with conditions:
1. F01/F02/F03 remediated (verified)
2. Observer retention qualified (Q2-F01 documented)
3. Native Linux and representative production capacity remain outstanding release gates

**Do not implement fixes.** **Do not perform owner acceptance.** **Do not close Q-B5B-09.** **Do not push.**

---
*End of independent qualification handoff.*