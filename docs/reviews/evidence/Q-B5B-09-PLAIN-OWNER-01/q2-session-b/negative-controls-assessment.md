# Q-B5B-09-PLAIN-Q2 Negative Controls Assessment

## Overview

Eight integrated negative controls verified. Seven inherited from Q1, one new (NC-08) targeting F01 mutation sensitivity.

## NC-01: Trailing Command Execution

**Mutation:** Re-enable Ready state after terminal classification; feed next parsed frame to session.

**Detection:** Small and delayed tails hit zero-provider assertion. Exact terminal FAILURE received before assertion.

**Result:** ✅ **PASSED** (Windows)

## NC-02: Socket Retention After Shutdown

**Mutation:** Suppress helper's socket close and enclosing connection's client disposal.

**Detection:** After listener stop, captured handle remained open and valid after StopAsync.

**Result:** ✅ **PASSED** (Windows)

## NC-03: Unlinked Listener Cancellation

**Mutation:** Remove listener token registration in terminal owner.

**Detection:** Stop ran while admitted drain awaited real peer input. Ended on independent drain deadline instead of listener-shutdown.

**Result:** ✅ **PASSED** (Windows)

## NC-04: Deadline Renewal

**Mutation:** Reset total timer at drain entry, use fresh drain elapsed origin.

**Detection:** Stalled drain ran to its own timer. Original deadline oracle expected total-deadline, observed drain-deadline.

**Result:** ✅ **PASSED** (Windows)

## NC-05: Drain Limit Violation

**Mutation:** Increase 8 KiB byte cap to 12 KiB.

**Detection:** Large tail exhausted 12,288 bytes against 8,192-byte expectation.

**Result:** ✅ **PASSED** (Windows)

## NC-06: Drain Capacity Violation

**Mutation:** Increase limit to two permits (clamped to MaxConnections - 1).

**Detection:** At connection limit 4, three contenders received two permits against expected one.

**Result:** ✅ **PASSED** (Windows)

## NC-07: Incorrect Transport Mode

**Mutation:** Permit helper entry on TLS with encrypted writer and zero-permit policy.

**Detection:** Peer received exact encrypted refusal; forbidden plaintext-helper events appeared. No raw TLS-owned data read.

**Result:** ✅ **PASSED** (Windows)

## NC-08: F01 Mutation Sensitivity (NEW)

**Mutation:** Restore original write-timeout-through-observer behavior:
1. `writeTimer.Token.UnsafeRegister(_ => InterruptWrite("write-deadline"), null)` → `owner.Interrupt("write-deadline")`
2. Move `messageProcessed()` call before `phase = "post-write"`
3. Remove post-write total-deadline check

**Detection:** F01 baseline test oracle fires:
- `reason=write-deadline`, `phase=write`, `elapsed=146 ms`, `failed=1`, `admitted=False`
- Both `F01_R1_CorrectedBehavior` and `NegativeControl_OriginalF01Defect_DetectedByOracle` oracles trigger

**Result:** ✅ **PASSED** (Windows and WSL2)

## Clean Control (NC-00)

All controls tree tests pass before and after mutation series (28 PlaintextTerminalCloseTests, 1,264+ core tests).

## Observer Retention Control (NEW)

**Test:** `ObserverRetention_IndefinitelyBlocked_DoesNotPreventSubsequentConnections`

**Purpose:** Verify oracle sensitivity to admission-slot retention by blocked observers.

**Result:** ✅ **PASSED** (Windows) — detects active=3, completed=0 while observer blocked; slots released after gate release.

## Summary

| Control | Platform | Status |
|---------|----------|--------|
| NC-01 | Windows | ✅ PASSED |
| NC-02 | Windows | ✅ PASSED |
| NC-03 | Windows | ✅ PASSED |
| NC-04 | Windows | ✅ PASSED |
| NC-05 | Windows | ✅ PASSED |
| NC-06 | Windows | ✅ PASSED |
| NC-07 | Windows | ✅ PASSED |
| NC-08 (F01 mutation) | Windows, WSL2 | ✅ PASSED |
| NC-00 (clean) | Windows | ✅ PASSED |
| Observer retention | Windows | ✅ PASSED |

All eight negative controls detect their intended faults. The F01 mutation (NC-08) is independently detected by the qualification oracle on both platforms.