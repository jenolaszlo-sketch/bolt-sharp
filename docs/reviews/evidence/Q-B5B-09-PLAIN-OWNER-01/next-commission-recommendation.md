# Recommended Next Commission — Q-B5B-09-PLAIN-OWNER-01

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

## Recommendation

**`Q-B5B-05` — same-connection RESET-recovery demonstration.**

Both independent Q2 qualifications independently recommended this decision as the next step;
this record selects the smallest bounded commission within the existing B5-B roadmap.

## Statement

After the observed FAILURE/IGNORED sequence, the official driver destroys the physical
connection. Whether the server would accept a `RESET` and continue the *same* connection was
never demonstrated, so `Q-B5B-05` remains an OPEN inherited B5-B qualification. This is a
bounded qualification probe, not an implementation task.

## Why this is the smallest appropriate next commission

- It closes an already-open inherited qualification rather than opening new scope.
- It needs no production change, no public API change, no new dependency and no new package.
- Its assets already exist and are verified: the CLOSED B5-B-TS1 byte oracle, the existing
  official-driver interop suite (`Neo4j.Driver` 6.4.0), the `docs/reviews/probes` harness,
  and the B5-B multi-client/lifecycle isolation tests.
- It is directly measurable at the wire level: `RESET` after the observed sequence, then a
  second authenticated exchange on the same socket.

## Prerequisites

1. A recorded commissioning decision naming the probe owner and an independent reviewer.
2. Reuse of the existing driver-interop harness at `Neo4j.Driver` 6.4.0 and the existing
   loopback / `bolt+ssc://` isolated-trust arrangements.
3. The standing qualification conventions: independent reviewer, negative controls, retained
   failures and skips, self-verifying evidence, and Windows + WSL2 execution.
4. Acceptance of the scope limit: this demonstrates server-side RESET handling only. If the
   driver destroys the connection, that is an observable outcome, not a defect.

## Alternatives considered and rejected

| Option | Why not now |
|---|---|
| `Q-B5B-04` (F02) refusal/error classification refinement | Requires a production classification change and broad re-qualification; explicitly optional. |
| B4 Browser/WebSocket entry | Larger entry scope than the smallest bounded action. |
| B6 hardening/packaging/release qualification | Carries the outstanding release gates (native Linux, production capacity, TLS policy); not a single bounded step. |
| Reopen the deferred TLS graceful-close investigation | Explicitly dispreferred by the owner decision. |
| Observer-isolation backlog item | Requires separate authorization and a design decision; explicitly not initiated. |

## Non-authorization

This recommendation authorizes nothing. The next implementation requires its own commission.