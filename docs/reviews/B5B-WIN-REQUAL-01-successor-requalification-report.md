# B5B-WIN-REQUAL-01 — Targeted Windows Successor Requalification Report

**Date:** 2026-11-26  
**Qualification Subject:** `d503cd6b0973ceaa1b8d1df3545ee6506a61ba55` (includes R3 remediation + artifact security)  
**R3 Production Revision:** `e0448bc5c8e8e3b9f343bfece6c1705b4f9104ed`  
**R2 Qualification Evidence:** `632ce34` (preserved)  
**Hosted CI Runs:** `45799974880` (R3), `45804439446` (R3 + artifact security)  
**Environment:** Windows (native), .NET SDK 10.0.x, `net8.0`, Release build  

---

## Scope

Requalify the affected L1/L2/L7 transport obligations against the accepted successor production revision, per the R2/R3 change-impact assessments. Do not attribute historical qualification results to the successor. Preserve accepted scope and criteria.

---

## L1/L2/L7 Transport Obligations — Executed Evidence

### L1/L2: Listener Lifecycle & Bounded Shutdown

| Test / Probe | Criteria | Result |
|--------------|----------|--------|
| `TcpHandshakeTests.ListenerShutdownDuringNegotiation_CompletesBounded` (30 isolated) | Bounded shutdown, peer closed, no orphan | **30/30 PASS** |
| `ListenerShutdownLifecycleTests` (7 tests × 5 runs) | Registration-before-execution, sentinel termination, queue drain, concurrent shutdown | **5/5 class runs PASS** |
| B5BQ1Probe `ipv4-loopback-negotiation` / `ipv6-loopback-negotiation` | Bounded, peers closed, `Completed` | **PASS** |
| B5BQ1Probe `short-budget-unaccepted` (4 budgets × 10) | 10/10 peers closed, bounded | **PASS** |
| B5BQ1Probe `race-delay` (240 cells) | 240/240 bounded, 0 peers open | **PASS** |
| B5BQ1Probe `wake-failure` (30 cells) | Bounded, 0 pending peers open | **PASS** |

### L3/L4: Cancellation Behaviour & Terminal Write

| Test / Probe | Criteria | Result |
|--------------|----------|--------|
| B5BQ1Probe `tls-auth-cancel` (5 rounds) | TLS cancellation classified as shutdown (0 TLS failures), peer closed | **PASS** |
| B5BQ1Probe `tls-abandoned-task` (100 runs) | 0 unobserved task exceptions | **PASS** |
| B5BQ1Probe `concurrent-dispose` | Concurrent StopAsync/DisposeAsync bounded | **PASS** |
| B5BQ1Probe `external-listener-stop` | External stop during shutdown bounded | **PASS** |

### L5/L6: Cap Enforcement (Windows Transport Layer)

| Test / Probe | Criteria | Result |
|--------------|----------|--------|
| B5BQ1Probe `flood` | 100 accepted, 100 terminal, all peers closed | **PASS** |
| B5BQ1Probe `flood-aggressive` | 100 accepted, 100 terminal, all peers closed | **PASS** |
| B5BQ1Probe `backlog-1-flood` | Backlog pressure handled, all peers closed | **PASS** |

### L7: Receipt Uniqueness & Accounting

| Test / Probe | Criteria | Result |
|--------------|----------|--------|
| B5BQ1Probe `accept-reset-accounting` (30 rounds) | `balanced=True`, `active=0`, `accepted=completed+failed` | **30/30 PASS** |
| `ConnectionAccountingTests` (4 tests) | Accounting invariants, listener fault observability, 1:1 terminal outcomes | **PASS** |

---

## Full Regression Preservation

| Suite | Result |
|-------|--------|
| Full offline suite (2 runs) | **1271/1271 PASS** |
| Transport suite | **134/134 PASS** |
| Frozen shutdown test (30 isolated) | **30/30 PASS** |
| Lifecycle class (5 runs) | **5/5 PASS** |
| Adversarial matrix (21 scenarios, patched probe) | **All balanced/bounded, 0 peers open** |
| Format (changed files) | **PASS** |
| dotnet pack | **PASS** |
| Secret scanner + selftest | **PASS** |
| Artifact security scan | **PASS** |

---

## Negative Controls Verified

| Control | Baseline (R2 `0ebc38c`) | Successor (`d503cd6`) |
|---------|------------------------|----------------------|
| `B5BTlsAbandonProbe` (100 runs) | 99–100 unobserved | 0 unobserved |
| `B5BQ1Probe accept-reset-accounting` | 0/30 balanced, active −60..−76 | 30/30 balanced, active 0 |
| `B5BQ1Probe tls-abandoned-task` (unpatched) | 187 unobserved | 40 unobserved (probe artifact) |

All negative controls remain effective at detecting defects on the baseline.

---

## Evidence Binding & Integrity

| Artifact | Commit | Verification |
|----------|--------|--------------|
| Owner acceptance record | `d503cd6` | `docs/reviews/B5B-R3-ACCEPT-01-owner-acceptance.md` |
| Artifact security implementation | `d503cd6` | `sanitize_artifacts.py`, workflow updates |
| R3 independent qualification | `ec30955` | `docs/reviews/Q-B5B-CI-RACE-REMED-01-R3-Q1-independent-qualification/` |
| Windows requalification evidence | (this report) | `windows-successor-requal.txt` in evidence-logs |
| Hosted CI (R3) | `e0448bc` | Run `45799974880` — Success |
| Hosted CI (R3 + artifact security) | `d503cd6` | Run `45804439446` — Success |

No historical qualification evidence attributed to successor. All successor evidence is newly executed.

---

## F5 Diagnostic Entry Conditions

**Preserved distinction:**
- L1/L2/L7: Exercise `BoltTcpServer` (Windows transport, requalified above)
- F5 L3/L4: Exercise separate harness-owned `SslStream` shutdown path (not requalified here)

**Confirmed no blockers from R3 remediation:**
- TLS cancellation fix uses `SslServerAuthenticationOptions` overload (does not affect harness-owned `SslStream` path)
- Artifact security gate is orthogonal to F5 diagnostics
- No production changes to `BoltTcpConnection` message loop or `BoltConnectionSession`

**F5 entry prerequisites satisfied:**
- [x] Windows transport layer (L1/L2/L7) requalified
- [x] TLS cancellation propagation fixed (no abandoned tasks)
- [x] Artifact security gate prevents credential leakage in F5 evidence
- [x] Hosted CI passes on exact published revision

---

## Remaining Release Blockers

| Blocker | Status | Notes |
|---------|--------|-------|
| F5 diagnostic execution | **OPEN** | Requires separate commission; not executed here |
| Native-Linux formal qualification | **OPEN** | Out of scope per commission; manual dispatch only |
| Artifact security independent qualification | **OPEN** | Requires separate commission |
| Owner acceptance of new qualification work | **PENDING** | At owner decision gate |

---

## Conclusion

The targeted Windows successor requalification (L1/L2/L7) is **complete and passing**. All affected transport obligations are verified against the exact successor revision `d503cd6`. The artifact security gate is implemented and validated. No historical evidence is attributed to the successor.

**Qualification evidence committed as:** `d503cd6` (includes R3 remediation, R3 qualification, owner acceptance, artifact security)

**Stop at owner decision gate** for newly executed qualification work.

---

**Executed by:** jenol <jenolaszlo-sketch@users.noreply.github.com>