# Backlog Item — Observer Isolation and Diagnostic Resource Lifetime

Owner decision: [`Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md`](../../../decisions/Q-B5B-09-PLAIN-OWNER-01-owner-acceptance.md)

**Status: BACKLOG — not authorized, not gating.**

## Recorded observations (independently verified)

Two independent qualifications measured the same behaviour and reached the same
classification.

- A synchronous `MessageProcessed` observer can outlive socket disposal.
- The connection task and the admission slot remain occupied until the observer returns.
- A blocked observer can cause subsequent healthy connections to be refused for capacity
  (`MaxConnections` is exhausted by already-accepted connections).
- `StopAsync` can return at its configured shutdown deadline with an active connection task
  remaining; it still ends admission and reports the residual count.
- Accounting and retained resources settle once the observer returns.
- Equivalent retention was demonstrated against the original plaintext implementation, so the
  behaviour is inherited rather than introduced by the remediation.

## Why it is accepted rather than remediated

The binding specification explicitly preserves admission ownership through disposal and task
retirement, scopes the lifecycle deadline to I/O (serialization through teardown), and states
that an arbitrary synchronous provider call remains outside the timer's preemptive control.
The observed behaviour is consistent with that contract. The residual risk is operational: a
permanently blocked observer retains admission capacity indefinitely, because the bounded
quantity is the resource count (`MaxConnections`) and not the retention duration.

## Scope of a future commission (requires separate owner authorization)

- Bounded diagnostic dispatch (how far delivery may be deferred without violating the
  synchronous observation contract).
- Observer isolation (whether observation may run off the connection task).
- Admission-slot reclamation (whether the slot may be released before the observer returns,
  and what that means for accounting).
- Diagnostic ordering and delivery semantics (per-connection ordering, correlation identity,
  loss or drop policy when bounded).
- Subscriber exception and shutdown policies (current containment versus fail-closed
  propagation, and behaviour during listener shutdown).

## Explicit non-authorizations

This backlog item does not authorize production change, does not reopen the deferred TLS
graceful-close investigation, and does not authorize an asynchronous diagnostic redesign. Any
future implementation requires a separate owner decision naming a reviewer and an
implementation specification.

It also does not create the `Q-B5B-09-PLAIN-OBS-01.md` proposal referenced by the parallel
Q2 qualification's report; that document does not exist at any pinned revision
(discrepancy D-05). This backlog item is the recorded, non-authorized item.