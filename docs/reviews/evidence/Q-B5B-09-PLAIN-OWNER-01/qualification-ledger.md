# Remaining Qualification Ledger — Q-B5B-09-PLAIN-OWNER-01

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

## 1. Dispositions after this owner decision

Two independent Q2 qualifications (`96a1a3c` session A, `3c2144a` session B) were completed
in parallel and are both preserved and reconciled. They are unanimous on the overall
disposition and on every finding disposition; see
[`q2-reconciliation.md`](q2-reconciliation.md).

| Item | Status before | Status after |
|---|---|---|
| Plaintext graceful-close remediation | OPEN (pending qualification) | **ACCEPTED WITH QUALIFICATIONS — CLOSED** |
| Portable typed-linger correction | OPEN | **ACCEPTED WITH QUALIFICATIONS** (Windows and WSL2 scope) |
| `Q-B5B-09-PLAIN-Q1-F01/F02/F03` | REMEDIATION REQUIRED | **CLOSED BY VERIFIED REMEDIATION** |
| TLS graceful-close remediation | DEFERRED | DEFERRED (unchanged) |
| Parent `Q-B5B-09` | OPEN | **OPEN (unchanged)** |

## 2. Preserved inherited qualifications

| Qualification | Status | Note |
|---|---|---|
| B5-B-TS1 | CLOSED | unchanged by this decision |
| Q-B5B-03 | CLOSED | closed by the earlier F01 R1 verification |
| Q-B5B-04 | OPEN | generic refusal/error classification; refinement optional |
| Q-B5B-05 | OPEN | same-connection RESET recovery; recommended as §4 |
| Q-B5B-01 / Q-B5B-02 / Q-B5B-06 / Q-B5B-07 / Q-B5B-08 | as previously accepted | unchanged |
| All B5-B-TS1, B0-A, B0-B, B2-*, B3-* dispositions | as previously recorded | unchanged |

## 3. Outstanding release gates

| Gate | Status | Blocking scope |
|---|---|---|
| Native Linux qualification | OUTSTANDING | release |
| Representative production capacity characterization | OUTSTANDING | release |
| Deferred TLS graceful-close design and qualification | DEFERRED | release |
| TLS-specific resource and backpressure requirements | OUTSTANDING | release |
| Security review, public API stability, performance budgets | OUTSTANDING | release (B6) |
| Observer isolation (backlog item) | BACKLOG | not gating; requires separate authorization |

## 4. Roadmap position after this decision

Remaining B5-B-roadmap items, in increasing scope:

1. **`Q-B5B-05`** — same-connection RESET recovery (recommended next commission; bounded
   driver-level probe, no production change).
2. **`Q-B5B-04`** — generic refusal/error classification refinement (optional; requires a
   production classification change).
3. **B4 Browser/WebSocket entry** — larger entry scope.
4. **B6 hardening/packaging/release qualification** — carries the outstanding release gates.
5. Blocked-write backpressure (`Q-B5A-06`) — part of release hardening.
6. TLS policy and the deferred TLS graceful-close investigation — deferred, not reopened.
7. Observer-isolation backlog item — not a roadmap entry; requires separate authorization.