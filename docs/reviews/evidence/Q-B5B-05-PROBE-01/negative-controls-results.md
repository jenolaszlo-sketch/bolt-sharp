# Negative-Control Results — Q-B5B-05-PROBE-01

Producer: [`Q-B5B-05-PROBE-01-producer-report.md`](../../../reviews/Q-B5B-05-PROBE-01-producer-report.md)

Commission §9 requires that the same-connection proof reject false-positive recovery
scenarios. Each control below documents the intended defect, the injection, the observed
execution path, the detecting oracle and the result. No canonical production behavior is
modified: the listener always runs the accepted production engine, and the only mutations live
in probe-owned code inside the probe's own test process.

## NC-01 — Replacement connection

| Field | Value |
|---|---|
| Intended defect | Present a subsequent successful operation performed on a **different physical connection** as same-connection recovery |
| Injection | None on the server. The probe performs the recoverable failure and the RESET on one client socket, disposes it, and then performs the subsequent operation on a **new** `ProbeClient` that reconnects, re-handshakes and re-authenticates |
| Observed execution path | Server `accepted=2`; two distinct connection ids (`conn-0001`, `conn-0002`); two per-connection provider instances; two distinct client local ports |
| Detecting oracle | `ConnectionsAccepted == 2`, two accepted ids, two provider instances and two distinct local ports — any one of which alone falsifies same-connection recovery |
| Result | **Detected** — the oracle's identity line fails |

## NC-02 — Missing RESET

| Field | Value |
|---|---|
| Intended defect | Report valid recovery although the RESET was never sent |
| Injection | None on the server. The probe omits the RESET after the recoverable failure and attempts the subsequent operation directly |
| Observed execution path | The subsequent `RUN` is answered with the byte-exact zero-field `IGNORED B0 7E`; the session remains failed. The probe then sends the RESET, `RESET → SUCCESS`, and the operation succeeds — recovery only follows the RESET itself |
| Detecting oracle | The expected `B3 10` RUN response is `B0 7E` IGNORED rather than a `SUCCESS`; the provider is not consulted (`PrepareCount` stays at 1 for the refused RUN until the RESET is sent) |
| Result | **Detected** — no valid recovery can be reported without the RESET |

## NC-03 — Incorrect recovery response or state

| Field | Value |
|---|---|
| Intended defect | Present a recovery attempt that does **not** answer the successful RESET response as valid recovery |
| Injection | Probe-owned `IBoltOperationProvider` whose operation's `Dispose` throws exactly once, injected in the probe test process. The probe establishes an outstanding result and then sends RESET, so the session must retire the operation to recover |
| Observed execution path | `RESET → FAILURE` with `50N00` and `neo4j_code = Neo.DatabaseError.General.UnknownError`; the session becomes terminal, no further message is processed, and the connection closes (`accepted=1 completed=1`) |
| Detecting oracle | The RESET response hex is `B1 7F …` and never `B170A0`; `DisposeFailures == 1`; no subsequent response arrives on the still-open socket |
| Result | **Detected** — the RESET oracle rejects the non-SUCCESS recovery |

## NC-04 — Terminal failure substituted for a recoverable failure

| Field | Value |
|---|---|
| Intended defect | Present a terminal Defunct failure as a recoverable failure so that the recovery proof could be satisfied |
| Injection | None on the server. The probe sends `BEGIN B1 11 A0` — a valid Bolt 5.8 signature this bounded server does not implement, whose refusal is terminal (`08N06`) with immediate closure |
| Observed execution path | `BEGIN → FAILURE 08N06`, then a RESET written to the closing socket produces **no response at all**; the provider is never consulted (`PrepareCount == 0`) |
| Detecting oracle | The expected `B3 10`/RESET/SUCCESS recovery sequence cannot exist: the response to RESET is `null` (nothing), and the production contract for a Defunct session is that no further message is processed |
| Result | **Detected** — the inadmissible failure classification cannot satisfy the recovery proof |

## Summary

| Control | Result |
|---|---|
| NC-01 replacement connection | **Detected** |
| NC-02 missing RESET | **Detected** |
| NC-03 incorrect recovery response/state | **Detected** |
| NC-04 terminal failure substituted | **Detected** |